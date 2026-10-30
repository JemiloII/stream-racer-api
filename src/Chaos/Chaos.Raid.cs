// Chaos.Raid: an air raid. A V of low-poly WW2 bombers appears high in the sky behind the field and breaks up: each
// plane picks a racer, dives at where that car is heading, lets go of a bomb that steers onto the car, pulls up, climbs
// back to altitude and picks its next target. When the raid is over they climb away and the event ends.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const int BomberMin = 3;                    // planes in the squadron (inclusive)
    const int BomberMax = 5;
    const float RaidMaxSeconds = 30f;           // attacking this long, then they leave
    const float RaidFlyOffSeconds = 5f;         // climbing away before they are removed
    const float RaidHigh = 95f;                 // m above its target where a plane starts a dive
    const float RaidStartBehind = 140f;         // the squadron appears this far behind the last car, at RaidHigh
    const float RaidPeelGap = 0.8f;             // s between planes breaking out of the V
    const float DiveSpeed = 100f;               // m/s in a dive
    const float ClimbSpeed = 75f;               // m/s climbing back up
    const float PlaneTurnRate = 1.6f;           // how fast a plane swings its heading toward where it wants to go (1/s)
    const float PlaneMaxTurn = 45f;             // degrees/s at most: turning back is a ~4 s banked arc, never a flip
    const float PlaneDiveTurn = 90f;            // degrees/s while lining up a dive on its car
    const float PlaneMaxClimb = 25f;            // degrees: the steepest it climbs out
    const float PlaneCoast = 2.5f;              // s it flies on after a drop before pulling round
    const float DiveFrom = 230f;                // flat distance to the target at which a climbing plane rolls in
    const float ReleaseHeight = 24f;            // bomb away this high above the target (or when this close)
    const float PullUpFloor = 9f;               // never lower than this above the ground under it
    const float ClimbBack = 170f;               // after a pass it climbs out to this far behind its next target
    const float FormationSpacing = 9f;          // between ranks of the V, sideways and back
    const float BombBlastRadius = 6f;
    const float BombSteer = 30f;                // m/s² a falling bomb steers sideways toward its car
    const float BombMaxSeconds = 8f;            // a bomb that never finds the ground is dropped quietly

    static readonly Color PlaneOlive = new(0.33f, 0.36f, 0.2f);
    static readonly Color PlaneGrey = new(0.45f, 0.47f, 0.45f);
    static readonly Color PlaneDark = new(0.15f, 0.15f, 0.14f);
    static readonly Color PlaneGlass = new(0.55f, 0.7f, 0.8f);
    static readonly Color BombDark = new(0.12f, 0.13f, 0.11f);

    // One plane: its flight (Chaos.Swoop), spinning propellers, and when it leaves the V.
    class Bomber : Swooper
    {
        public readonly List<Transform> Propellers = new();
        public float PeelAt;                    // still flying in the V until this time
    }

    static IEnumerator Raid()
    {
        var racers = Racers();
        if (RouteLength <= 0f || racers.Count == 0)
        {
            yield break;
        }
        Announce("AIR RAID", "RAID");
        Siren();
        var squadron = Holder("air-raid");
        float distance = racers.Min(vehicle => vehicle.Progress()) - RaidStartBehind;
        var heading = RoadDirectionAt(distance);
        squadron.position = RoadAt(distance) + Vector3.up * RaidHigh;
        squadron.rotation = Quaternion.LookRotation(heading);
        var bombers = BuildSquadron(squadron);
        for (int index = 0; index < bombers.Count; index++)
        {
            bombers[index].Velocity = heading * ClimbSpeed;
            bombers[index].PeelAt = Time.time + 1.5f + index * RaidPeelGap;
        }
        Cam.PlaneCam(bombers[0].Body);   // ride with the lead bomber (any key in the game, or the raid ending, leaves it)

        for (float time = 0f; time < RaidMaxSeconds && squadron != null; time += Time.deltaTime)
        {
            squadron.position += heading * ClimbSpeed * Time.deltaTime;   // the V itself, until everyone has peeled off
            foreach (var bomber in bombers.Where(bomber => bomber.Body != null))
            {
                SpinPropellers(bomber);
                if (Time.time >= bomber.PeelAt)
                {
                    FlyAttack(bomber);
                }
            }
            yield return null;
        }
        if (squadron == null)
        {
            yield break;
        }
        yield return RaidFlyOff(bombers);
        Cam.EndPlaneCam();
        Object.Destroy(squadron.gameObject);
    }

    static readonly SwoopStyle BomberStyle = new()
    {
        High = RaidHigh,
        DiveSpeed = DiveSpeed,
        ClimbSpeed = ClimbSpeed,
        Turn = PlaneTurnRate,
        MaxTurn = PlaneMaxTurn,
        DiveTurn = PlaneDiveTurn,
        MaxClimb = PlaneMaxClimb,
        CoastSeconds = PlaneCoast,
        DiveFrom = DiveFrom,
        ClimbBack = ClimbBack,
        AttackHeight = ReleaseHeight,
        AttackRange = ReleaseHeight * 1.4f,
        Floor = PullUpFloor,
    };

    // One frame of a plane's attack run (Chaos.Swoop): climb behind its target, dive, drop a bomb, pull up, repeat.
    static void FlyAttack(Bomber bomber)
    {
        if (bomber.Body.parent != Root.transform)
        {
            bomber.Body.SetParent(Root.transform, true);   // out of the V: flies on its own now
        }
        if (bomber.Target == null || bomber.Target.Car() == null || bomber.Target.HasFinished())
        {
            bomber.Target = RandomRacer();
        }
        var target = bomber.Target;
        var next = SwoopStep(bomber, BomberStyle, out bool attack);
        FacePlane(bomber, next - bomber.Body.position);
        bomber.Body.position = next;
        if (attack)
        {
            Spawn(FallingBomb(BuildBomb(next - bomber.Body.up * 0.9f), bomber.Velocity, target));
            bomber.Target = RandomRacer();
        }
    }

    // Nose along the flight path, banked into the turn.
    static void FacePlane(Swooper bomber, Vector3 move)
    {
        if (move.sqrMagnitude < 1e-6f)
        {
            return;
        }
        var flatOld = Vector3.ProjectOnPlane(bomber.Body.forward, Vector3.up);
        var flatNew = Vector3.ProjectOnPlane(move, Vector3.up);
        float turn = flatOld.sqrMagnitude > 1e-6f && flatNew.sqrMagnitude > 1e-6f
            ? Vector3.SignedAngle(flatOld, flatNew, Vector3.up) / Mathf.Max(Time.deltaTime, 1e-3f)
            : 0f;
        float bank = Mathf.Clamp(-turn * 0.5f, -50f, 50f);
        var nose = Quaternion.LookRotation(move.normalized) * Quaternion.Euler(0f, 0f, bank);
        bomber.Body.rotation = Quaternion.Slerp(bomber.Body.rotation, nose, 1f - Mathf.Exp(-6f * Time.deltaTime));
    }

    // Everyone climbs away (no more bombs), then they're gone.
    static IEnumerator RaidFlyOff(List<Bomber> bombers)
    {
        for (float time = 0f; time < RaidFlyOffSeconds; time += Time.deltaTime)
        {
            foreach (var bomber in bombers.Where(bomber => bomber.Body != null))
            {
                var flat = Vector3.ProjectOnPlane(bomber.Velocity, Vector3.up).normalized;
                var climb = (flat + Vector3.up * 0.6f).normalized;
                var heading = Vector3.RotateTowards(bomber.Velocity.normalized, climb, PlaneMaxTurn * Mathf.Deg2Rad * Time.deltaTime, 0f);
                bomber.Velocity = heading * Mathf.MoveTowards(bomber.Velocity.magnitude, ClimbSpeed * 1.4f, 20f * Time.deltaTime);
                FacePlane(bomber, bomber.Velocity * Time.deltaTime);
                bomber.Body.position += bomber.Velocity * Time.deltaTime;
                SpinPropellers(bomber);
            }
            yield return null;
        }
        foreach (var bomber in bombers.Where(bomber => bomber.Body != null))
        {
            Object.Destroy(bomber.Body.gameObject);
        }
    }

    // ---- the planes ----

    // A V: one plane at the point, the rest alternating left and right, each rank further back and slightly higher.
    static List<Bomber> BuildSquadron(Transform squadron)
    {
        int count = Random.Range(BomberMin, BomberMax + 1);
        var bombers = new List<Bomber>();
        for (int index = 0; index < count; index++)
        {
            int rank = (index + 1) / 2;
            float side = index % 2 == 1 ? -1f : 1f;
            var offset = new Vector3(side * rank * FormationSpacing, rank * 1.2f, -rank * FormationSpacing);
            bombers.Add(BuildBomber(squadron, offset, Random.value < 0.5f ? PlaneOlive : PlaneGrey));
        }
        return bombers;
    }

    // A twin-engine bomber, nose toward local +z: capsule fuselage, wings, tailplane and fin, glass nose, two engines.
    static Bomber BuildBomber(Transform squadron, Vector3 offset, Color paint)
    {
        var body = Holder("bomber", squadron);
        body.localPosition = offset;
        var bomber = new Bomber
        {
            Body = body,
        };
        Prim(PrimitiveType.Capsule, body, Vector3.zero, new Vector3(1.3f, 3.6f, 1.3f), paint, new Vector3(90f, 0f, 0f));
        Prim(PrimitiveType.Cube, body, new Vector3(0f, -0.15f, 0.6f), new Vector3(12f, 0.2f, 2f), paint);
        Prim(PrimitiveType.Cube, body, new Vector3(0f, 0.15f, -3.1f), new Vector3(4.4f, 0.15f, 1.1f), paint);
        Prim(PrimitiveType.Cube, body, new Vector3(0f, 0.9f, -3.2f), new Vector3(0.15f, 1.6f, 1.2f), paint);
        Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0.4f, 2.1f), new Vector3(0.8f, 0.55f, 1.3f), PlaneGlass);
        Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0f, 3.5f), new Vector3(0.7f, 0.7f, 0.5f), PlaneGlass);
        foreach (float side in new[] { -1f, 1f })
        {
            bomber.Propellers.Add(BuildEngine(body, new Vector3(side * 2.8f, -0.25f, 1.1f), paint));
        }
        return bomber;
    }

    // An engine nacelle under the wing with a two-blade propeller at its front; returns the propeller to spin.
    static Transform BuildEngine(Transform body, Vector3 position, Color paint)
    {
        Prim(PrimitiveType.Cylinder, body, position, new Vector3(0.75f, 1.1f, 0.75f), paint, new Vector3(90f, 0f, 0f));
        var propeller = Holder("propeller", body);
        propeller.localPosition = position + Vector3.forward * 1.2f;
        Prim(PrimitiveType.Sphere, propeller, Vector3.zero, Vector3.one * 0.4f, PlaneDark);
        Prim(PrimitiveType.Cube, propeller, Vector3.zero, new Vector3(0.18f, 2.6f, 0.06f), PlaneDark);
        Prim(PrimitiveType.Cube, propeller, Vector3.zero, new Vector3(2.6f, 0.18f, 0.06f), PlaneDark);
        return propeller;
    }

    static void SpinPropellers(Bomber bomber)
    {
        foreach (var propeller in bomber.Propellers.Where(propeller => propeller != null))
        {
            propeller.Rotate(0f, 0f, 1500f * Time.deltaTime, Space.Self);
        }
    }

    // ---- the bombs ----

    // A small dark bomb, nose toward local -y, with two crossed fins at its tail (+y).
    static Transform BuildBomb(Vector3 release)
    {
        var bomb = Holder("bomb");
        bomb.position = release;
        Prim(PrimitiveType.Capsule, bomb, Vector3.zero, new Vector3(0.4f, 0.5f, 0.4f), BombDark);
        Prim(PrimitiveType.Cube, bomb, Vector3.up * 0.55f, new Vector3(0.7f, 0.3f, 0.05f), BombDark);
        Prim(PrimitiveType.Cube, bomb, Vector3.up * 0.55f, new Vector3(0.05f, 0.3f, 0.7f), BombDark);
        return bomb;
    }

    // Falls with gravity from the plane's speed, steering sideways onto its car, nose along its path; booms where it
    // meets the ground.
    static IEnumerator FallingBomb(Transform bomb, Vector3 velocity, Vehicle target)
    {
        for (float time = 0f; time < BombMaxSeconds && bomb != null; time += Time.deltaTime)
        {
            velocity += Physics.gravity * Time.deltaTime;
            velocity += BombCorrection(bomb.position, velocity, target) * Time.deltaTime;
            var next = bomb.position + velocity * Time.deltaTime;
            var ground = Ground(next);
            if (ground != next && next.y <= ground.y)   // Ground() hands back the point itself over nothing
            {
                Blast(ground, BombBlastRadius, "air raid");
                break;
            }
            bomb.position = next;
            bomb.rotation = Quaternion.FromToRotation(Vector3.down, velocity.normalized);
            yield return null;
        }
        if (bomb != null)
        {
            Object.Destroy(bomb.gameObject);
        }
    }

    // Sideways push that brings the bomb down on the car: where it would land now vs where the car will be then.
    static Vector3 BombCorrection(Vector3 position, Vector3 velocity, Vehicle target)
    {
        if (target == null || target.Car() == null)
        {
            return Vector3.zero;
        }
        var car = CarPosition(target);
        float gravity = -Physics.gravity.y;
        float height = Mathf.Max(0.5f, position.y - car.y);
        float fall = (velocity.y + Mathf.Sqrt(Mathf.Max(0f, velocity.y * velocity.y + 2f * gravity * height))) / gravity;
        var landing = position + new Vector3(velocity.x, 0f, velocity.z) * fall;
        var body = target.Car().GetComponent<Rigidbody>();
        var carVelocity = body != null ? new Vector3(body.velocity.x, 0f, body.velocity.z) : Vector3.zero;
        var miss = car + carVelocity * fall - landing;
        miss.y = 0f;
        return Vector3.ClampMagnitude(miss * 4f, BombSteer);
    }
}
