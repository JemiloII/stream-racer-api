using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cage.StreamRacer;
using HarmonyLib;
using UnityEngine;

namespace StreamRacerApi;

// Camera director. Shots the game has: follow (keys 1-9), free. Shots we add on top of the free cam by
// steering it every frame: chase (behind the car), pack (as many racers in frame as possible), orbit
// (slow pan around a car), overhead (whole track). Every custom shot can zoom (FOV from -> to over its life).
static class Cam
{
    public static bool Auto;
    public static string Mode;     // follow | free | chase | front | pack | sweep | side | high | orbit | overhead | prop | finish | grid | manual
    static float _manualUntil;     // the streamer touched the camera: hands off until this
    static Vector3 _finishPos, _finishLine, _finishDir;
    static bool _afterFinishShown;  // the "look at the leader" beat after a finish burst has been done
    static List<string> _cars = new(); // logins in the current shot (for the camera event)
    public static string Target;   // login when the shot is about one car
    public static CFBJLEBOFHJ LastBoomed;

    static Coroutine _auto, _focus;
    static float _autoPausedUntil;
    static float _defaultFov = -1f;

    // active custom shot
    static CFBJLEBOFHJ _car;
    static float _t0, _life, _fovFrom, _fovTo;
    static bool _zoomTemp; // zoom in and back out over the shot (sin curve) instead of a one-way lerp
    static Vector3 _pos, _overhead, _dir;
    static bool _first;
    static List<CFBJLEBOFHJ> _group = new(); // the cars a group shot was framed on; locked for the shot so the camera doesn't hunt
    static float _raceStart;

    // Group for the current shot: the set chosen at the cut, minus anyone who finished or vanished.
    static List<CFBJLEBOFHJ> Group()
    {
        _group = _group.Where(v => v != null && !v.NIKOEDJIAFB && v.JPHIMKLIAAO != null).ToList();
        if (_group.Count == 0) _group = DensestPack();
        return _group;
    }

    // The game's own follow cam attaches rigidly to this child of the car every frame; it is the smooth thing to track.
    static Transform Anchor(CFBJLEBOFHJ v) => v.JPHIMKLIAAO.GetComponent<Car>()?.AHBGCGOEADP?.transform ?? v.JPHIMKLIAAO.transform;

    // Smoothed heading: blend of where the car points and where the route goes, eased over time so pans stay clean.
    static Vector3 Heading(CFBJLEBOFHJ v)
    {
        var want = Flat(v.JPHIMKLIAAO.transform.forward * 0.5f + RouteDir(v) * 0.5f, RouteDir(v));
        _dir = _first ? want : Vector3.Slerp(_dir, want, 1f - Mathf.Exp(-Time.deltaTime * 3f));
        return _dir;
    }

    // Raw camera pose for studying how a human drives it: where it is, where it looks, what the game is doing.
    public static object Pose()
    {
        var cam = Camera.main; if (cam == null) return new { error = "no camera" };
        var t = cam.transform; var e = t.rotation.eulerAngles;
        string game = FollowCam.NEPFAEJAMGI != null && FollowCam.NEPFAEJAMGI.MKJKDNAFJML ? "follow" : FreeCam.NEPFAEJAMGI != null && FreeCam.NEPFAEJAMGI.MKJKDNAFJML ? "free" : "other";
        string target = null; float dist = 0, height = 0, behind = 0, sideways = 0;
        var followT = FollowCam.NEPFAEJAMGI?.KKPFHEIDONO;
        CFBJLEBOFHJ near = null; float nd = float.MaxValue;
        foreach (var v in Racing()) {
        float d = Vector3.Distance(Anchor(v).position, t.position);
        if (d < nd) { nd = d; near = v; } }
        if (near != null)
        {
            target = Game.Login(near); dist = nd;
            var a = Anchor(near).position; var rel = t.position - a; height = rel.y;
            var fwd = Flat(near.JPHIMKLIAAO.transform.forward, Vector3.forward); var right = Vector3.Cross(Vector3.up, fwd);
            behind = -Vector3.Dot(rel, fwd); sideways = Vector3.Dot(rel, right);
        }
        int inView = Racing().Count(v => { var p = cam.WorldToViewportPoint(Anchor(v).position); return p.z > 0 && p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1; });
        bool InView(CFBJLEBOFHJ v) { var p = cam.WorldToViewportPoint(Anchor(v).position); return p.z > 0 && p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1; }
        var leader = Leader(); bool leaderInView = leader != null && InView(leader);
        return new { t = Time.time, game, pluginMode = Mode, auto = Auto, fov = cam.fieldOfView, pos = new[] { t.position.x, t.position.y, t.position.z }, pitch = e.x > 180 ? e.x - 360 : e.x, yaw = e.y,
                     nearest = target, dist, height, behind, sideways, followTarget = followT != null ? followT.root.name : null, carsInView = inView, racing = Racing().Count, leaderInView };
    }

    public static object State => new { auto = Auto, mode = Mode, target = Target, cars = _cars, fov = Camera.main != null ? Camera.main.fieldOfView : 0f, shots = Settings.Shots };
    // Director toggle for a shot key (settings.camera.shots); manual POST /camera/<shot> never asks.
    static bool On(string key) => Pure.ShotEnabled(Settings.Shots, key);
    static void Announce() => Plugin.Emit("camera", State);
    static bool Ready => Game.Running && FollowCam.NEPFAEJAMGI != null && FreeCam.NEPFAEJAMGI != null;
    public static bool ManualHold => Time.time < _manualUntil;

