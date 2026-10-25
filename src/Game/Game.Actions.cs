// Game.Actions: things done to a car. Boom, boost (free and pool), bot auto-boosting, respawn (+ the chat respawn
// pool), slow-down, mark finished, kick.
using System.Collections;
using HarmonyLib;

namespace StreamRacerApi;

static partial class Game
{
    // ---- boom ----

    // 5s fuse + 4s stun in the game's explode coroutine
    const float BoomBusySeconds = 9f;
    static readonly Dictionary<Vehicle, float> BoomedUntil = new();

    static bool IsBoomed(Vehicle vehicle) => BoomedUntil.TryGetValue(vehicle, out var until) && Time.unscaledTime < until;

    public static bool Boomable(Vehicle vehicle) => !vehicle.HasFinished() && !IsBoomed(vehicle);

    // Unlike the game's picker, skips cars already mid-boom. false = nobody left to hit.
    public static bool BoomRandom()
    {
        var pool = Vehicles().Where(Boomable).ToList();
        return pool.Count > 0 && Boom(pool[Random.Range(0, pool.Count)]);
    }

    public static bool Boom(Vehicle vehicle)
    {
        if (!Running || !Boomable(vehicle)) return false;
        BoomedUntil[vehicle] = Time.unscaledTime + BoomBusySeconds;
        vehicle.Explode();
        return true;
    }

    // Lightning (rain): pick a random racing car and reserve it (nothing else booms it while the bolt comes down).
    public static Vehicle PickStrikeTarget()
    {
        if (!Running)
        {
            return null;
        }
        var targets = Vehicles().Where(vehicle => vehicle.Car() != null && !vehicle.HasFinished() && Boomable(vehicle)).ToList();
        if (targets.Count == 0)
        {
            return null;
        }
        // a car the camera can see, when there is one: a strike off screen is a boom with no bolt to watch
        var inView = targets.Where(OnScreen).ToList();
        var pool = inView.Count > 0 ? inView : targets;
        var struck = pool[Random.Range(0, pool.Count)];
        BoomedUntil[struck] = Time.unscaledTime + BoomBusySeconds;
        return struck;
    }

    // A chaos event hits this car now (meteor, bomb, fireball fuse, ...): the game's boom credited to `by`. False when
    // the car can't be hit (finished, already mid-boom, no car).
    public static bool StrikeNow(Vehicle vehicle, string by)
    {
        if (!Running || vehicle?.Car() == null || !Boomable(vehicle))
        {
            return false;
        }
        BoomedUntil[vehicle] = Time.unscaledTime + BoomBusySeconds;
        StrikeCar(vehicle, by);
        return true;
    }

