// Cam director: the coverage-driven auto camera. Rule learned from the streamer: keep as many cars in frame as you
// can. Hold a shot while it still shows most of the field; when coverage drops (or the shot gets stale), cut to
// whichever candidate shot would show the most cars, keeping 1st place in the rotation. Booms, the finish, pile-ups,
// lead changes, duels and the track cams take priority. Shot keys ("high", "wide:<login>", "pileup", ...) are the
// settings.camera.shots toggle names (Pure.ShotKind) and the "never the same shot twice" memory.
using System.Collections;

namespace StreamRacerApi;

static partial class Cam
{
    static PropCam _lastProp;
    static bool _leaderDue;       // the last shot didn't have 1st place in it -> next one must
    static float _lastCutAt; static string _lastCutKey; static bool _wasRunning;
    static Vehicle _lastLeader; static float _lastDuelAt = -100f, _lastOverheadAt = -100f, _runningSince;
    // Every so often, a short look straight down so viewers can see where the whole field is on the track.
    public const float OverheadEvery = 35f, OverheadHold = 5f;
    static void NoteCut(string key) { _lastCutAt = Time.time; _lastCutKey = key; }

    public static void OnBoom(Vehicle vehicle)
    {
        LastBoomed = vehicle;
        if (Auto && On("boom") && Game.Running && Time.time >= _autoPausedUntil) { Shot(() => Orbit(vehicle, 6f, 60f, 50f), 6f); _leaderDue = true; } // fuse + launch, then straight back to the leader
    }

    // How many racers a camera at `position` looking at `lookAt` with `fov` would have in frame (without moving the camera).
    static int InViewCount(Vector3 position, Vector3 lookAt, float fov, out bool leaderIn)
    {
        var camera = MainCam; leaderIn = false;
        if (camera == null) return 0;
        var view = Matrix4x4.TRS(position, Quaternion.LookRotation(lookAt - position, Vector3.up), Vector3.one).inverse;
        var projection = Matrix4x4.Perspective(fov, camera.aspect, 0.3f, 2000f);
        var viewProjection = projection * view; int count = 0; var leader = Leader();
        foreach (var vehicle in Racing())
        {
            var anchor = Anchor(vehicle).position;
            var clip = viewProjection * new Vector4(anchor.x, anchor.y + 1f, anchor.z, 1f);
            if (clip.w <= 0) continue;
            float x = clip.x / clip.w, y = clip.y / clip.w;
            if (x > -0.92f && x < 0.92f && y > -0.85f && y < 0.85f) { count++; if (vehicle == leader) leaderIn = true; }
        }
        return count;
    }

    // How many racers the live camera has in frame right now.
    static int CurrentCoverage(out bool leaderIn)
    {
        var camera = MainCam; leaderIn = false; if (camera == null) return 0;
        int count = 0; var leader = Leader();
        foreach (var vehicle in Racing())
        {
            var viewport = camera.WorldToViewportPoint(Anchor(vehicle).position + Vector3.up);
            if (viewport.z > 0 && viewport.x > 0.04f && viewport.x < 0.96f && viewport.y > 0.06f && viewport.y < 0.94f) { count++; if (vehicle == leader) leaderIn = true; }
        }
        return count;
    }

