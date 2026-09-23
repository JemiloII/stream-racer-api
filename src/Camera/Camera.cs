// Cam: camera state and the helpers every shot shares. Shots the game has: follow (keys 1-9), free, prop cams.
// Shots we add on top of the free cam by steering it every frame live in Camera.Shots.cs; the coverage-driven
// director that picks between them in Camera.Director.cs. This file holds the state (mode, target, active shot),
// the geometry helpers (anchor, heading, groups, leader), FOV handling and the manual/auto toggles.
using System.Collections;

namespace StreamRacerApi;

static partial class Cam
{
    public static bool Auto;
    public static CameraMode Mode;
    static float _manualUntil;                 // the streamer touched the camera: hands off until this
    static Vector3 _finishCameraPosition, _finishLinePosition, _finishLineDirection;
    static bool _afterFinishShown;             // the "look at the leader" beat after a finish burst has been done
    static List<string> _shotLogins = new();   // logins in the current shot (for the camera event)
    public static string Target;               // login when the shot is about one car
    public static Vehicle LastBoomed;

    static Coroutine _directorCoroutine, _resumeCoroutine;
    static float _autoPausedUntil;
    static float _defaultFov = -1f;

    // active custom shot
    static Vehicle _shotCar;
    static float _shotStartedAt, _shotSeconds, _fovFrom, _fovTo;
    static bool _zoomTemp;                     // zoom in and back out over the shot (sin curve) instead of a one-way lerp
    static Vector3 _position, _overheadPosition, _heading;
    static Vector3 _packDirection; static float _packSpread;   // smoothed inputs for the pack shot
    static bool _firstFrame;
    static List<Vehicle> _shotGroup = new();   // the cars a group shot was framed on; locked for the shot so the camera doesn't hunt
    static Vehicle _followCar;                 // the car of the wide follow shot
    static float _sideSign = 1f;               // which side of the road the side/high shots sit on
    static Vector3 _gridPosition, _sweepPosition;

    // Group for the current shot: the set chosen at the cut, minus anyone who finished or vanished.
    static List<Vehicle> Group()
    {
        _shotGroup = _shotGroup.Where(vehicle => vehicle != null && !vehicle.HasFinished() && vehicle.Car() != null).ToList();
        if (_shotGroup.Count == 0) _shotGroup = DensestPack();
        return _shotGroup;
    }

    // The game's own follow cam attaches rigidly to this child of the car every frame; it is the smooth thing to track.
    static Transform Anchor(Vehicle vehicle) => vehicle.Car().GetComponent<Car>()?.CameraAnchor()?.transform ?? vehicle.Car().transform;

    // Smoothed heading: blend of where the car points and where the route goes, eased over time so pans stay clean.
    static Vector3 Heading(Vehicle vehicle)
    {
        var wanted = Flat(vehicle.Car().transform.forward * 0.5f + RouteDir(vehicle) * 0.5f, RouteDir(vehicle));
        _heading = _firstFrame ? wanted : Vector3.Slerp(_heading, wanted, 1f - Mathf.Exp(-Time.deltaTime * 3f));
        return _heading;
    }

    static Vector3 Centroid(List<Vehicle> vehicles) => vehicles.Aggregate(Vector3.zero, (sum, vehicle) => sum + Anchor(vehicle).position) / vehicles.Count;

    // Raw camera pose for studying how a human drives it: where it is, where it looks, what the game is doing.
    public static object Pose()
    {
        var camera = Camera.main; if (camera == null) return new { error = "no camera" };
        var cameraTransform = camera.transform; var euler = cameraTransform.rotation.eulerAngles;
        string game = Instances.FollowCam != null && Instances.FollowCam.IsActive() ? "follow" : Instances.FreeCam != null && Instances.FreeCam.IsActive() ? "free" : "other";
        string nearestLogin = null; float distance = 0, height = 0, behind = 0, sideways = 0;
        var followTarget = Instances.FollowCam?.Target();
        Vehicle nearest = null; float nearestDistance = float.MaxValue;
        foreach (var vehicle in Racing())
        {
            float candidate = Vector3.Distance(Anchor(vehicle).position, cameraTransform.position);
            if (candidate < nearestDistance) { nearestDistance = candidate; nearest = vehicle; }
        }
        if (nearest != null)
        {
            nearestLogin = Game.Login(nearest); distance = nearestDistance;
            var anchor = Anchor(nearest).position; var relative = cameraTransform.position - anchor; height = relative.y;
            var forward = Flat(nearest.Car().transform.forward, Vector3.forward); var right = Vector3.Cross(Vector3.up, forward);
            behind = -Vector3.Dot(relative, forward); sideways = Vector3.Dot(relative, right);
        }
        bool InView(Vehicle vehicle) { var viewport = camera.WorldToViewportPoint(Anchor(vehicle).position); return viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1; }
        int inView = Racing().Count(InView);
        var leader = Leader(); bool leaderInView = leader != null && InView(leader);
        return new
        {
            t = Time.time, game, pluginMode = Mode.ToApiString(), auto = Auto, fov = camera.fieldOfView,
            pos = new[] { cameraTransform.position.x, cameraTransform.position.y, cameraTransform.position.z }, pitch = euler.x > 180 ? euler.x - 360 : euler.x, yaw = euler.y,
            nearest = nearestLogin, dist = distance, height, behind, sideways, followTarget = followTarget != null ? followTarget.root.name : null, carsInView = inView, racing = Racing().Count, leaderInView,
        };
    }