    static bool OnScreen(Vehicle vehicle)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }
        var view = camera.WorldToViewportPoint(vehicle.Car().transform.position);
        return view.z > 0f && view.x > 0.05f && view.x < 0.95f && view.y > 0.05f && view.y < 0.95f;
    }

    // The bolt landed: the game's own boom, credited to `by` on the feed line ("name · by storm").
    public static void StrikeCar(Vehicle struck, string by)
    {
        if (!Running || struck.Car() == null || struck.HasFinished())
        {
            return;
        }
        struck.Explode(by);
        BoomNow(struck);
        // the game's feed line names only the car now (Oct 10 build ignores "by"): credit the storm on that line
        var feed = GameObject.Find("InGameCanvas/EventNotifications/EventContainer/Content/List");
        if (feed != null && feed.transform.childCount > 0)
        {
            SetFeedText(feed.transform.GetChild(feed.transform.childCount - 1), "Main/Content/Top/Text", struck.Profile().DisplayName() + " · by " + by);
        }
    }

    // The game's boom waits 5 s before the blast; a strike (bolt, meteor, bomb, a fuse running out) should blow the car
    // up the moment it lands. Cancel that fuse and run the same blast now, then the game's own 4 s stun and recovery.
    const float BoomForce = 2900000f, BoomForceRadius = 1000f, BoomStunSeconds = 4f;   // the game's own numbers
    static void BoomNow(Vehicle vehicle)
    {
        var car = vehicle.Car().GetComponent<Car>();
        if (car == null || !car.StopBoomFuse())
        {
            return;   // can't reach the fuse: the game's own (late) boom still happens
        }
        car.StartCoroutine(BlastAndRecover(car));
    }

    static IEnumerator BlastAndRecover(Car car)
    {
        car.SetBoomBusy(true);
        var body = car.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.AddExplosionForce(BoomForce, car.transform.position - new Vector3(0f, -0.25f, -1f), BoomForceRadius);
        }
        car.BoomBlast();
        yield return new WaitForSeconds(BoomStunSeconds);
        if (car != null)
        {
            car.BoomRecover();
            car.SetBoomBusy(false);
        }
    }

    public static void ResetBoomTracking() { BoomedUntil.Clear(); Slows.Clear(); _respawnsUsed.Clear(); }

    // ---- boost ----

    public static bool Boost(Vehicle vehicle, float? force, float? seconds)
    {
        if (!Running || vehicle.Car() == null) return false;
        var ai = vehicle.Car().GetComponent<CarAIControl>();
        if (ai == null || !ai.IsDriving()) return false;
        var car = vehicle.Car().GetComponent<Car>();
        car.Boost(force ?? Random.Range(2.5f, 15f), seconds ?? Random.Range(1.5f, 5f));
        Plugin.Emit("boost", EventDto(vehicle));
        Chaos.OnBoost(vehicle);
        return true;
    }

    // Same as typing !boost: consumes one from the pool, random strength.
    public static bool UseBoost(Vehicle vehicle) => Running && vehicle.UseBoost();

    // "On a straight" = the route keeps its heading for the next stretch ahead of the car.
    public static bool OnStraight(Vehicle vehicle, float lookAhead = 35f, float minDot = 0.96f)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null || vehicle.Car() == null) return false;
        var here = circuit.GetRoutePoint(vehicle.Progress()).Direction().normalized;
        var ahead = circuit.GetRoutePoint(vehicle.Progress() + lookAhead).Direction().normalized;
        var facing = vehicle.Car().transform.forward.normalized;
        return Vector3.Dot(here, ahead) > minDot && Vector3.Dot(facing, here) > 0.9f;
    }

    // A boost only makes sense with wheels on the road: not mid-boom, not airborne, not off in the grass.
    public static bool ReadyToBoost(Vehicle vehicle) => BoostBlocker(vehicle) == null;

    // Why a boost would be wasted right now (null = go): the API refuses with this instead of spending the boost.
    public static string BoostBlocker(Vehicle vehicle)
    {
        if (vehicle.Car() == null) return "no car";
        if (vehicle.HasFinished()) return "finished";
        if (IsBoomed(vehicle)) return "boomed";
        var carTransform = vehicle.Car().transform;
        var body = vehicle.Car().GetComponent<Rigidbody>();
        if (body != null && Mathf.Abs(body.velocity.y) > 2.5f) return "in the air";                          // flying / falling
        if (Vector3.Dot(carTransform.up, Vector3.up) < 0.7f) return "flipped";                                // tipped over
        if (!Physics.Raycast(carTransform.position + Vector3.up * 0.5f, Vector3.down, 2.5f)) return "in the air";   // nothing under the car
        if (IsOffRoad(vehicle, carTransform)) return "off the road";
        return null;
    }

    // Bot racers are AI cars nobody is typing !boost for, so drive their boosts: first try 0-4 s after
    // the start, then every 6-18 s, and only fire on a straight (fallback: fire anyway after waiting too long).
    public static IEnumerator AutoBoostRace()
    {
        while (!Running) yield return null;
        var nextTryAt = new Dictionary<string, float>();
        var forceAt = new Dictionary<string, float>();
        foreach (var vehicle in Vehicles().Where(vehicle => Settings.IsBot(Login(vehicle)) && Settings.AutoBoosts(Login(vehicle)) && Login(vehicle) != StreamerLogin))
            nextTryAt[Login(vehicle)] = Time.time + Random.Range(0f, 4f);
        while (Running)
        {
            yield return new WaitForSeconds(0.25f);
            foreach (var vehicle in Vehicles())
            {
                string login = Login(vehicle);
                if (!nextTryAt.TryGetValue(login, out var dueAt) || Time.time < dueAt || vehicle.HasFinished() || Boosts(vehicle) <= 0) continue;
                if (!Settings.AutoBoosts(login)) continue;                       // third-party controlled bot: leave its pool alone
                if (!ReadyToBoost(vehicle)) { forceAt.Remove(login); continue; } // wait until they're back on the road, wheels down
                if (!forceAt.ContainsKey(login)) forceAt[login] = Time.time + 8f;
                bool onStraight = InBoostZone(vehicle.Progress()) || (BoostZones().Count == 0 && OnStraight(vehicle));
                if (!onStraight && Time.time < forceAt[login]) continue;
                if (UseBoost(vehicle)) { nextTryAt[login] = Time.time + Random.Range(6f, 18f); forceAt.Remove(login); }
            }
        }
    }

    // ---- boost pool ----

    static readonly System.Reflection.FieldInfo BoostsField = AccessTools.Field(typeof(Vehicle), GameNames.VehicleBoosts);

    public static int Boosts(Vehicle vehicle) => (int)BoostsField.GetValue(vehicle);
    public static void AddBoosts(Vehicle vehicle, int count)
    {
        BoostsField.SetValue(vehicle, Boosts(vehicle) + count);
        EmitBoosts(vehicle, count);
    }
    // `boosts` = a pool changed without a boost being fired (add, perk, race start). Pool spends are the `boost` event (pool patch).
    public static void EmitBoosts(Vehicle vehicle, int delta = 0) =>
        Plugin.Emit("boosts", new { login = Login(vehicle), displayName = DisplayName(vehicle), boosts = Boosts(vehicle), delta });
    public static void EmitBoostPools() { foreach (var vehicle in Vehicles()) EmitBoosts(vehicle); }

    // ---- respawn ----

    public static bool Respawn(Vehicle vehicle)
    {
        if (!Running || vehicle.Car() == null || vehicle.HasFinished()) return false;
        var ai = vehicle.Car().GetComponent<CarAIControl>();
        if (ai == null) return false;
        ai.StartCoroutine(GameNames.AiRespawnCoroutine); // the game's own stuck-car respawn coroutine
        Plugin.Emit("respawn", EventDto(vehicle));
        return true;
    }

    // Respawn pool: chat respawns per racer per race; API respawns (the streamer) are free.
    static readonly Dictionary<string, int> _respawnsUsed = new();
    public static int RespawnsLeft(string login) =>
        Pure.RespawnsLeft(Settings.Current.respawnLimit, login != null && _respawnsUsed.TryGetValue(login.ToLowerInvariant(), out var used) ? used : 0);
    static void NoteChatRespawn(string login) => _respawnsUsed[login] = (_respawnsUsed.TryGetValue(login, out var used) ? used : 0) + 1;

    public static object InventoryDto(string login)
    {
        var vehicle = Find(login); if (vehicle == null) return null;
        return new { login = Login(vehicle), displayName = DisplayName(vehicle), boosts = Boosts(vehicle), respawns = RespawnsLeft(Login(vehicle)), respawnLimit = Settings.Current.respawnLimit, running = Running };
    }

    // ---- slow-down ----

    // The AI driver resets the top-speed multiplier every frame, so we store the
    // slow here and re-apply it in a postfix on that update (see Patches).
    static readonly Dictionary<CarController, (float mult, float until)> Slows = new();

    public static bool Speed(Vehicle vehicle, float multiplier, float seconds)
    {
        if (!Running || vehicle.Car() == null) return false;
        var controller = vehicle.Car().GetComponent<CarController>();
        if (controller == null) return false;
        Slows[controller] = (multiplier, Time.unscaledTime + seconds);
        return true;
    }

    public static void ApplySlow(CarController controller)
    {
        if (controller == null || !Slows.TryGetValue(controller, out var slow)) return;
        if (Time.unscaledTime < slow.until) controller.SetTopSpeedMultiplier(controller.TopSpeedMultiplier() * slow.mult);
        else Slows.Remove(controller);
    }

    // ---- finish ----

    // Streamer says "that car finished": run the car's own finish-line trigger handler, exactly what touching
    // the FinishLine collider does. For when a car crosses the line without the game noticing.
    static readonly System.Reflection.MethodInfo FinishTrigger = AccessTools.Method(typeof(Car), GameNames.CarFinishTrigger);
    public static bool MarkFinished(Vehicle vehicle)
    {
        if (!Running || vehicle.HasFinished() || vehicle.Car() == null) return false;
        var car = vehicle.Car().GetComponent<Car>();
        if (car == null || FinishTrigger == null) return false;
        float finishAt = FinishAt(vehicle);
        if (finishAt > 0 && vehicle.Progress() <= finishAt) vehicle.SetProgress(finishAt + 1f); // the handler refuses cars the game thinks are too early
        FinishTrigger.Invoke(car, null);
        return vehicle.HasFinished();
    }

    // ---- kick ----

    // Lobby only: kicking mid-race can end the race. Mirrors the row's own kick button (remove + destroy row).
    public static bool Kick(Vehicle vehicle)
    {
        if (Running || !Instances.VehicleManager.RemoveVehicleByTwitchUsername(Login(vehicle))) return false;
        foreach (var row in Object.FindObjectsOfType<PreGamePlayerListItem>())
            if (row.Profile()?.Login() == Login(vehicle)) Object.Destroy(row.gameObject);
        return true;
    }
}