    // Where a candidate shot would put the camera (same maths the live shots use), without applying it.
    static bool CandidatePose(string kind, Vehicle car, out Vector3 position, out Vector3 lookAt)
    {
        position = lookAt = Vector3.zero;
        var group = DensestPack(); if (group.Count == 0) return false;
        var ordered = group.OrderBy(vehicle => vehicle.Progress()).ToList(); var rear = ordered.First(); var front = ordered.Last();
        var centroid = Centroid(group);
        var direction = RouteDir(rear); float spread = Vector3.Distance(Anchor(rear).position, Anchor(front).position);
        var sideVector = Vector3.Cross(Vector3.up, direction);
        switch (kind)
        {
            case "followwide":
            {
                if (car?.Car() == null) return false;
                var anchor = Anchor(car).position;
                var others = Racing().Where(other => other != car).ToList();
                var look = others.Count > 0 ? Centroid(others) - anchor : Heading(car);
                look.y = 0; if (look.sqrMagnitude < 1f) look = RouteDir(car);
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                position = Quaternion.Euler(22f, yaw, 0f) * new Vector3(0, 0, -40f) + anchor; lookAt = anchor; return true;
            }
            case "chase":
            {
                if (car?.Car() == null) return false;
                var anchor = Anchor(car).position; var forward = Heading(car);
                position = anchor - forward * 18f + Vector3.up * 7f; lookAt = anchor + forward * 12f + Vector3.up; return true;
            }
            case "high":
                position = centroid - direction * (45f + spread * 0.35f) + sideVector * 25f + Vector3.up * (35f + spread * 0.25f); lookAt = centroid + direction * 12f; return true;
            case "high2":
                position = centroid - direction * (45f + spread * 0.35f) - sideVector * 25f + Vector3.up * (35f + spread * 0.25f); lookAt = centroid + direction * 12f; return true;
            case "side":
                position = centroid + sideVector * (22f + spread * 0.3f) + Vector3.up * (8f + spread * 0.15f) - direction * 4f; lookAt = centroid + direction * (14f + spread * 0.3f) + Vector3.up; return true;
            case "pack":
                position = Anchor(rear).position - direction * (20f + spread * 0.45f) + Vector3.up * (9f + spread * 0.3f); lookAt = centroid + direction * 6f + Vector3.up; return true;
            case "sweep":
            {
                var circuit = Instances.WaypointController?.GetCircuit(); if (circuit == null) return false;
                var routePoint = circuit.GetRoutePoint(rear.Progress() + 90f); var routeDirection = Flat(routePoint.Direction(), Vector3.forward);
                position = routePoint.Position() + Vector3.Cross(Vector3.up, routeDirection) * 26f + Vector3.up * 11f; lookAt = centroid + Vector3.up; return true;
            }
        }
        return false;
    }