    // The streamer pressed a camera key / moved the mouse: stop steering and keep auto quiet for 5 s (renewed on every input).
    public static void NoteUserInput()
    {
        _manualUntil = Time.time + 5f;
        if (Mode == "chase" || Mode == "front" || Mode == "orbit" || Mode == "pack" || Mode == "sweep" || Mode == "side" || Mode == "high" || Mode == "overhead" || Mode == "finish")
        {
            RestoreFov();
            Mode = "manual"; Target = null; _cars = new List<string>(); Announce();
        }
        else if (Mode != "manual") { Mode = "manual"; Target = null; _cars = new List<string>(); Announce(); }
    }

    // ---- finish ----
    // Cars within `within` route units of the real finish line (and not yet past it by much).
    static List<CFBJLEBOFHJ> Approaching(float within = 110f)
    {
        if (!Game.FinishLine(out _, out _, out float dist)) return new List<CFBJLEBOFHJ>();
        return Racing().Where(v => dist - v.MNHJMCLOGPB < within && dist - v.MNHJMCLOGPB > -30f).OrderByDescending(v => v.MNHJMCLOGPB).ToList();
    }

    // High and off to the side of the line, angled down at the line and the last stretch of road into it.
    public static bool Finish(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        if (!Game.FinishLine(out var linePos, out var lineDir, out _)) return false;
        _finishLine = linePos; _finishDir = Flat(lineDir, Vector3.forward);
        var side = Vector3.Cross(Vector3.up, _finishDir) * (Random.value < 0.5f ? 1f : -1f);
        _finishPos = _finishLine - _finishDir * 8f + side * 30f + Vector3.up * 40f;
        if (!Begin("finish", null, seconds, fovFrom, fovTo)) return false;
        _cars = Approaching().Select(Game.Login).ToList(); Target = "finish";
        return true;
    }
    static Camera MainCam => Camera.main;

    static void RestoreFov() { if (_defaultFov > 0 && MainCam != null) MainCam.fieldOfView = _defaultFov; }
    static void RememberFov() { if (_defaultFov < 0 && MainCam != null) _defaultFov = MainCam.fieldOfView; }

    // ---- game-native shots ----

    public static bool Follow(CFBJLEBOFHJ v)
    {
        if (!Ready || v?.JPHIMKLIAAO == null) return false;
        RestoreFov();
        CamController.NEPFAEJAMGI.ReleasePropCam();
        FreeCam.NEPFAEJAMGI.MKJKDNAFJML = false;
        FollowCam.NEPFAEJAMGI.Follow(v);
        Mode = "follow"; Target = Game.Login(v); _cars = new List<string> { Target }; Announce();
        return true;
    }

    // Number-key follow the way the streamer uses it: zoomed right out (40), tilted ~22° down, and orbited so the
    // camera sits on the far side of the target from the rest of the field -> the target plus the pack in frame.
    static readonly System.Reflection.FieldInfo FollowZoom = AccessTools.Field(typeof(FollowCam), "JNEOPKGDGJB");
    public static bool FollowWide(CFBJLEBOFHJ v, float zoom = 40f, float pitch = 22f)
    {
        if (!Follow(v)) return false;
        _followCar = v;
        FollowZoom?.SetValue(FollowCam.NEPFAEJAMGI, zoom);
        var others = Racing().Where(o => o != v).ToList();
        Vector3 look;
        if (others.Count > 0) { var c = others.Aggregate(Vector3.zero, (a, o) => a + Anchor(o).position) / others.Count; look = c - Anchor(v).position; }
        else look = Heading(v);
        look.y = 0;
        if (look.sqrMagnitude < 1f) look = Heading(v);
        CamController.NEPFAEJAMGI.JAPOGLMOALK = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg; // yaw: camera behind the target looking across it at the others
        CamController.NEPFAEJAMGI.JMFMLGGKDLA = pitch;
        Mode = "followwide"; Announce(); _shotStart = Time.time;
        return true;
    }

    // Runs before FollowCam.Update every frame while in the wide follow: pan (yaw) toward the rest of the field
    // at a human pace, and hold the tilt. This is what keeps the pack in frame instead of cutting.
    static CFBJLEBOFHJ _followCar;
    public static void PanFollow()
    {
        if (Mode != "followwide" || _followCar?.JPHIMKLIAAO == null || CamController.NEPFAEJAMGI == null) return;
        var a = Anchor(_followCar).position;
        var others = Racing().Where(o => o != _followCar).ToList();
        Vector3 l = others.Count > 0 ? others.Aggregate(Vector3.zero, (acc, o) => acc + Anchor(o).position) / others.Count - a : RouteDir(_followCar);
        l.y = 0; if (l.sqrMagnitude < 1f) l = RouteDir(_followCar);
        float want = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
        float cur = CamController.NEPFAEJAMGI.JAPOGLMOALK;
        CamController.NEPFAEJAMGI.JAPOGLMOALK = Mathf.MoveTowardsAngle(cur, want, 35f * Time.deltaTime); // ~35°/s pan
        CamController.NEPFAEJAMGI.JMFMLGGKDLA = Mathf.MoveTowards(CamController.NEPFAEJAMGI.JMFMLGGKDLA, 22f, 20f * Time.deltaTime);
        FollowZoom?.SetValue(FollowCam.NEPFAEJAMGI, 40f);
    }

