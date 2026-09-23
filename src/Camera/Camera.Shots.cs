// Cam shots. Game-native: follow (plus the zoomed-out "wide" follow that pans toward the field), free, the fixed
// track cameras (props). Custom: chase, front, orbit, pack, side, high, grid, sweep, overhead, finish; Begin() takes
// the free cam and Tick() places it every frame from the FreeCam.LateUpdate postfix. Every custom shot can zoom
// (FOV from -> to over its life).
using HarmonyLib;

namespace StreamRacerApi;

static partial class Cam
{
    // ---- game-native shots ----

    public static bool Follow(Vehicle vehicle)
    {
        if (!Ready || vehicle?.Car() == null) return false;
        RestoreFov();
        Instances.CamController.ReleasePropCam();
        Instances.FreeCam.SetActive(false);
        Instances.FollowCam.Follow(vehicle);
        Mode = CameraMode.Follow; Target = Game.Login(vehicle); _shotLogins = new List<string> { Target }; Announce();
        return true;
    }

    // Number-key follow the way the streamer uses it: zoomed right out (40), tilted ~22° down, and orbited so the
    // camera sits on the far side of the target from the rest of the field -> the target plus the pack in frame.
    static readonly System.Reflection.FieldInfo FollowZoom = AccessTools.Field(typeof(FollowCam), GameNames.FollowCamZoom);
    public static bool FollowWide(Vehicle vehicle, float zoom = 40f, float pitch = 22f)
    {
        if (!Follow(vehicle)) return false;
        _followCar = vehicle;
        FollowZoom?.SetValue(Instances.FollowCam, zoom);
        var others = Racing().Where(other => other != vehicle).ToList();
        Vector3 look = others.Count > 0 ? Centroid(others) - Anchor(vehicle).position : Heading(vehicle);
        look.y = 0;
        if (look.sqrMagnitude < 1f) look = Heading(vehicle);
        Instances.CamController.SetYaw(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg); // yaw: camera behind the target looking across it at the others
        Instances.CamController.SetPitch(pitch);
        Mode = CameraMode.FollowWide; Announce(); _lastCutAt = Time.time;
        return true;
    }

    // Runs before FollowCam.Update every frame while in the wide follow: pan (yaw) toward the rest of the field
    // at a human pace, and hold the tilt. This is what keeps the pack in frame instead of cutting.
    public static void PanFollow()
    {
        if (Mode != CameraMode.FollowWide || _followCar?.Car() == null || Instances.CamController == null) return;
        var anchor = Anchor(_followCar).position;
        var others = Racing().Where(other => other != _followCar).ToList();
        Vector3 look = others.Count > 0 ? Centroid(others) - anchor : RouteDir(_followCar);
        look.y = 0; if (look.sqrMagnitude < 1f) look = RouteDir(_followCar);
        float wantedYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        var controller = Instances.CamController;
        controller.SetYaw(Mathf.MoveTowardsAngle(controller.Yaw(), wantedYaw, 35f * Time.deltaTime)); // ~35°/s pan
        controller.SetPitch(Mathf.MoveTowards(controller.Pitch(), 22f, 20f * Time.deltaTime));
        FollowZoom?.SetValue(Instances.FollowCam, 40f);
    }

    public static bool Free()
    {
        if (!Ready) return false;
        RestoreFov();
        TakeFreeCam();
        Mode = CameraMode.Free; Target = null; _shotLogins = new List<string>(); Announce();
        return true;
    }

    static void TakeFreeCam()
    {
        Instances.CamController.ReleasePropCam();
        Instances.FollowCam.StopFollow();
        Instances.FreeCam.SetActive(true);
    }

    // ---- custom shots (steered every frame in Tick) ----

