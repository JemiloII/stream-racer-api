// Game.Drift: !drift. Arms a car for its next corner (costs one pool boost, refunded if no corner comes), then on
// corner entry loosens the rear wheels' sideways grip and swings the tail out with a yaw push so the car slides
// through (the CarController draws smoke / skid marks / screech by itself), restores the grip exactly on exit and
// fires a mini-turbo that grows with the drift's length. Chat: settings.driftCommand; API: POST /drift/:x.
using System.Collections;
using HarmonyLib;

namespace StreamRacerApi;

static partial class Game
{
    // ---- tuning knobs (untested in a live race: expect to adjust these) ----
    const float DriftArmSeconds = 10f;          // armed this long waiting for a corner, then the boost is refunded
    const float DriftCornerLook = 60f;          // route units ahead checked for a corner (real maps turn ~35° per 60, rarely per 30)
    const float DriftCarve = 5f;                // 1/s: how hard the slide's travel is pulled along the road (a corner boost)
    const float DriftCarveLook = 8f;            // route units ahead whose direction the travel is pulled toward
    const float DriftCentring = 0.06f;          // per unit off the road's centre line, steer back toward it
    const float DriftCarrySpeed = 1.05f;        // the slide keeps at least this x its entry speed
    const float DriftGripReturn = 0.5f;         // seconds the rear grip takes to come back (no snap, no flop)
    const float DriftExitSpinDamp = 0.85f;      // per physics step while the grip returns: kills the leftover spin
    const float DriftBendLook = 25f;            // ... and the bend must already have begun: this many units ahead
    const float DriftBendDegrees = 12f;         // turn at least this much (else the slide starts on the straight)
    const float DriftTurboAlignedSlip = 12f;    // the mini-turbo waits until the nose points where the car goes
    const float DriftTurboWait = 1f;            // seconds it waits for that before giving up (no turbo into a wall)
    const float DriftEnterDegrees = 35f;        // the road must turn at least this much over DriftCornerLook to start
    const float DriftExitLook = 40f;            // route units ahead checked while sliding
    const float DriftExitDegrees = 8f;         // the slide ends once the road ahead turns less than this (its way)
    const float DriftMinSpeed = 10f;            // m/s: slower cars wait (a slow car only spins)
    const float DriftRearGrip = 0.3f;          // rear wheels' sideways stiffness x this while sliding
    const float DriftSteerHelper = 0f;          // CarController.m_SteerHelper x this while sliding (it re-aligns velocity
                                                // with the nose every step, which straightens a drift out)
    const float DriftYawPush = 2.4f;            // rad/s: the minimum yaw rate into the turn until the target slip angle
    const float DriftTargetSlip = 22f;          // degrees between nose and direction of travel the push aims for
    const float DriftMaxSlip = 35f;             // degrees: past this the car may not rotate further in (slide, not spin)
    const float DriftRealignedSlip = 6f;        // degrees: a slide that has straightened back to this is over
    const float DriftMinSeconds = 0.8f;         // a slide always lasts this long before an exit is considered
    const float DriftMaxSeconds = 2.5f;         // and never longer than this
    const float DriftTurboMinSlide = 0.5f;      // seconds of sliding before any mini-turbo is earned
    const float DriftTurboFullSlide = 2f;       // seconds of sliding for the biggest mini-turbo
    const float DriftTurboMinForce = 3f;        // Car.Boost force of the smallest mini-turbo (the game's !boost: 2.5..15)
    const float DriftTurboMaxForce = 9f;        // ... and of the biggest
    const float DriftTurboSeconds = 1.2f;       // how long the mini-turbo pushes

    public const string NoBoostsForDrift = "no boosts left";

    sealed class DriftState
    {
        public Vehicle Vehicle;
        public bool Paid;
        public float ArmedUntil;
        public bool Sliding;
        public float StartedAt;
        public float TurnSign;                   // +1 = right-hand corner, -1 = left
        public Rigidbody Body;
        public WheelCollider[] RearWheels;
        public WheelFrictionCurve[] SavedSideways;
        public CarController Controller;
        public float SavedSteerHelper;
        public float EntrySpeed;
        public bool GameOwnsGrip;                // GripFor started mid-slide: it rewrote all wheels, restores them itself
    }

    static readonly Dictionary<Vehicle, DriftState> Drifts = new();
    static readonly System.Reflection.FieldInfo SteerHelperField = AccessTools.Field(typeof(CarController), "m_SteerHelper");