    public static bool Free()
    {
        if (!Ready) return false;
        RestoreFov();
        TakeFreeCam();
        Mode = "free"; Target = null; _cars = new List<string>(); Announce();
        return true;
    }

    static void TakeFreeCam()
    {
        CamController.NEPFAEJAMGI.ReleasePropCam();
        FollowCam.NEPFAEJAMGI.StopFollow();
        FreeCam.NEPFAEJAMGI.MKJKDNAFJML = true;
    }

    // ---- custom shots (steered every frame in Tick) ----

    static bool Begin(string mode, CFBJLEBOFHJ car, float seconds, float fovFrom, float fovTo)
    {
        if (!Ready) return false;
        RememberFov();
        TakeFreeCam();
        Mode = mode; _car = car; Target = car != null ? Game.Login(car) : null;
        _group = DensestPack();
        _cars = car != null ? new List<string> { Game.Login(car) } : _group.Select(Game.Login).ToList();
        _t0 = Time.time; _life = seconds > 0 ? seconds : 9999f;
        _fovFrom = fovFrom > 0 ? fovFrom : _defaultFov; _fovTo = fovTo > 0 ? fovTo : _fovFrom;
        _zoomTemp = _fovTo < _fovFrom; // any zoom-in is temporary: punch in, then settle back out
        _first = true;
        Announce(); _shotStart = Time.time;
        return true;
    }

    public static bool Chase(CFBJLEBOFHJ v, float seconds = 0, float fovFrom = 0, float fovTo = 0) => v?.JPHIMKLIAAO != null && Begin("chase", v, seconds, fovFrom, fovTo);
    public static bool Front(CFBJLEBOFHJ v, float seconds = 0, float fovFrom = 0, float fovTo = 0) => v?.JPHIMKLIAAO != null && Begin("front", v, seconds, fovFrom, fovTo);
    public static bool Orbit(CFBJLEBOFHJ v, float seconds = 0, float fovFrom = 0, float fovTo = 0) => v?.JPHIMKLIAAO != null && Begin("orbit", v, seconds, fovFrom, fovTo);
    public static bool Pack(float seconds = 0, float fovFrom = 0, float fovTo = 0) => Racing().Count > 0 && Begin("pack", null, seconds, fovFrom, fovTo);
    // Alongside the group, looking a little ahead: jumps, inclines and corners show before the cars reach them.
    static float _sideSign = 1f;
    public static bool Side(float seconds = 0, float fovFrom = 0, float fovTo = 0) { _sideSign = Random.value < 0.5f ? 1f : -1f; return Racing().Count > 0 && Begin("side", null, seconds, fovFrom, fovTo); }
    // High and far behind the group, looking down the road.
    public static bool High(float seconds = 0, float fovFrom = 0, float fovTo = 0) { _sideSign = Random.value < 0.5f ? 1f : -1f; return Racing().Count > 0 && Begin("high", null, seconds, fovFrom, fovTo); }