    static bool Begin(CameraMode mode, Vehicle car, float seconds, float fovFrom, float fovTo)
    {
        if (!Ready) return false;
        RememberFov();
        TakeFreeCam();
        Mode = mode; _shotCar = car; Target = car != null ? Game.Login(car) : null;
        _shotGroup = DensestPack();
        _shotLogins = car != null ? new List<string> { Game.Login(car) } : _shotGroup.Select(Game.Login).ToList();
        _shotStartedAt = Time.time; _shotSeconds = seconds > 0 ? seconds : 9999f;
        _fovFrom = fovFrom > 0 ? fovFrom : _defaultFov; _fovTo = fovTo > 0 ? fovTo : _fovFrom;
        _zoomTemp = _fovTo < _fovFrom; // any zoom-in is temporary: punch in, then settle back out
        _firstFrame = true;
        Announce(); _lastCutAt = Time.time;
        return true;
    }

    public static bool Chase(Vehicle vehicle, float seconds = 0, float fovFrom = 0, float fovTo = 0) => vehicle?.Car() != null && Begin(CameraMode.Chase, vehicle, seconds, fovFrom, fovTo);
    public static bool Front(Vehicle vehicle, float seconds = 0, float fovFrom = 0, float fovTo = 0) => vehicle?.Car() != null && Begin(CameraMode.Front, vehicle, seconds, fovFrom, fovTo);
    public static bool Orbit(Vehicle vehicle, float seconds = 0, float fovFrom = 0, float fovTo = 0) => vehicle?.Car() != null && Begin(CameraMode.Orbit, vehicle, seconds, fovFrom, fovTo);
    public static bool Pack(float seconds = 0, float fovFrom = 0, float fovTo = 0) => Racing().Count > 0 && Begin(CameraMode.Pack, null, seconds, fovFrom, fovTo);
    // Alongside the group, looking a little ahead: jumps, inclines and corners show before the cars reach them.
    public static bool Side(float seconds = 0, float fovFrom = 0, float fovTo = 0) { _sideSign = Random.value < 0.5f ? 1f : -1f; return Racing().Count > 0 && Begin(CameraMode.Side, null, seconds, fovFrom, fovTo); }
    // High and far behind the group, looking down the road.
    public static bool High(float seconds = 0, float fovFrom = 0, float fovTo = 0) { _sideSign = Random.value < 0.5f ? 1f : -1f; return Racing().Count > 0 && Begin(CameraMode.High, null, seconds, fovFrom, fovTo); }
    // The high shot from the other side of the road (director candidate "high2").
    static bool HighOtherSide() { _sideSign = -1f; return Racing().Count > 0 && Begin(CameraMode.High, null, 0, 60f, 60f); }

    // High and off to the side of the line, angled down at the line and the last stretch of road into it.
    public static bool Finish(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        if (!Game.FinishLine(out var linePosition, out var lineDirection, out _)) return false;
        _finishLinePosition = linePosition; _finishLineDirection = Flat(lineDirection, Vector3.forward);
        var side = Vector3.Cross(Vector3.up, _finishLineDirection) * (Random.value < 0.5f ? 1f : -1f);
        _finishCameraPosition = _finishLinePosition - _finishLineDirection * 8f + side * 30f + Vector3.up * 40f;
        if (!Begin(CameraMode.Finish, null, seconds, fovFrom, fovTo)) return false;
        _shotLogins = Approaching().Select(Game.Login).ToList(); Target = "finish";
        return true;
    }

    // Start shot: parked on the road ~45 units ahead of the grid, low, looking back at the field; the launch comes at
    // the camera and the cars stream past underneath. Cut to the high overview a few seconds later.
    public static bool Grid(float seconds = 0)
    {
        var cars = Racing(); if (cars.Count == 0) return false;
        var lead = cars.OrderByDescending(vehicle => vehicle.Progress()).First();
        var circuit = Instances.WaypointController?.GetCircuit(); if (circuit == null) return false;
        var routePoint = circuit.GetRoutePoint(lead.Progress() + 30f);
        var direction = Flat(routePoint.Direction(), Vector3.forward);
        var side = Vector3.Cross(Vector3.up, direction) * (Random.value < 0.5f ? 4f : -4f);
        _gridPosition = routePoint.Position() + side + Vector3.up * 4f;
        return Begin(CameraMode.Grid, null, seconds, 60f, 60f);
    }