    // Null = armed; otherwise why not. free = no pool boost taken (API ?free=1).
    public static string ArmDrift(Vehicle vehicle, bool free)
    {
        if (!Running)
        {
            return "no race running";
        }
        if (!Driving(vehicle))
        {
            return "not driving";
        }
        string blocked = BoostBlocker(vehicle);
        if (blocked != null && blocked != "off the road" && blocked != "in the air")
        {
            return blocked;   // finished / boomed / flipped: refuse now; airborne or off-road cars may land before the corner
        }
        if (Drifts.ContainsKey(vehicle))
        {
            return "already armed";
        }
        if (!free && Boosts(vehicle) <= 0)
        {
            return NoBoostsForDrift;
        }
        if (!free)
        {
            AddBoosts(vehicle, -1);
        }
        var drift = new DriftState
        {
            Vehicle = vehicle,
            Paid = !free,
            ArmedUntil = Time.time + DriftArmSeconds,
        };
        Drifts[vehicle] = drift;
        EmitDrift(vehicle, "armed", DriftArmSeconds, 0f);
        Plugin.Instance.StartCoroutine(RunDrift(drift));
        return null;
    }

    // The chat command: the typer's own car only, paid from their pool.
    static void OnDriftCommand(string login, string displayName)
    {
        var vehicle = Find(login);
        if (vehicle == null || !Running)
        {
            return;
        }
        if (ArmDrift(vehicle, false) != NoBoostsForDrift)
        {
            return;
        }
        Plugin.Emit("denied", new
        {
            login,
            displayName,
            command = "drift",
            reason = NoBoostsForDrift,
            boosts = 0,
        });
        Reply(Pure.DriftNoBoostsReply(displayName));
    }

    // Race over: every armed or sliding car gets its own grip back.
    public static void StopDrifts()
    {
        foreach (var drift in Drifts.Values.ToList())
        {
            EndDrift(drift, false);
        }
    }

    static bool Driving(Vehicle vehicle)
    {
        if (vehicle.Car() == null || vehicle.HasFinished())
        {
            return false;
        }
        var ai = vehicle.Car().GetComponent<CarAIControl>();
        return ai != null && ai.IsDriving();
    }

    static IEnumerator RunDrift(DriftState drift)
    {
        while (Drifts.TryGetValue(drift.Vehicle, out var current) && current == drift && StepDrift(drift))
        {
            yield return new WaitForFixedUpdate();
        }
    }

    // One physics step. False = the drift is over (and already cleaned up).
    static bool StepDrift(DriftState drift)
    {
        try
        {
            if (!Running || drift.Vehicle.Car() == null || drift.Vehicle.HasFinished())
            {
                EndDrift(drift, false);
                return false;
            }
            return drift.Sliding ? Slide(drift) : WaitForCorner(drift);
        }
        catch (System.Exception error)
        {
            Plugin.Log.LogWarning("drift: " + error.Message);
            EndDrift(drift, false);
            return false;
        }
    }

    static bool WaitForCorner(DriftState drift)
    {
        if (Time.time > drift.ArmedUntil)
        {
            EndDrift(drift, false);
            return false;
        }
        var vehicle = drift.Vehicle;
        // GripFor running = the game owns the friction right now: wait it out rather than fight it.
        if (vehicle.GripActive() || !Driving(vehicle) || !ReadyToBoost(vehicle))
        {
            return true;
        }
        var body = vehicle.Car().GetComponent<Rigidbody>();
        if (body == null || FlatSpeed(body) < DriftMinSpeed)
        {
            return true;
        }
        float turn = RouteTurnAhead(vehicle.Progress(), DriftCornerLook);
        float bend = RouteTurnAhead(vehicle.Progress(), DriftBendLook);
        bool cornerNow = Mathf.Abs(turn) >= DriftEnterDegrees && Mathf.Abs(bend) >= DriftBendDegrees && Mathf.Sign(bend) == Mathf.Sign(turn);
        if (cornerNow && !StartSlide(drift, body, Mathf.Sign(turn)))
        {
            EndDrift(drift, false);
            return false;
        }
        return true;
    }