    // Start shot: parked on the road ~45 units ahead of the grid, low, looking back at the field; the launch comes at
    // the camera and the cars stream past underneath. Cut to the high overview a few seconds later.
    static Vector3 _gridPos;
    public static bool Grid(float seconds = 0)
    {
        var cars = Racing(); if (cars.Count == 0) return false;
        var lead = cars.OrderByDescending(v => v.MNHJMCLOGPB).First();
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit(); if (c == null) return false;
        var rp = c.GetRoutePoint(lead.MNHJMCLOGPB + 30f);
        var dir = Flat(rp.AHLOFCFINIO, Vector3.forward);
        var side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? 4f : -4f);
        _gridPos = rp.PFHEOLIHHHD + side + Vector3.up * 4f;
        return Begin("grid", null, seconds, 60f, 60f);
    }

    // Cinematic crane shot: the camera parks beside the track ahead of the densest group and only pans as they go by.
    static Vector3 _sweepPos;
    public static bool Sweep(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        var pack = DensestPack(); if (pack.Count == 0) return false;
        var rear = pack.OrderBy(v => v.MNHJMCLOGPB).First();
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit(); if (c == null) return false;
        var rp = c.GetRoutePoint(rear.MNHJMCLOGPB + 90f);
        var dir = Flat(rp.AHLOFCFINIO, Vector3.forward);
        var side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? 1f : -1f);
        _sweepPos = rp.PFHEOLIHHHD + side * 26f + Vector3.up * 11f;
        return Begin("sweep", null, seconds, fovFrom, fovTo);
    }

    public static bool Overhead(float seconds = 0, float fovFrom = 0, float fovTo = 0)
    {
        var circuit = WaypointController.NEPFAEJAMGI?.GetCircuit();
        var pts = circuit?.IPOOGHHOEGD?.Where(t => t != null).Select(t => t.position).ToList();
        if (pts == null || pts.Count == 0 || !Begin("overhead", null, seconds, fovFrom, fovTo)) return false;
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        float fov = _fovFrom * Mathf.Deg2Rad, aspect = MainCam != null ? MainCam.aspect : 1.78f;
        float need = Mathf.Max(b.size.z / 2f / Mathf.Tan(fov / 2f), b.size.x / 2f / Mathf.Tan(fov / 2f) / aspect);
        _overhead = new Vector3(b.center.x, b.max.y + Mathf.Clamp(need * 1.15f + 20f, 60f, 900f), b.center.z);
        return true;
    }

    static List<CFBJLEBOFHJ> Racing() => Game.Vehicles().Where(v => !v.NIKOEDJIAFB && v.JPHIMKLIAAO != null).ToList();

    // On the road: within 25 units of where the route says it should be. Cars flung off track don't get framed.
    static bool OnRoad(CFBJLEBOFHJ v)
    {
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit(); if (c == null) return true;
        var p = c.GetRoutePoint(v.MNHJMCLOGPB).PFHEOLIHHHD; var a = Anchor(v).position;
        return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(a.x, a.z)) < 25f;
    }
    static List<CFBJLEBOFHJ> OnRoadRacing() { var r = Racing().Where(OnRoad).ToList(); return r.Count > 0 ? r : Racing(); }

    // The leader and whoever is within 70 units behind them.
    static List<CFBJLEBOFHJ> LeaderGroup()
    {
        var l = Leader(); if (l == null) return new List<CFBJLEBOFHJ>();
        return Racing().Where(v => l.MNHJMCLOGPB - v.MNHJMCLOGPB <= 70f && l.MNHJMCLOGPB - v.MNHJMCLOGPB >= 0f).ToList();
    }

    // The window of cars (by track progress) that packs the most racers into ~70 units.
    static List<CFBJLEBOFHJ> DensestPack()
    {
        var cars = OnRoadRacing().OrderBy(v => v.MNHJMCLOGPB).ToList();
        List<CFBJLEBOFHJ> best = new();
        for (int i = 0; i < cars.Count; i++)
        {
            var w = cars.Where(c => c.MNHJMCLOGPB >= cars[i].MNHJMCLOGPB && c.MNHJMCLOGPB <= cars[i].MNHJMCLOGPB + 70f).ToList();
            if (w.Count > best.Count) best = w;
        }
        return best;
    }

    // Runs from a postfix on FreeCam.LateUpdate: places the camera for the active custom shot.
    public static void Tick(FreeCam cam)
    {
        if (cam == null || MainCam == null) return;
        if (Mode != "chase" && Mode != "front" && Mode != "orbit" && Mode != "pack" && Mode != "sweep" && Mode != "side" && Mode != "high" && Mode != "overhead" && Mode != "finish" && Mode != "grid") return;
        if (!Game.Running) { Mode = null; return; }
        float age = Time.time - _t0, k = Mathf.Clamp01(age / _life);
        MainCam.fieldOfView = Mathf.Lerp(_fovFrom, _fovTo, _zoomTemp ? Mathf.Sin(k * Mathf.PI) : k);
        Vector3 want, lookAt;
        switch (Mode)
        {
            case "overhead":
                cam.transform.position = _overhead; cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); return;
            case "chase":
            {
                if (_car?.JPHIMKLIAAO == null) { Mode = null; return; }
                var a = Anchor(_car).position; var fwd = Heading(_car);
                want = a - fwd * 18f + Vector3.up * 7f; lookAt = a + fwd * 12f + Vector3.up * 1f; break; // far enough back to see the road ahead
            }
            case "front":
            {
                if (_car?.JPHIMKLIAAO == null) { Mode = null; return; }
                var a = Anchor(_car).position; var fwd = Heading(_car);
                want = a + fwd * 16f + Vector3.up * 6f; lookAt = a - fwd * 6f + Vector3.up * 1f; break; // ahead of the car, looking back at it and the chasers
            }
            case "orbit":
            {
                if (_car?.JPHIMKLIAAO == null) { Mode = null; return; }
                var a = Anchor(_car).position;
                var off = Quaternion.Euler(0f, age * 22f, 0f) * (-Heading(_car) * 15f);
                want = a + off + Vector3.up * 5f; lookAt = a + Vector3.up * 1f; break;
            }
            case "side":
            case "high":
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = null; return; }
                var ordered = pack.OrderBy(v => v.MNHJMCLOGPB).ToList();
                var rear = ordered.First(); var front = ordered.Last();
                var centroid = pack.Aggregate(Vector3.zero, (a, v) => a + Anchor(v).position) / pack.Count;
                var dir = Heading(rear);
                float spread = Vector3.Distance(Anchor(rear).position, Anchor(front).position);
                if (Mode == "side")
                {
                    var sideV = Vector3.Cross(Vector3.up, dir) * _sideSign;
                    want = centroid + sideV * (22f + spread * 0.3f) + Vector3.up * (8f + spread * 0.15f) - dir * 4f;
                    lookAt = centroid + dir * (14f + spread * 0.3f) + Vector3.up * 1f;   // look ahead of them
                }
                else
                {
                    // like the streamer's free-cam overview: ~70 units off, ~35 up, ~35° down, the whole group in frame
                    var sideV = Vector3.Cross(Vector3.up, dir) * _sideSign;
                    want = centroid - dir * (45f + spread * 0.35f) + sideV * 25f + Vector3.up * (35f + spread * 0.25f);
                    lookAt = centroid + dir * 12f;
                }
                Target = pack.Count + " cars"; _cars = pack.Select(Game.Login).ToList();
                break;
            }
            case "finish":
            {
                var coming = Approaching(160f);
                _cars = coming.Select(Game.Login).ToList();
                // frame the line plus the last ~25 units of road, leaning a little toward the nearest car
                var approach = _finishLine - _finishDir * 25f;
                var focus = coming.Count > 0 ? Vector3.Lerp(approach, Anchor(coming[0]).position, 0.35f) : approach;
                want = _finishPos; lookAt = focus;
                break;
            }
            case "grid":
            {
                var all = Racing(); if (all.Count == 0) { Mode = null; return; }
                var centroid = all.Aggregate(Vector3.zero, (acc, v) => acc + Anchor(v).position) / all.Count;
                want = _gridPos; lookAt = centroid + Vector3.up * 1f; _cars = all.Select(Game.Login).ToList(); Target = all.Count + " cars";
                break;
            }
            case "sweep":
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = null; return; }
                var centroid = pack.Aggregate(Vector3.zero, (a, v) => a + Anchor(v).position) / pack.Count;
                want = _sweepPos; lookAt = centroid + Vector3.up * 1f;
                Target = pack.Count + " cars"; _cars = pack.Select(Game.Login).ToList();
                break;
            }
            default: // pack
            {
                var pack = Group();
                if (pack.Count == 0) { Mode = null; return; }
                var ordered = pack.OrderBy(v => v.MNHJMCLOGPB).ToList();
                var rear = ordered.First(); var front = ordered.Last();
                var centroid = pack.Aggregate(Vector3.zero, (a, v) => a + Anchor(v).position) / pack.Count;
                var dir = Heading(rear);
                float spread = Vector3.Distance(Anchor(rear).position, Anchor(front).position);
                float back = 20f + spread * 0.45f, up = 9f + spread * 0.3f;
                want = Anchor(rear).position - dir * back + Vector3.up * up; lookAt = centroid + dir * 6f + Vector3.up * 1f;
                Target = pack.Count + " cars"; _cars = pack.Select(Game.Login).ToList();
                break;
            }
        }
        // Rigid on position (like the game's follow cam); only the look direction eases.
        // Pack is the exception: its anchor car can change, so ease position there to avoid a snap.
        bool eased = Mode == "pack" || Mode == "side" || Mode == "high";
        if (eased && !_first)
        {
            var next = Vector3.Lerp(_pos, want, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            float maxStep = 45f * Time.deltaTime;                        // never faster than a car
            _pos = Vector3.Distance(next, _pos) > maxStep ? _pos + (next - _pos).normalized * maxStep : next;
        }
        else _pos = want;
        var look = Quaternion.LookRotation(lookAt - _pos, Vector3.up);
        cam.transform.position = _pos;
        float turn = Mode == "sweep" || Mode == "finish" || Mode == "grid" ? 3f : 10f; // parked cameras pan slowly
        cam.transform.rotation = _first ? look : Quaternion.Slerp(cam.transform.rotation, look, 1f - Mathf.Exp(-Time.deltaTime * turn));
        _first = false;
        // keep the game's free-cam angles in sync so taking over with the mouse doesn't snap
        var e = cam.transform.rotation.eulerAngles;
        CamController.NEPFAEJAMGI.JMFMLGGKDLA = e.x > 180f ? e.x - 360f : e.x;
        CamController.NEPFAEJAMGI.JAPOGLMOALK = e.y;
    }

    static Vector3 RouteDir(CFBJLEBOFHJ v)
    {
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit();
        var d = c != null ? c.GetRoutePoint(v.MNHJMCLOGPB + 5f).AHLOFCFINIO : Vector3.forward;
        return Flat(d, Vector3.forward);
    }
    static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 0.01f ? fallback : v.normalized; }

    // The game's fixed track cameras: physical camera props (tag propCam, the Camera is their first child).
    // Cars can knock them over; a knocked-over one is never switched to, but if it gets hit while live we
    // ride it out for the rest of the shot (fun), then it's locked out.
    public class PropCam { public GameObject go; public Vector3 pos; public Quaternion rot; public bool hit; }
    static readonly List<PropCam> _props = new();
    static float _propsScanned = -1f;
    public static int PropCount => _props.Count(p => !p.hit);
    public static PropCam _livingProp;

    static void ScanProps()
    {
        _props.Clear();
        try
        {
            foreach (var go in GameObject.FindGameObjectsWithTag("propCam"))
                if (go.transform.childCount > 0) _props.Add(new PropCam { go = go, pos = go.transform.position, rot = go.transform.rotation });
        }
        catch { }
        _propsScanned = Time.time;
    }

    static void UpdateProps()
    {
        if (!Game.Running) return;
        if (_propsScanned < 0 || _props.Any(p => p.go == null)) ScanProps();
        foreach (var p in _props)
            if (!p.hit && p.go != null && (Vector3.Distance(p.go.transform.position, p.pos) > 1.2f || Quaternion.Angle(p.go.transform.rotation, p.rot) > 8f))
                p.hit = true;
    }

    // An intact prop that a car (leader or the densest group) is heading toward and close to.
    static PropCam ApproachingProp(float within = 150f)
    {
        UpdateProps();
        var cars = DensestPack(); var l = Leader(); if (l != null && !cars.Contains(l)) cars.Add(l);
        PropCam best = null; float bestD = float.MaxValue;
        foreach (var p in _props.Where(p => !p.hit && p.go != null))
            foreach (var c in cars)
            {
                var a = Anchor(c).position; var to = p.go.transform.position - a; to.y = 0;
                float d = to.magnitude;
                if (d < within && d < bestD && Vector3.Dot(to.normalized, RouteDir(c)) > 0.3f) { best = p; bestD = d; }
            }
        return best;
    }

    public static bool Prop(PropCam p)
    {
        if (!Ready || p?.go == null || p.hit) return false;
        RestoreFov();
        FollowCam.NEPFAEJAMGI.StopFollow(); FreeCam.NEPFAEJAMGI.MKJKDNAFJML = false;
        Traverse.Create(CamController.NEPFAEJAMGI).Method("LCMHCNJMNPM", p.go.transform.GetChild(0).gameObject).GetValue();
        _livingProp = p;
        Mode = "prop"; Target = p.go.name; _cars = DensestPack().Select(Game.Login).ToList(); Announce();
        return true;
    }

    public static bool PropNearLeader(float within = 220f)
    {
        UpdateProps();
        var l = Leader(); if (l?.JPHIMKLIAAO == null) return false;
        var near = Anchor(l).position + RouteDir(l) * 60f;
        var best = _props.Where(p => !p.hit && p.go != null).OrderBy(p => Vector3.Distance(p.go.transform.position, near)).FirstOrDefault();
        return best != null && Vector3.Distance(best.go.transform.position, near) <= within && Prop(best);
    }

    public static CFBJLEBOFHJ Leader() => Racing().OrderByDescending(v => v.MNHJMCLOGPB).FirstOrDefault() ?? Game.Ranked().FirstOrDefault();

    // ---- one-off shots / director ----

    public static bool Shot(System.Func<bool> shot, float seconds)
    {
        if (_focus != null) Plugin.Instance.StopCoroutine(_focus);
        if (!shot()) return false;
        _autoPausedUntil = seconds > 0 ? Time.time + seconds : float.MaxValue;
        if (seconds > 0 && Auto) _focus = Plugin.Instance.StartCoroutine(ResumeAfter(seconds));
        return true;
    }
    static IEnumerator ResumeAfter(float s) { yield return new WaitForSeconds(s); _autoPausedUntil = 0; _focus = null; }

    public static void SetAuto(bool on)
    {
        Auto = on;
        if (_auto != null) { Plugin.Instance.StopCoroutine(_auto); _auto = null; }
        if (on) { _autoPausedUntil = 0; _auto = Plugin.Instance.StartCoroutine(Director()); }
        Announce();
    }

    public static void OnBoom(CFBJLEBOFHJ v)
    {
        LastBoomed = v;
        if (Auto && On("boom") && Game.Running && Time.time >= _autoPausedUntil) { Shot(() => Orbit(v, 6f, 60f, 50f), 6f); _leaderDue = true; } // fuse + launch, then straight back to the leader
    }

    static PropCam _lastProp;
    static bool _leaderDue; // the last shot didn't have 1st place in it -> next one must
    static readonly float[] Fovs = { 60f, 60f, 65f }; // the streamer never touches FOV: stay near the game's 60

    public static void OnRaceStart() => _raceStart = Time.time;

    // ---- coverage-driven director ----
    // Rule learned from the streamer: keep as many cars in frame as you can. Hold a shot while it still shows most
    // of the field; when coverage drops (or the shot gets stale), cut to whichever candidate shot would show the most
    // cars, keeping 1st place in the rotation. Booms, the finish and the track cams still take priority.
    static int InViewCount(Vector3 pos, Vector3 look, float fov, out bool leaderIn)
    {
        var cam = MainCam; leaderIn = false;
        if (cam == null) return 0;
        var view = Matrix4x4.TRS(pos, Quaternion.LookRotation(look - pos, Vector3.up), Vector3.one).inverse;
        var proj = Matrix4x4.Perspective(fov, cam.aspect, 0.3f, 2000f);
        var vp = proj * view; int n = 0; var leader = Leader();
        foreach (var v in Racing())
        {
            var c = vp * new Vector4(Anchor(v).position.x, Anchor(v).position.y + 1f, Anchor(v).position.z, 1f);
            if (c.w <= 0) continue;
            float x = c.x / c.w, y = c.y / c.w;
            if (x > -0.92f && x < 0.92f && y > -0.85f && y < 0.85f) { n++; if (v == leader) leaderIn = true; }
        }
        return n;
    }

    static int CurrentCoverage(out bool leaderIn)
    {
        var cam = MainCam; leaderIn = false; if (cam == null) return 0;
        int n = 0; var leader = Leader();
        foreach (var v in Racing())
        {
            var p = cam.WorldToViewportPoint(Anchor(v).position + Vector3.up);
            if (p.z > 0 && p.x > 0.04f && p.x < 0.96f && p.y > 0.06f && p.y < 0.94f) { n++; if (v == leader) leaderIn = true; }
        }
        return n;
    }

    // Where a candidate shot would put the camera (same maths the live shots use), without applying it.
    static bool CandidatePose(string kind, CFBJLEBOFHJ car, out Vector3 pos, out Vector3 look)
    {
        pos = look = Vector3.zero;
        var group = DensestPack(); if (group.Count == 0) return false;
        var ordered = group.OrderBy(v => v.MNHJMCLOGPB).ToList(); var rear = ordered.First(); var front = ordered.Last();
        var centroid = group.Aggregate(Vector3.zero, (acc, v) => acc + Anchor(v).position) / group.Count;
        var dir = RouteDir(rear); float spread = Vector3.Distance(Anchor(rear).position, Anchor(front).position);
        var sideV = Vector3.Cross(Vector3.up, dir);
        switch (kind)
        {
            case "followwide":
            {
                if (car?.JPHIMKLIAAO == null) return false;
                var a = Anchor(car).position;
                var others = Racing().Where(o => o != car).ToList();
                var l = others.Count > 0 ? others.Aggregate(Vector3.zero, (acc, o) => acc + Anchor(o).position) / others.Count - a : Heading(car);
                l.y = 0; if (l.sqrMagnitude < 1f) l = RouteDir(car);
                float yaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                pos = Quaternion.Euler(22f, yaw, 0f) * new Vector3(0, 0, -40f) + a; look = a; return true;
            }
            case "high":
                pos = centroid - dir * (45f + spread * 0.35f) + sideV * 25f + Vector3.up * (35f + spread * 0.25f); look = centroid + dir * 12f; return true;
            case "high2":
                pos = centroid - dir * (45f + spread * 0.35f) - sideV * 25f + Vector3.up * (35f + spread * 0.25f); look = centroid + dir * 12f; return true;
            case "side":
                pos = centroid + sideV * (22f + spread * 0.3f) + Vector3.up * (8f + spread * 0.15f) - dir * 4f; look = centroid + dir * (14f + spread * 0.3f) + Vector3.up; return true;
            case "pack":
                pos = Anchor(rear).position - dir * (20f + spread * 0.45f) + Vector3.up * (9f + spread * 0.3f); look = centroid + dir * 6f + Vector3.up; return true;
            case "sweep":
            {
                var c = WaypointController.NEPFAEJAMGI?.GetCircuit(); if (c == null) return false;
                var rp = c.GetRoutePoint(rear.MNHJMCLOGPB + 90f); var d2 = Flat(rp.AHLOFCFINIO, Vector3.forward);
                pos = rp.PFHEOLIHHHD + Vector3.Cross(Vector3.up, d2) * 26f + Vector3.up * 11f; look = centroid + Vector3.up; return true;
            }
        }
        return false;
    }

    static float _shotStart; static string _shotKey; static bool _wasRunningDir;
    static CFBJLEBOFHJ _lastLeader; static float _lastDuel = -100f, _runningSince;
    static void Took(string key) { _shotStart = Time.time; _shotKey = key; }

    static IEnumerator Director()
    {
        float lowSince = -1f;
        while (Auto)
        {
            yield return new WaitForSeconds(0.5f);
            if (!Auto) yield break;
            if (!Game.Running) { _wasRunningDir = false; continue; }
            if (!_wasRunningDir) { _wasRunningDir = true; _runningSince = Time.time; _lastLeader = null; }
            if (ManualHold || Time.time < _autoPausedUntil) continue;
            var racing = Racing(); if (racing.Count == 0) continue;

            // priorities: finish, then a track cam a group is about to pass
            if (On("finish") && Approaching().Count > 0)
            {
                if (Mode != "finish") { Finish(0, 60f, 60f); Took("finish"); }
                _afterFinishShown = false; continue;
            }
            if (Mode == "finish" || (!_afterFinishShown && Game.Vehicles().Any(v => v.NIKOEDJIAFB)))
            {
                _afterFinishShown = true;
                var l = Leader(); if (l != null) { FollowWide(l); Took("wide:" + Game.Login(l)); }
                continue;
            }
            float age = Time.time - _shotStart;

            // Crashes: a pile-up (3+ cars down within 8 s) is worth a look; a single crash is not — go find the action.
            int recent = Game.CrashedAt.Values.Count(t => Time.time - t < 8f);
            if (On("pileup") && recent >= 3 && _shotKey != "pileup")
            {
                _group = Game.CrashedAt.Keys.Where(v => v != null && v.JPHIMKLIAAO != null).ToList();
                if (High(0, 60f, 60f)) { Took("pileup"); continue; }
            }
            if (Mode == "followwide" && _followCar != null && Game.CrashedAt.ContainsKey(_followCar) && age > 3f)
            {
                var alive = Racing().Where(v => !Game.CrashedAt.ContainsKey(v)).OrderByDescending(v => v.MNHJMCLOGPB).FirstOrDefault();
                if (alive != null) { FollowWide(alive); Took("wide:" + Game.Login(alive)); _leaderDue = alive != Leader(); continue; }
            }

            var ap = ApproachingProp();
            if (On("prop") && ap != null && ap != _lastProp && age > 6f && Random.value < 0.5f) { Prop(ap); _lastProp = ap; Took("prop"); continue; }
            if (Mode != "prop") _lastProp = null;

            // opening: the launch from the road ahead (cars come at the camera), then one high overview
            float sinceStart = Time.time - _runningSince;
            if (sinceStart < 6f && On("grid")) { if (Mode != "grid") { Grid(0); Took("grid"); } continue; }
            if (sinceStart < 16f && On("high")) { if (Mode != "high") { High(0, 60f, 60f); Took("high"); } continue; }

            // the lead changed hands: that's the story, show the new leader from the front for a bit
            var leadNow = Leader();
            if (leadNow != null && _lastLeader != null && leadNow != _lastLeader && age > 4f)
            {
                _lastLeader = leadNow;
                if (On("front") && Front(leadNow, 7f, 60f, 60f)) { Took("front:" + Game.Login(leadNow)); _leaderDue = false; continue; }
            }
            _lastLeader = leadNow;

            // a duel for the lead: top two within 25 units -> a tight side shot on just those two, every so often
            var top2 = racing.OrderByDescending(v => v.MNHJMCLOGPB).Take(2).ToList();
            if (On("duel") && top2.Count == 2 && top2[0].MNHJMCLOGPB - top2[1].MNHJMCLOGPB < 25f && Time.time - _lastDuel > 40f && age > 8f)
            {
                _lastDuel = Time.time;
                if (Side(0, 60f, 60f)) { _group = top2; Took("duel"); continue; }
            }

            // hold while the shot still shows most of the field (or all but two), unless it's gone stale
            int seen = CurrentCoverage(out bool leaderIn);
            bool good = seen >= Mathf.Max(2, Mathf.CeilToInt(racing.Count * 0.6f)) || seen >= racing.Count - 2;
            if (good) lowSince = -1f; else if (lowSince < 0) lowSince = Time.time;
            bool stale = age > 45f, minHeld = age >= 8f;
            bool cut = minHeld && ((!good && Time.time - lowSince > 2f) || stale || (age > 25f && !leaderIn && _leaderDue));
            if (Mode == "prop" && age > 9f) cut = true; // fixed cams don't move: never sit on one for long
            if (_shotKey == "pileup" && age > 8f) cut = true;
            if (_shotKey == "duel" && age > 10f) cut = true;
            if (!cut) continue;

            // pick the candidate that would show the most cars; prefer ones with the leader; never the same shot again
            var leader = Leader(); var top = racing.Where(v => !Game.CrashedAt.ContainsKey(v)).OrderByDescending(v => v.MNHJMCLOGPB).Take(3).ToList();
            if (top.Count == 0) top = racing.OrderByDescending(v => v.MNHJMCLOGPB).Take(3).ToList();
            var cands = new List<(string key, System.Func<bool> go, int n, bool lead)>();
            void Add(string key, string kind, CFBJLEBOFHJ car, System.Func<bool> go)
            {
                if (key == _shotKey || !On(key) || !CandidatePose(kind, car, out var pos, out var look)) return;
                int n = InViewCount(pos, look, 60f, out bool li); cands.Add((key, go, n, li));
            }
            foreach (var c in top) { var cc = c; Add("wide:" + Game.Login(cc), "followwide", cc, () => FollowWide(cc)); }
            Add("high", "high", null, () => High(0, 60f, 60f));
            Add("high2", "high2", null, () => { _sideSign = -1f; return Racing().Count > 0 && Begin("high", null, 0, 60f, 60f); });
            Add("side", "side", null, () => Side(0, 60f, 60f));
            Add("pack", "pack", null, () => Pack(0, 60f, 60f));
            Add("sweep", "sweep", null, () => Sweep(0, 60f, 60f));
            if (cands.Count == 0) continue;
            bool needLeader = _leaderDue || !leaderIn;
            var pick = cands.OrderByDescending(c => c.n + (needLeader && c.lead ? 2 : 0) + (c.key.StartsWith("wide") ? 1 : 0)).First();
            if (pick.go()) { Took(pick.key); _leaderDue = !pick.lead; }
        }
    }

    // The game positions the name labels in CarLabel.LateUpdate. If that ran before our camera move this frame,
    // labels lag one frame behind the cars and stutter. Re-place them after we move the camera.
    static CarLabel[] _labels = new CarLabel[0]; static float _labelsScanned = -10f;
    public static bool Steering => Mode == "chase" || Mode == "front" || Mode == "orbit" || Mode == "pack" || Mode == "sweep" || Mode == "side" || Mode == "high" || Mode == "overhead" || Mode == "finish" || Mode == "grid";
    public static void RefreshLabels()
    {
        if (!Steering) return;
        if (Time.time - _labelsScanned > 2f) { _labels = Object.FindObjectsOfType<CarLabel>(); _labelsScanned = Time.time; }
        foreach (var l in _labels) if (l != null && l.isActiveAndEnabled) { try { l.LateUpdate(); } catch { } }
    }

    public static void OnRaceEnd()
    {
        _props.Clear(); _propsScanned = -1f; _lastProp = null; _livingProp = null; _afterFinishShown = false; _manualUntil = 0;
        if (_focus != null) { Plugin.Instance.StopCoroutine(_focus); _focus = null; }
        _autoPausedUntil = 0; Mode = null; Target = null; RestoreFov(); Announce();
    }
}