    // Cinematic crane shot: the camera parks beside the track ahead of the densest group and only pans as they go by.
    public static bool Sweep(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        var pack = DensestPack(); if (pack.Count == 0) return false;
        var rear = pack.OrderBy(vehicle => vehicle.Progress()).First();
        var circuit = Instances.WaypointController?.GetCircuit(); if (circuit == null) return false;
        var routePoint = circuit.GetRoutePoint(rear.Progress() + 90f);
        var direction = Flat(routePoint.Direction(), Vector3.forward);
        var side = Vector3.Cross(Vector3.up, direction) * (Random.value < 0.5f ? 1f : -1f);
        _sweepPosition = routePoint.Position() + side * 26f + Vector3.up * 11f;
        return Begin(CameraMode.Sweep, null, seconds, fovFrom, fovTo);
    }

    public static bool Overhead(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        var points = circuit?.Waypoints()?.Where(waypoint => waypoint != null).Select(waypoint => waypoint.position).ToList();
        if (points == null || points.Count == 0 || !Begin(CameraMode.Overhead, null, seconds, fovFrom, fovTo)) return false;
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) bounds.Encapsulate(point);
        float fov = _fovFrom * Mathf.Deg2Rad, aspect = MainCam != null ? MainCam.aspect : 1.78f;
        float needed = Mathf.Max(bounds.size.z / 2f / Mathf.Tan(fov / 2f), bounds.size.x / 2f / Mathf.Tan(fov / 2f) / aspect);
        _overheadPosition = new Vector3(bounds.center.x, bounds.max.y + Mathf.Clamp(needed * 1.15f + 20f, 60f, 900f), bounds.center.z);
        return true;
    }

    // Framing shared by the group shots (pack, side, high). Taken raw these numbers shudder: the rear car swaps as
    // places change, and any car's own heading twitches with every steering input. So the direction comes from the
    // road rather than the car, and both it and the spread are eased over time. Reset at each cut by _firstFrame.
    static Vector3 _groupDirection; static float _groupSpread;
    static (Vector3 centroid, Vector3 direction, float spread) GroupFrame(List<Vehicle> pack)
    {
        var ordered = pack.OrderBy(vehicle => vehicle.Progress()).ToList();
        var rear = ordered[0]; var front = ordered[ordered.Count - 1];
        var routeDirection = RouteDir(rear); routeDirection.y = 0f;
        if (routeDirection.sqrMagnitude < 0.01f) routeDirection = Heading(rear);
        _groupDirection = _firstFrame || _groupDirection.sqrMagnitude < 0.01f
            ? routeDirection.normalized
            : Vector3.Slerp(_groupDirection, routeDirection.normalized, 1f - Mathf.Exp(-SmoothDelta * 1.5f));
        float rawSpread = Vector3.Distance(Anchor(rear).position, Anchor(front).position);
        _groupSpread = _firstFrame ? rawSpread : Mathf.Lerp(_groupSpread, rawSpread, 1f - Mathf.Exp(-SmoothDelta * 0.8f));
        return (Centroid(pack), _groupDirection, _groupSpread);
    }

    // Runs from a postfix on FreeCam.LateUpdate: places the camera for the active custom shot.
    public static void Tick(FreeCam freeCam)
    {
        if (freeCam == null || MainCam == null) return;
        if (!Mode.IsSteered()) return;
        if (!Game.Running) { Mode = CameraMode.None; return; }
        float age = Time.time - _shotStartedAt, life = Mathf.Clamp01(age / _shotSeconds);
        MainCam.fieldOfView = Mathf.Lerp(_fovFrom, _fovTo, _zoomTemp ? Mathf.Sin(life * Mathf.PI) : life);
        Vector3 wanted, lookAt;
        switch (Mode)
        {
            case CameraMode.Overhead:
                freeCam.transform.position = _overheadPosition; freeCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); return;
            case CameraMode.Chase:
            {
                if (_shotCar?.Car() == null) { Mode = CameraMode.None; return; }
                var anchor = Anchor(_shotCar).position; var forward = Heading(_shotCar);
                wanted = anchor - forward * 18f + Vector3.up * 7f; lookAt = anchor + forward * 12f + Vector3.up * 1f; break; // far enough back to see the road ahead
            }
            case CameraMode.Front:
            {
                if (_shotCar?.Car() == null) { Mode = CameraMode.None; return; }
                var anchor = Anchor(_shotCar).position; var forward = Heading(_shotCar);
                wanted = anchor + forward * 16f + Vector3.up * 6f; lookAt = anchor - forward * 6f + Vector3.up * 1f; break; // ahead of the car, looking back at it and the chasers
            }
            case CameraMode.Orbit:
            {
                if (_shotCar?.Car() == null) { Mode = CameraMode.None; return; }
                var anchor = Anchor(_shotCar).position;
                var offset = Quaternion.Euler(0f, age * 22f, 0f) * (-Heading(_shotCar) * 15f);
                wanted = anchor + offset + Vector3.up * 5f; lookAt = anchor + Vector3.up * 1f; break;
            }
            case CameraMode.Side:
            case CameraMode.High:
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = CameraMode.None; return; }
                var (centroid, direction, spread) = GroupFrame(pack);
                var sideVector = Vector3.Cross(Vector3.up, direction) * _sideSign;
                if (Mode == CameraMode.Side)
                {
                    wanted = centroid + sideVector * (22f + spread * 0.3f) + Vector3.up * (8f + spread * 0.15f) - direction * 4f;
                    lookAt = centroid + direction * (14f + spread * 0.3f) + Vector3.up * 1f;   // look ahead of them
                }
                else
                {
                    // like the streamer's free-cam overview: ~70 units off, ~35 up, ~35° down, the whole group in frame
                    wanted = centroid - direction * (45f + spread * 0.35f) + sideVector * 25f + Vector3.up * (35f + spread * 0.25f);
                    lookAt = centroid + direction * 12f;
                }
                Target = pack.Count + " cars"; _shotLogins = pack.Select(Game.Login).ToList();
                break;
            }
            case CameraMode.Finish:
            {
                var coming = Approaching(160f);
                _shotLogins = coming.Select(Game.Login).ToList();
                // frame the line plus the last ~25 units of road, leaning a little toward the nearest car
                var approach = _finishLinePosition - _finishLineDirection * 25f;
                var focus = coming.Count > 0 ? Vector3.Lerp(approach, Anchor(coming[0]).position, 0.35f) : approach;
                wanted = _finishCameraPosition; lookAt = focus;
                break;
            }
            case CameraMode.Grid:
            {
                var all = Racing(); if (all.Count == 0) { Mode = CameraMode.None; return; }
                wanted = _gridPosition; lookAt = Centroid(all) + Vector3.up * 1f; _shotLogins = all.Select(Game.Login).ToList(); Target = all.Count + " cars";
                break;
            }
            case CameraMode.Sweep:
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = CameraMode.None; return; }
                wanted = _sweepPosition; lookAt = Centroid(pack) + Vector3.up * 1f;
                Target = pack.Count + " cars"; _shotLogins = pack.Select(Game.Login).ToList();
                break;
            }
            default: // pack
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = CameraMode.None; return; }
                // Hangs off the middle of the group, not off one car, so a place swap does not move the camera.
                var (centroid, direction, spread) = GroupFrame(pack);
                float back = 26f + spread * 0.7f, up = 9f + spread * 0.3f;
                wanted = centroid - direction * back + Vector3.up * up;
                lookAt = centroid + direction * 6f + Vector3.up * 1f;
                Target = pack.Count + " cars"; _shotLogins = pack.Select(Game.Login).ToList();
                break;
            }
        }
        // Rigid on position (like the game's follow cam); only the look direction eases.
        // Pack is the exception: its anchor car can change, so ease position there to avoid a snap.
        bool eased = Mode is CameraMode.Pack or CameraMode.Side or CameraMode.High;
        float ease = Mode == CameraMode.Pack ? 1.4f : 2f;     // the group shots drift, they never dart
        if (eased && !_firstFrame)
        {
            var next = Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-SmoothDelta * ease));
            float maxStep = 45f * SmoothDelta;                        // never faster than a car
            _position = Vector3.Distance(next, _position) > maxStep ? _position + (next - _position).normalized * maxStep : next;
        }
        else _position = wanted;
        var look = Quaternion.LookRotation(lookAt - _position, Vector3.up);
        freeCam.transform.position = _position;
        float turn = Mode is CameraMode.Sweep or CameraMode.Finish or CameraMode.Grid ? 3f : eased ? 4.5f : 10f; // parked cameras pan slowly, group shots turn gently, car-mounted shots keep up
        freeCam.transform.rotation = _firstFrame ? look : Quaternion.Slerp(freeCam.transform.rotation, look, 1f - Mathf.Exp(-SmoothDelta * turn));
        _firstFrame = false;
        // keep the game's free-cam angles in sync so taking over with the mouse doesn't snap
        var euler = freeCam.transform.rotation.eulerAngles;
        Instances.CamController.SetPitch(euler.x > 180f ? euler.x - 360f : euler.x);
        Instances.CamController.SetYaw(euler.y);
    }

    // ---- track cameras ----
    // The game's fixed track cameras: physical camera props (tag propCam, the Camera is their first child).
    // Cars can knock them over; a knocked-over one is never switched to, but if it gets hit while live we
    // ride it out for the rest of the shot (fun), then it's locked out.
    public class PropCam { public GameObject gameObject; public Vector3 position; public Quaternion rotation; public bool hit; }
    static readonly List<PropCam> _props = new();
    static float _propsScanned = -1f;
    public static int PropCount => _props.Count(prop => !prop.hit);

    static void ScanProps()
    {
        _props.Clear();
        try
        {
            foreach (var propObject in GameObject.FindGameObjectsWithTag("propCam"))
                if (propObject.transform.childCount > 0) _props.Add(new PropCam { gameObject = propObject, position = propObject.transform.position, rotation = propObject.transform.rotation });
        }
        catch { }
        _propsScanned = Time.time;
    }

    static void UpdateProps()
    {
        if (!Game.Running) return;
        if (_propsScanned < 0 || _props.Any(prop => prop.gameObject == null)) ScanProps();
        foreach (var prop in _props)
            if (!prop.hit && prop.gameObject != null && (Vector3.Distance(prop.gameObject.transform.position, prop.position) > 1.2f || Quaternion.Angle(prop.gameObject.transform.rotation, prop.rotation) > 8f))
                prop.hit = true;
    }

    // An intact prop that a car (leader or the densest group) is heading toward and close to.
    static PropCam ApproachingProp(float within = 150f)
    {
        UpdateProps();
        var cars = DensestPack(); var leader = Leader(); if (leader != null && !cars.Contains(leader)) cars.Add(leader);
        PropCam best = null; float bestDistance = float.MaxValue;
        foreach (var prop in _props.Where(prop => !prop.hit && prop.gameObject != null))
            foreach (var car in cars)
            {
                var anchor = Anchor(car).position; var toProp = prop.gameObject.transform.position - anchor; toProp.y = 0;
                float distance = toProp.magnitude;
                if (distance < within && distance < bestDistance && Vector3.Dot(toProp.normalized, RouteDir(car)) > 0.3f) { best = prop; bestDistance = distance; }
            }
        return best;
    }

    public static bool Prop(PropCam prop)
    {
        if (!Ready || prop?.gameObject == null || prop.hit) return false;
        RestoreFov();
        Instances.FollowCam.StopFollow(); Instances.FreeCam.SetActive(false);
        Traverse.Create(Instances.CamController).Method(GameNames.CamUsePropCam, prop.gameObject.transform.GetChild(0).gameObject).GetValue();
        Mode = CameraMode.Prop; Target = prop.gameObject.name; _shotLogins = DensestPack().Select(Game.Login).ToList(); Announce();
        return true;
    }

    public static bool PropNearLeader(float within = 220f)
    {
        UpdateProps();
        var leader = Leader(); if (leader?.Car() == null) return false;
        var ahead = Anchor(leader).position + RouteDir(leader) * 60f;
        var best = _props.Where(prop => !prop.hit && prop.gameObject != null).OrderBy(prop => Vector3.Distance(prop.gameObject.transform.position, ahead)).FirstOrDefault();
        return best != null && Vector3.Distance(best.gameObject.transform.position, ahead) <= within && Prop(best);
    }
}