    static bool StartSlide(DriftState drift, Rigidbody body, float turnSign)
    {
        var car = drift.Vehicle.Car();
        var rear = RearWheels(car.transform, car.GetComponent<Car>()?.Wheels());
        if (rear.Length == 0)
        {
            return false;
        }
        drift.Vehicle.SaveGripBaseline();   // the game's one-time snapshot must be the car's own grip, never ours
        drift.RearWheels = rear;
        drift.SavedSideways = rear.Select(wheel => wheel.sidewaysFriction).ToArray();
        for (int i = 0; i < rear.Length; i++)
        {
            var curve = drift.SavedSideways[i];
            curve.stiffness *= DriftRearGrip;
            rear[i].sidewaysFriction = curve;
        }
        drift.Controller = car.GetComponent<CarController>();
        if (drift.Controller != null && SteerHelperField != null)
        {
            drift.SavedSteerHelper = (float)SteerHelperField.GetValue(drift.Controller);
            SteerHelperField.SetValue(drift.Controller, drift.SavedSteerHelper * DriftSteerHelper);
        }
        drift.Body = body;
        drift.EntrySpeed = FlatSpeed(body);
        drift.TurnSign = turnSign;
        drift.StartedAt = Time.time;
        drift.Sliding = true;
        EmitDrift(drift.Vehicle, "start", 0f, 0f);
        return true;
    }

    // Rear = the wheels behind the middle of the wheelbase (local z), whatever order the prefab lists them in.
    static WheelCollider[] RearWheels(Transform car, WheelCollider[] wheels)
    {
        var present = (wheels ?? new WheelCollider[0]).Where(wheel => wheel != null).ToArray();
        if (present.Length < 2)
        {
            return new WheelCollider[0];
        }
        float middle = present.Average(wheel => car.InverseTransformPoint(wheel.transform.position).z);
        return present.Where(wheel => car.InverseTransformPoint(wheel.transform.position).z < middle).ToArray();
    }

    static bool Slide(DriftState drift)
    {
        if (drift.Vehicle.GripActive())
        {
            drift.GameOwnsGrip = true;
            EndDrift(drift, true);
            return false;
        }
        if (!Driving(drift.Vehicle))
        {
            EndDrift(drift, false);   // boomed mid-slide: no turbo
            return false;
        }
        var body = drift.Body;
        float elapsed = Time.time - drift.StartedAt;
        float turn = RouteTurnAhead(drift.Vehicle.Progress(), DriftExitLook) * drift.TurnSign;
        float slip = SlipDegrees(body) * drift.TurnSign;
        if (Pure.DriftOver(elapsed, turn, slip, DriftMinSeconds, DriftMaxSeconds, DriftExitDegrees, DriftRealignedSlip))
        {
            EndDrift(drift, true);
            return false;
        }
        var spin = body.angularVelocity;
        float rateIntoTurn = Pure.DriftYawRate(slip, spin.y * drift.TurnSign, DriftTargetSlip, DriftMaxSlip, DriftYawPush);
        spin.y = drift.TurnSign * rateIntoTurn;
        body.angularVelocity = spin;
        Carve(drift.Vehicle, body, Mathf.Max(FlatSpeed(body), drift.EntrySpeed * DriftCarrySpeed));
        return true;
    }