    public static object State => new { auto = Auto, mode = Mode.ToApiString(), target = Target, cars = _shotLogins, fov = Camera.main != null ? Camera.main.fieldOfView : 0f, shots = Settings.Shots };
    // Director toggle for a shot key (settings.camera.shots); manual POST /camera/<shot> never asks.
    static bool On(string key) => Pure.ShotEnabled(Settings.Shots, key);
    static void Announce() => Plugin.Emit("camera", State);
    static bool Ready => Game.Running && Instances.FollowCam != null && Instances.FreeCam != null;
    public static bool ManualHold => Time.time < _manualUntil;
    public static bool Steering => Mode.IsSteered();

    // The streamer pressed a camera key / moved the mouse: stop steering and keep auto quiet for 5 s (renewed on every input).
    public static void NoteUserInput()
    {
        _manualUntil = Time.time + 5f;
        if (Mode.IsSteered() && Mode != CameraMode.Grid) RestoreFov();
        if (Mode != CameraMode.Manual) { Mode = CameraMode.Manual; Target = null; _shotLogins = new List<string>(); Announce(); }
    }

    // Cars within `within` route units of the real finish line (and not yet past it by much).
    static List<Vehicle> Approaching(float within = 110f)
    {
        if (!Game.FinishLine(out _, out _, out float finishDistance)) return new List<Vehicle>();
        return Racing().Where(vehicle => finishDistance - vehicle.Progress() < within && finishDistance - vehicle.Progress() > -30f).OrderByDescending(vehicle => vehicle.Progress()).ToList();
    }

    static Camera MainCam => Camera.main;
    static void RestoreFov() { if (_defaultFov > 0 && MainCam != null) MainCam.fieldOfView = _defaultFov; }
    static void RememberFov() { if (_defaultFov < 0 && MainCam != null) _defaultFov = MainCam.fieldOfView; }

    static List<Vehicle> Racing() => Game.Vehicles().Where(vehicle => !vehicle.HasFinished() && vehicle.Car() != null).ToList();

    // On the road: within 25 units of where the route says it should be. Cars flung off track don't get framed.
    static bool OnRoad(Vehicle vehicle)
    {
        var circuit = Instances.WaypointController?.GetCircuit(); if (circuit == null) return true;
        var routePosition = circuit.GetRoutePoint(vehicle.Progress()).Position(); var anchor = Anchor(vehicle).position;
        return Vector2.Distance(new Vector2(routePosition.x, routePosition.z), new Vector2(anchor.x, anchor.z)) < 25f;
    }
    static List<Vehicle> OnRoadRacing() { var onRoad = Racing().Where(OnRoad).ToList(); return onRoad.Count > 0 ? onRoad : Racing(); }

    // The window of cars (by track progress) that packs the most racers into ~70 units.
    static List<Vehicle> DensestPack()
    {
        var cars = OnRoadRacing().OrderBy(vehicle => vehicle.Progress()).ToList();
        List<Vehicle> best = new();
        for (int i = 0; i < cars.Count; i++)
        {
            var window = cars.Where(other => other.Progress() >= cars[i].Progress() && other.Progress() <= cars[i].Progress() + 70f).ToList();
            if (window.Count > best.Count) best = window;
        }
        return best;
    }

    public static Vehicle Leader() => Racing().OrderByDescending(vehicle => vehicle.Progress()).FirstOrDefault() ?? Game.Ranked().FirstOrDefault();

    static Vector3 RouteDir(Vehicle vehicle)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        var direction = circuit != null ? circuit.GetRoutePoint(vehicle.Progress() + 5f).Direction() : Vector3.forward;
        return Flat(direction, Vector3.forward);
    }
    static Vector3 Flat(Vector3 vector, Vector3 fallback) { vector.y = 0; return vector.sqrMagnitude < 0.01f ? fallback : vector.normalized; }

    // ---- one-off shots / auto toggle ----

    public static bool Shot(System.Func<bool> shot, float seconds)
    {
        if (_resumeCoroutine != null) Plugin.Instance.StopCoroutine(_resumeCoroutine);
        if (!shot()) return false;
        _autoPausedUntil = seconds > 0 ? Time.time + seconds : float.MaxValue;
        if (seconds > 0 && Auto) _resumeCoroutine = Plugin.Instance.StartCoroutine(ResumeAfter(seconds));
        return true;
    }
    static IEnumerator ResumeAfter(float seconds) { yield return new WaitForSeconds(seconds); _autoPausedUntil = 0; _resumeCoroutine = null; }

    public static void SetAuto(bool on)
    {
        Auto = on;
        if (_directorCoroutine != null) { Plugin.Instance.StopCoroutine(_directorCoroutine); _directorCoroutine = null; }
        if (on) { _autoPausedUntil = 0; _directorCoroutine = Plugin.Instance.StartCoroutine(Director()); }
        Announce();
    }

    // The game positions the name labels in CarLabel.LateUpdate. If that ran before our camera move this frame,
    // labels lag one frame behind the cars and stutter. Re-place them after we move the camera.
    static CarLabel[] _labels = new CarLabel[0]; static float _labelsScanned = -10f;
    public static void RefreshLabels()
    {
        if (!Steering) return;
        if (Time.time - _labelsScanned > 2f) { _labels = Object.FindObjectsOfType<CarLabel>(); _labelsScanned = Time.time; }
        foreach (var label in _labels) if (label != null && label.isActiveAndEnabled) { try { label.LateUpdate(); } catch { } }
    }

    public static void OnRaceEnd()
    {
        _props.Clear(); _propsScanned = -1f; _lastProp = null; _afterFinishShown = false; _manualUntil = 0;
        if (_resumeCoroutine != null) { Plugin.Instance.StopCoroutine(_resumeCoroutine); _resumeCoroutine = null; }
        _autoPausedUntil = 0; Mode = CameraMode.None; Target = null; RestoreFov(); Announce();
    }
}
