// Game.Vehicles: who is in the race. Vehicle lookup, the streamer, the JSON shapes (Dto / Snapshot / PosFrame),
// what each car is doing (StateOf) and the crash tracking built on it. Obfuscated members come in through
// GameNames.cs only; a game update that renames one makes the affected route throw and return 501.
using HarmonyLib;
using UnityStandardAssets.Vehicles.Car;

namespace StreamRacerApi;

static partial class Game
{
    static readonly System.Reflection.FieldInfo FinishAtField = AccessTools.Field(typeof(Vehicle), GameNames.VehicleFinishAt);

    public static float FinishAt(Vehicle vehicle) => (float)FinishAtField.GetValue(vehicle);

    // The game's own Twitch app token (Helix-capable) and client id, so we can resolve logins without another auth flow.
    public static string TwitchToken => Instances.Backend?.CurrentUser()?.AccessToken();
    public static string TwitchClientId => Instances.TwitchClientId;

    public static string StreamerId => Instances.Backend?.CurrentUser()?.Id();
    public static string StreamerLogin => Instances.Backend?.CurrentUser()?.Login();
    public static Vehicle StreamerVehicle() =>
        Vehicles().FirstOrDefault(vehicle => vehicle.Profile().TwitchId() == StreamerId || vehicle.Profile().Login() == StreamerLogin);

    public static bool Running => Instances.GameController != null && Instances.GameController.IsGameRunning();

    public static List<Vehicle> Vehicles() =>
        Instances.VehicleManager == null ? new List<Vehicle>() : Instances.VehicleManager.GetVehicles();

    // Same ordering the in-game leaderboard uses: progress descending, finishers get bumped high.
    // Who finished, in the order they crossed the line. A finished car stops covering ground, so ranking on distance
    // alone let the next car still driving overtake it and be called the winner. Finishers keep their places.
    static readonly List<string> _finishOrder = new();
    public static IReadOnlyList<string> FinishOrder => _finishOrder;
    public static void ForgetFinishOrder() => _finishOrder.Clear();
    public static int NoteFinished(Vehicle finisher)
    {
        string login = Login(finisher);
        if (login != null && !_finishOrder.Contains(login)) _finishOrder.Add(login);
        return FinishPlace(finisher) is int place && place > 0 ? place : _finishOrder.Count;
    }
    static int FinishPlace(Vehicle vehicle)
    {
        string login = Login(vehicle);
        int index = login == null ? -1 : _finishOrder.IndexOf(login);
        return index < 0 ? 0 : index + 1;
    }

    public static List<Vehicle> Ranked() => Vehicles()
        .OrderBy(vehicle => FinishPlace(vehicle) > 0 ? 0 : 1)                       // everyone who finished, first
        .ThenBy(vehicle => FinishPlace(vehicle) > 0 ? FinishPlace(vehicle) : 0)     // in the order they crossed
        .ThenByDescending(vehicle => vehicle.Progress())                            // then the rest by ground covered
        .ToList();

    public static Vehicle Find(string idOrLogin) =>
        Vehicles().FirstOrDefault(vehicle => vehicle.Profile().TwitchId() == idOrLogin || vehicle.Profile().Login() == idOrLogin);

    public static string Login(Vehicle vehicle) => vehicle.Profile().Login();
    public static string DisplayName(Vehicle vehicle) => vehicle.Profile().DisplayName();

    // What the lobby row prints under the name: backend CustomTitle ("Developer", "Streamer", ...) else Subscriber / Normal Racer.
    public static string Title(Vehicle vehicle) => vehicle.Profile().Title();

    /// How far along the route the finish line actually is: the game's own finishAt can sit short of the line, which
    /// would read 100% while the car is still driving. Falls back to finishAt when the trigger can't be found.
    // The route distance a car has covered when it crosses the line. Best source first:
    //   1. what the first finisher of this race actually covered (exact, once anybody has finished),
    //   2. the finish-line trigger's distance along the route,
    //   3. the route length, since the game's own target sits well short of the line and would read 100 early.
    static float _learnedFinish = -1f;
    public static void ForgetLearnedFinish() => _learnedFinish = -1f;
    public static void LearnFinishDistance(Vehicle finisher)
    {
        float covered = finisher?.Progress() ?? 0f;
        if (covered > 1f && (_learnedFinish < 1f || covered < _learnedFinish)) _learnedFinish = covered;
    }

    public static float FinishDistance(Vehicle vehicle)
    {
        // The track itself is the answer: how far along the route the finish line sits. Only when the route can't be
        // read do we fall back on what the first finisher actually covered.
        if (FinishLine(out _, out _, out var lineDistance) && lineDistance > 1f) return lineDistance;
        if (_learnedFinish > 1f) return _learnedFinish;
        float routeLength = Instances.WaypointController?.GetCircuit()?.Length() ?? 0f;
        return routeLength > 1f ? routeLength : Mathf.Max(FinishAt(vehicle), 1f);
    }

    // Only a car that actually crossed the line reads 100: anything else stops at 99, however the distances work out.
    static float ProgressPercent(Vehicle vehicle) =>
        vehicle.HasFinished() ? 100f : Mathf.Clamp(vehicle.Progress() / Mathf.Max(1f, FinishDistance(vehicle)) * 100f, 0f, 99f);