    static IEnumerator Director()
    {
        float lowSince = -1f;
        while (Auto)
        {
            yield return new WaitForSeconds(0.5f);
            if (!Auto) yield break;
            if (!Game.Running) { _wasRunning = false; continue; }
            if (!_wasRunning) { _wasRunning = true; _runningSince = Time.time; _lastLeader = null; _lastOverheadAt = Time.time; }
            if (ManualHold || Time.time < _autoPausedUntil) continue;
            var racing = Racing(); if (racing.Count == 0) continue;

            // priorities: finish, then a track cam a group is about to pass
            if (On("finish") && Approaching().Count > 0)
            {
                if (Mode != CameraMode.Finish) { Finish(0, 60f, 60f); NoteCut("finish"); }
                _afterFinishShown = false; continue;
            }
            if (Mode == CameraMode.Finish || (!_afterFinishShown && Game.Vehicles().Any(vehicle => vehicle.HasFinished())))
            {
                _afterFinishShown = true;
                var leader = Leader(); if (leader != null) { FollowWide(leader); NoteCut("wide:" + Game.Login(leader)); }
                continue;
            }
            float age = Time.time - _lastCutAt;

            // Crashes: a pile-up (3+ cars down within 8 s) is worth a look; a single crash is not — go find the action.
            int recentCrashes = Game.CrashedAt.Values.Count(at => Time.time - at < 8f);
            if (On("pileup") && recentCrashes >= 3 && _lastCutKey != "pileup")
            {
                _shotGroup = Game.CrashedAt.Keys.Where(vehicle => vehicle != null && vehicle.Car() != null).ToList();
                if (High(0, 60f, 60f)) { NoteCut("pileup"); continue; }
            }
            if (Mode == CameraMode.FollowWide && _followCar != null && Game.CrashedAt.ContainsKey(_followCar) && age > 3f)
            {
                var alive = Racing().Where(vehicle => !Game.CrashedAt.ContainsKey(vehicle)).OrderByDescending(vehicle => vehicle.Progress()).FirstOrDefault();
                if (alive != null) { FollowWide(alive); NoteCut("wide:" + Game.Login(alive)); _leaderDue = alive != Leader(); continue; }
            }

            var approachingProp = ApproachingProp();
            if (On("prop") && approachingProp != null && approachingProp != _lastProp && age > 6f && Random.value < 0.5f) { Prop(approachingProp); _lastProp = approachingProp; NoteCut("prop"); continue; }
            if (Mode != CameraMode.Prop) _lastProp = null;

            // opening: the launch from the road ahead (cars come at the camera), then one high overview
            float sinceStart = Time.time - _runningSince;
            if (sinceStart < 6f && On("grid")) { if (Mode != CameraMode.Grid) { Grid(0); NoteCut("grid"); } continue; }
            if (sinceStart < 16f && On("high")) { if (Mode != CameraMode.High) { High(0, 60f, 60f); NoteCut("high"); } continue; }

            // the lead changed hands: that's the story, show the new leader from the front for a bit
            var leadNow = Leader();
            if (leadNow != null && _lastLeader != null && leadNow != _lastLeader && age > 4f)
            {
                _lastLeader = leadNow;
                if (On("front") && Front(leadNow, 7f, 60f, 60f)) { NoteCut("front:" + Game.Login(leadNow)); _leaderDue = false; continue; }
            }
            _lastLeader = leadNow;

            // a duel for the lead: top two within 25 units -> a tight side shot on just those two, every so often
            var topTwo = racing.OrderByDescending(vehicle => vehicle.Progress()).Take(2).ToList();
            if (On("duel") && topTwo.Count == 2 && topTwo[0].Progress() - topTwo[1].Progress() < 25f && Time.time - _lastDuelAt > 40f && age > 8f)
            {
                _lastDuelAt = Time.time;
                if (Side(0, 60f, 60f)) { _shotGroup = topTwo; NoteCut("duel"); continue; }
            }

            // a quick overhead every so often: the whole track at once, so nobody loses track of where they are
            if (On("overhead") && Mode != CameraMode.Overhead && age > 8f && Time.time - _lastOverheadAt > OverheadEvery)
            {
                _lastOverheadAt = Time.time;
                if (Overhead(0, 60f, 60f)) { NoteCut("overhead"); continue; }
            }

            // hold while the shot still shows most of the field (or all but two), unless it's gone stale
            int seen = CurrentCoverage(out bool leaderIn);
            bool good = seen >= Mathf.Max(2, Mathf.CeilToInt(racing.Count * 0.6f)) || seen >= racing.Count - 2;
            if (good) lowSince = -1f; else if (lowSince < 0) lowSince = Time.time;
            bool stale = age > 45f, minHeld = age >= 8f;
            bool cut = minHeld && ((!good && Time.time - lowSince > 2f) || stale || (age > 25f && !leaderIn && _leaderDue));
            if (_lastCutKey == "overhead") cut = age > OverheadHold;   // it is deliberately a brief shot
            if (Mode == CameraMode.Prop && age > 9f) cut = true; // fixed cams don't move: never sit on one for long
            if (_lastCutKey == "pileup" && age > 8f) cut = true;
            if (_lastCutKey == "duel" && age > 10f) cut = true;
            if (_lastCutKey == "overhead" && age > OverheadHold) cut = true;   // a glance, not a stay
            if (!cut) continue;

            // pick the candidate that would show the most cars; prefer ones with the leader; never the same shot again
            var top = racing.Where(vehicle => !Game.CrashedAt.ContainsKey(vehicle)).OrderByDescending(vehicle => vehicle.Progress()).Take(3).ToList();
            if (top.Count == 0) top = racing.OrderByDescending(vehicle => vehicle.Progress()).Take(3).ToList();
            var candidates = new List<(string key, System.Func<bool> take, int inView, bool hasLeader)>();
            void Consider(string key, string kind, Vehicle car, System.Func<bool> take)
            {
                if (key == _lastCutKey || !On(key) || !CandidatePose(kind, car, out var position, out var lookAt)) return;
                int inView = InViewCount(position, lookAt, 60f, out bool hasLeader); candidates.Add((key, take, inView, hasLeader));
            }
            // Shots from behind a car come first: that is the view racers need to judge a straight and spend a boost.
            foreach (var vehicle in top)
            {
                var followed = vehicle;
                Consider("wide:" + Game.Login(followed), "followwide", followed, () => FollowWide(followed));
                Consider("chase:" + Game.Login(followed), "chase", followed, () => Chase(followed, 0, 60f, 60f));
            }
            Consider("high", "high", null, () => High(0, 60f, 60f));
            Consider("high2", "high2", null, HighOtherSide);
            Consider("side", "side", null, () => Side(0, 60f, 60f));
            Consider("pack", "pack", null, () => Pack(0, 60f, 60f));
            Consider("sweep", "sweep", null, () => Sweep(0, 60f, 60f));
            if (candidates.Count == 0) continue;
            bool needLeader = _leaderDue || !leaderIn;
            int behindBonus(string key) => key.StartsWith("chase") ? 4 : key.StartsWith("wide") ? 3 : key == "pack" || key == "high" || key == "high2" ? 1 : 0;
            var pick = candidates.OrderByDescending(candidate => candidate.inView + (needLeader && candidate.hasLeader ? 2 : 0) + behindBonus(candidate.key)).First();
            if (pick.take()) { NoteCut(pick.key); _leaderDue = !pick.hasLeader; }
        }
    }
}