    // The corner boost: the nose is angled for show, but the travel follows the road (pulled toward its direction a
    // little ahead, and back toward the centre line), at no less than `speed`.
    static void Carve(Vehicle vehicle, Rigidbody body, float speed)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null)
        {
            return;
        }
        var (here, _) = RouteSample(circuit, vehicle.Progress());
        var (_, ahead) = RouteSample(circuit, vehicle.Progress() + DriftCarveLook);
        var along = new Vector3(ahead.x, 0f, ahead.z).normalized;
        var right = Vector3.Cross(Vector3.up, along);
        float offCentre = Vector3.Dot(body.position - here, right);
        var wanted = (along - right * offCentre * DriftCentring).normalized * speed;
        var flat = new Vector3(body.velocity.x, 0f, body.velocity.z);
        flat = Vector3.Lerp(flat, wanted, Mathf.Clamp01(DriftCarve * Time.fixedDeltaTime));
        body.velocity = new Vector3(flat.x, body.velocity.y, flat.z);
    }

    // Armed and never slid: refund. Slid: grip back exactly, then the mini-turbo if it was a clean exit.
    static void EndDrift(DriftState drift, bool clean)
    {
        if (Drifts.TryGetValue(drift.Vehicle, out var current) && current == drift)
        {
            Drifts.Remove(drift.Vehicle);
        }
        if (!drift.Sliding)
        {
            if (drift.Paid && Running && drift.Vehicle.Car() != null)
            {
                AddBoosts(drift.Vehicle, 1);
            }
            EmitDrift(drift.Vehicle, "refund", 0f, 0f);
            return;
        }
        if (clean)
        {
            Plugin.Instance.StartCoroutine(EaseOutOfSlide(drift));
        }
        else
        {
            RestoreGrip(drift);
        }
        float seconds = Time.time - drift.StartedAt;
        float turbo = clean
            ? Pure.DriftTurbo(seconds, DriftTurboMinSlide, DriftTurboFullSlide, DriftTurboMinForce, DriftTurboMaxForce)
            : 0f;
        EmitDrift(drift.Vehicle, "end", seconds, turbo);
        if (turbo > 0f)
        {
            Plugin.Instance.StartCoroutine(TurboWhenStraight(drift.Vehicle, turbo));
        }
    }

    // The mini-turbo fires once the car points where it is going; a car still sideways (or spinning) gets none.
    static IEnumerator TurboWhenStraight(Vehicle vehicle, float force)
    {
        for (float waited = 0f; waited < DriftTurboWait; waited += Time.deltaTime)
        {
            var body = vehicle.Car() != null ? vehicle.Car().GetComponent<Rigidbody>() : null;
            if (body == null || !Running)
            {
                yield break;
            }
            if (Mathf.Abs(SlipDegrees(body)) <= DriftTurboAlignedSlip)
            {
                Boost(vehicle, force, DriftTurboSeconds);
                yield break;
            }
            yield return null;
        }
    }

    // A clean exit: the rear grip comes back over DriftGripReturn while the leftover spin is damped and the travel
    // still follows the road, then the exact saved grip is put back (a snap back to full grip flipped cars into walls).
    static IEnumerator EaseOutOfSlide(DriftState drift)
    {
        var body = drift.Body;
        for (float elapsed = 0f; elapsed < DriftGripReturn; elapsed += Time.fixedDeltaTime)
        {
            if (body == null || drift.GameOwnsGrip || drift.Vehicle.GripActive() || !Running)
            {
                break;
            }
            float back = Mathf.Lerp(DriftRearGrip, 1f, elapsed / DriftGripReturn);
            for (int i = 0; i < drift.RearWheels.Length; i++)
            {
                if (drift.RearWheels[i] != null)
                {
                    var curve = drift.SavedSideways[i];
                    curve.stiffness *= back;
                    drift.RearWheels[i].sidewaysFriction = curve;
                }
            }
            var spin = body.angularVelocity;
            spin.y *= DriftExitSpinDamp;
            body.angularVelocity = spin;
            Carve(drift.Vehicle, body, FlatSpeed(body));
            yield return new WaitForFixedUpdate();
        }
        if (drift.Vehicle.GripActive())
        {
            drift.GameOwnsGrip = true;   // the game rewrote the friction and restores it; we still put the steer helper back
        }
        RestoreGrip(drift);
    }

    static void RestoreGrip(DriftState drift)
    {
        if (drift.Controller != null && SteerHelperField != null)
        {
            SteerHelperField.SetValue(drift.Controller, drift.SavedSteerHelper);
        }
        if (drift.GameOwnsGrip || drift.RearWheels == null)
        {
            return;   // GripFor rewrote every wheel from the baseline it saved before the slide, and restores them itself
        }
        for (int i = 0; i < drift.RearWheels.Length; i++)
        {
            if (drift.RearWheels[i] != null)
            {
                drift.RearWheels[i].sidewaysFriction = drift.SavedSideways[i];
            }
        }
    }

    static float RouteTurnAhead(float progress, float look)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        return circuit == null ? 0f : Pure.TurnAhead(distance => RouteSample(circuit, distance), progress, look);
    }

    static (Vector3 pos, Vector3 dir) RouteSample(WaypointCircuit circuit, float distance)
    {
        var point = circuit.GetRoutePoint(distance);
        return (point.Position(), point.Direction());
    }

    static float FlatSpeed(Rigidbody body) => new Vector3(body.velocity.x, 0f, body.velocity.z).magnitude;

    // Degrees the nose points to the right of the direction of travel (left = negative); 0 when barely moving.
    static float SlipDegrees(Rigidbody body)
    {
        var travel = body.velocity;
        if (FlatSpeed(body) < 3f)
        {
            return 0f;
        }
        return Pure.FlatTurn(travel, body.transform.forward);
    }

    static void EmitDrift(Vehicle vehicle, string phase, float seconds, float turbo) => Plugin.Emit("drift", new
    {
        login = Login(vehicle),
        phase,
        seconds = Mathf.Round(seconds * 100f) / 100f,
        turbo = Mathf.Round(turbo * 100f) / 100f,
    });
}