    public static object Dto(Vehicle vehicle, int place)
    {
        var profile = vehicle.Profile();
        var car = vehicle.Car();
        return new
        {
            place,
            id = profile.TwitchId(),
            login = profile.Login(),
            displayName = profile.DisplayName(),
            color = "#" + ColorUtility.ToHtmlStringRGB(profile.Color()),
            sub = profile.IsSubscriber(),
            type = vehicle.Type().ToString(),
            progress = vehicle.Progress(),
            finishAt = FinishAt(vehicle),
            pct = ProgressPercent(vehicle),
            finished = vehicle.HasFinished(),
            boosts = Boosts(vehicle),
            respawns = RespawnsLeft(profile.Login()),
            state = StateOf(vehicle).ToApiString(),
            image = Images.TryGetValue(profile.Login() ?? "", out var image) ? image : null,
            avatar = Avatar(vehicle),
            title = Title(vehicle),
            x = car != null ? (float?)car.transform.position.x : null,
            z = car != null ? (float?)car.transform.position.z : null,
        };
    }

    public static object EventDto(Vehicle vehicle) => Dto(vehicle, Ranked().IndexOf(vehicle) + 1);

    // Tiny per-frame payload for maps/overlays: [login, x, z, pct, place, finished, state] per car.
    public static object PosFrame()
    {
        var ranked = Ranked();
        var frame = new List<object[]>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            var vehicle = ranked[i];
            var position = vehicle.Car() != null ? vehicle.Car().transform.position : Vector3.zero;
            frame.Add(new object[]
            {
                Login(vehicle), Mathf.Round(position.x * 10f) / 10f, Mathf.Round(position.z * 10f) / 10f, Mathf.Round(ProgressPercent(vehicle) * 10f) / 10f,
                i + 1, vehicle.HasFinished() ? 1 : 0, StateOf(vehicle).ToApiString(),
            });
        }
        return new { t = Time.unscaledTime, v = frame };
    }

    public static object Snapshot()
    {
        var ranked = Ranked();
        var game = Instances.GameController?.CurrentGame();
        return new
        {
            running = Running, lobby = InLobby, streamer = StreamerLogin,
            map = game == null ? null : new { id = game.MapId(), name = game.MapName(), creator = MapCreator(game.MapId()) },
            vehicles = ranked.Select((vehicle, index) => Dto(vehicle, index + 1)).ToList(),
        };
    }

    // ---- vehicle state ----

    // A car is "on the road" when it sits within 18 units of where the route says its progress is.
    static bool IsOffRoad(Vehicle vehicle, Transform carTransform)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null) return false;
        var routePosition = circuit.GetRoutePoint(vehicle.Progress()).Position();
        return Vector2.Distance(new Vector2(routePosition.x, routePosition.z), new Vector2(carTransform.position.x, carTransform.position.z)) > 18f;
    }

    public static VehicleState StateOf(Vehicle vehicle)
    {
        if (vehicle.HasFinished()) return VehicleState.Finished;
        if (vehicle.Car() == null) return VehicleState.Spawning;
        if (IsBoomed(vehicle)) return VehicleState.Stunned;
        var carTransform = vehicle.Car().transform;
        if (Vector3.Dot(carTransform.up, Vector3.up) < 0.5f) return VehicleState.Flipped;
        var body = vehicle.Car().GetComponent<Rigidbody>();
        bool grounded = Physics.Raycast(carTransform.position + Vector3.up * 0.5f, Vector3.down, 2.5f);
        if (!grounded || (body != null && Mathf.Abs(body.velocity.y) > 4f)) return VehicleState.Air;
        if (IsOffRoad(vehicle, carTransform)) return VehicleState.Offroad;
        var ai = vehicle.Car().GetComponent<CarAIControl>();
        if (Running && ai != null && ai.IsDriving() && body != null && body.velocity.magnitude < 0.5f && Time.time - _runningSince > 10f) return VehicleState.Stuck;
        return VehicleState.Driving;
    }
    static float _runningSince = -1f; static bool _wasRunning;

    // Crash tracking: a car leaving "driving" for flipped/offroad/stuck (not a boom, not a jump) for > 1 s is a crash;
    // back to driving is a recovery. Emitted as SSE "crash" / "recovered"; recent crashes feed the director's pile-up rule.
    static readonly Dictionary<Vehicle, float> _badSince = new();
    public static readonly Dictionary<Vehicle, float> CrashedAt = new(); // still-crashed cars -> when
    static float _nextStateScan;
    public static void TrackStates()
    {
        if (Time.unscaledTime < _nextStateScan) return; _nextStateScan = Time.unscaledTime + 0.25f;
        if (!Running) { _wasRunning = false; _badSince.Clear(); CrashedAt.Clear(); return; }
        if (!_wasRunning) { _wasRunning = true; _runningSince = Time.time; } // the green light, not the countdown
        foreach (var vehicle in Vehicles())
        {
            var state = StateOf(vehicle);
            bool bad = state.IsCrashed();
            if (bad) { if (!_badSince.ContainsKey(vehicle)) _badSince[vehicle] = Time.time; }
            else _badSince.Remove(vehicle);
            bool crashed = CrashedAt.ContainsKey(vehicle);
            float needed = state == VehicleState.Stuck ? 4f : 1f; // a stall needs longer to count than a flip
            if (!crashed && bad && Time.time - _badSince[vehicle] > needed)
            {
                CrashedAt[vehicle] = Time.time;
                Plugin.Emit("crash", new { login = Login(vehicle), displayName = DisplayName(vehicle), state = state.ToApiString(), place = Ranked().IndexOf(vehicle) + 1, pileup = CrashedAt.Values.Count(at => Time.time - at < 8f) });
            }
            else if (crashed && state is VehicleState.Driving or VehicleState.Finished or VehicleState.Stunned)
            {
                CrashedAt.Remove(vehicle);
                Plugin.Emit("recovered", new { login = Login(vehicle), displayName = DisplayName(vehicle), place = Ranked().IndexOf(vehicle) + 1 });
            }
        }
    }
}
