using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine.UI;
using System.Linq;
using Cage.StreamRacer;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Vehicles.Car;

namespace StreamRacerApi;

// ponytail: obfuscated member names referenced directly. If a game update renames
// them the affected route throws and returns 501; fix = update the name here.
static class Game
{
    static readonly System.Reflection.FieldInfo BoostsField =
        AccessTools.Field(typeof(CFBJLEBOFHJ), "MKFJMCDNHMA");
    static readonly System.Reflection.FieldInfo FinishAtField =
        AccessTools.Field(typeof(CFBJLEBOFHJ), "BNMEBMGJHDF");

    public static float FinishAt(CFBJLEBOFHJ v) => (float)FinishAtField.GetValue(v);

    // The game's own Twitch app token (Helix-capable) and client id, so we can resolve logins without another auth flow.
    public static string TwitchToken => HCPAEADBKIA.JIMBAOHPFOG?.HENMPBNKMKD()?.BINBLDPOLPE;
    public static string TwitchClientId => ApplicationController.IANOCCLIMAA;

    public static string StreamerId => HCPAEADBKIA.JIMBAOHPFOG?.HENMPBNKMKD()?.DNJFGHLIAIM;
    public static string StreamerLogin => HCPAEADBKIA.JIMBAOHPFOG?.HENMPBNKMKD()?.DBOIOBOEHLN;
    public static CFBJLEBOFHJ StreamerVehicle() =>
        Vehicles().FirstOrDefault(v => v.JDDOIMHIFHK.DNJFGHLIAIM == StreamerId || v.JDDOIMHIFHK.JLDPKDLFPJP == StreamerLogin);

    public static bool Running => GameController.NEPFAEJAMGI != null && GameController.NEPFAEJAMGI.IsGameRunning();

    public static List<CFBJLEBOFHJ> Vehicles() =>
        VehicleManager.NEPFAEJAMGI == null ? new List<CFBJLEBOFHJ>() : VehicleManager.NEPFAEJAMGI.GetVehicles();

    // Same ordering the in-game leaderboard uses: progress descending, finishers get bumped high.
    public static List<CFBJLEBOFHJ> Ranked() => Vehicles().OrderByDescending(v => v.MNHJMCLOGPB).ToList();

    public static CFBJLEBOFHJ Find(string idOrLogin) =>
        Vehicles().FirstOrDefault(v => v.JDDOIMHIFHK.DNJFGHLIAIM == idOrLogin || v.JDDOIMHIFHK.JLDPKDLFPJP == idOrLogin);

    public static string Login(CFBJLEBOFHJ v) => v.JDDOIMHIFHK.JLDPKDLFPJP;

    // Twitch profile pictures per login, resolved in the background via Helix so overlays get a picture.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Avatars = new();
    static readonly HashSet<string> AvatarPending = new();

    public static void EnsureAvatars()
    {
        var need = Vehicles().Select(Login).Where(l => !string.IsNullOrEmpty(l) && !Avatars.ContainsKey(l) && !AvatarPending.Contains(l)).Distinct().Take(100).ToList();
        if (need.Count == 0 || string.IsNullOrEmpty(TwitchToken)) return;
        foreach (var l in need) AvatarPending.Add(l);
        string token = TwitchToken, clientId = TwitchClientId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var wc = new WebClient();
                wc.Headers["Authorization"] = "Bearer " + token; wc.Headers["Client-Id"] = clientId;
                string url = "https://api.twitch.tv/helix/users?" + string.Join("&", need.Select(n => "login=" + System.Uri.EscapeDataString(n)));
                var data = Newtonsoft.Json.Linq.JObject.Parse(wc.DownloadString(url))["data"];
                foreach (var u in data) Avatars[(string)u["login"]] = (string)u["profile_image_url"];
                foreach (var l in need) Avatars.TryAdd(l, null); // not on twitch -> remember that too
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("avatar lookup failed: " + e.Message); }
            lock (AvatarPending) foreach (var l in need) AvatarPending.Remove(l);
        });
    }

    // Custom images are served by the plugin (they may be local files), Twitch avatars are direct CDN urls.
    public static string Avatar(CFBJLEBOFHJ v) =>
        ImageSource(Login(v)) != null ? "/image/" + Login(v) : Avatars.TryGetValue(Login(v) ?? "", out var a) ? a : null;

    // Bytes of a custom image (file or url), cached. Null if none / failed.
    // Where a login's picture comes from: a live join, a custom bot, or an auto-join entry.
    public static string ImageSource(string login)
    {
        if (login == null) return null;
        if (Images.TryGetValue(login, out var s) && !string.IsNullOrEmpty(s)) return s;
        var cb = Settings.CustomBot(login); if (!string.IsNullOrEmpty(cb?.image)) return cb.image;
        var aj = Settings.Current.autoJoin.FirstOrDefault(e => e.login == login.ToLowerInvariant()); if (!string.IsNullOrEmpty(aj?.image)) return aj.image;
        return null;
    }

    public static string ImageDir => Path.Combine(BepInEx.Paths.ConfigPath, "shibiko.streamracer.images");

    // Save an uploaded picture (data URL) for a login; returns the file path to store in settings.
    public static string SaveImage(string login, string dataUrl)
    {
        var m = System.Text.RegularExpressions.Regex.Match(dataUrl ?? "", @"^data:image/(png|jpe?g|gif|webp);base64,(.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return null;
        Directory.CreateDirectory(ImageDir);
        string ext = m.Groups[1].Value.ToLowerInvariant() == "jpeg" ? "jpg" : m.Groups[1].Value.ToLowerInvariant();
        string safe = System.Text.RegularExpressions.Regex.Replace(login.ToLowerInvariant(), "[^a-z0-9_]", "");
        string path = Path.Combine(ImageDir, safe + "." + ext);
        File.WriteAllBytes(path, System.Convert.FromBase64String(m.Groups[2].Value));
        ImageCache.Remove(path);
        return path.Replace('\\', '/');
    }

    public static byte[] CustomImageBytes(string login)
    {
        var src = ImageSource(login);
        if (src == null) return null;
        if (ImageCache.TryGetValue(src, out var b)) return b;
        try
        {
            if (File.Exists(src)) b = File.ReadAllBytes(src);
            else { var wc = new WebClient(); wc.Headers["User-Agent"] = "StreamRacerApi"; b = wc.DownloadData(src); }
            ImageCache[src] = b; return b;
        }
        catch { return null; }
    }

    // The route as a polyline (sampled along the circuit), cached per track.
    static List<Vector3> _track; static object _trackKey;
    public static List<Vector3> TrackPoints()
    {
        var circuit = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (circuit == null) return new List<Vector3>();
        var wps = circuit.IPOOGHHOEGD;
        object key = wps == null || wps.Length == 0 ? null : (object)(wps.Length + ":" + (wps[0] != null ? wps[0].position.ToString() : "") + ":" + (wps[wps.Length - 1] != null ? wps[wps.Length - 1].position.ToString() : ""));
        if (_track != null && key != null && key.Equals(_trackKey)) return _track;
        // The waypoint transforms are the road itself, in order. Route-distance sampling can jump across gaps, so avoid it.
        var pts = circuit.IPOOGHHOEGD.Where(t => t != null).Select(t => t.position).ToList();
        // The waypoint list ends with a hop back to the start (closing the circuit for the AI). Cut the outline
        // at the first segment that is far longer than the typical spacing so no start-finish line gets drawn.
        if (pts.Count > 6)
        {
            var segs = Enumerable.Range(0, pts.Count - 1).Select(i => Vector3.Distance(pts[i], pts[i + 1])).OrderBy(x => x).ToList();
            float median = segs[segs.Count / 2], limit = Mathf.Max(60f, median * 6f);
            // the closing hop back to the start lives at the tail; only cut there (last 20 %), never at the front
            for (int i = (int)(pts.Count * 0.8f); i < pts.Count - 1; i++)
                if (Vector3.Distance(pts[i], pts[i + 1]) > limit) { pts = pts.Take(i + 1).ToList(); break; }
        }
        // densify long straights a little so the line renderer's corners stay smooth
        var dense = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            dense.Add(pts[i]);
            if (i + 1 < pts.Count)
            {
                float d = Vector3.Distance(pts[i], pts[i + 1]);
                for (int k = 1; k < (int)(d / 12f); k++) dense.Add(Vector3.Lerp(pts[i], pts[i + 1], k * 12f / d));
            }
        }
        pts = dense;
        _track = pts; _trackKey = key;
        return pts;
    }

    // ---- boost zones ----
    // Straights worth boosting on: stretches where the route barely turns and stays flat over the next ~40 units.
    // Returned as [start, end] route distances; cars boost when their progress is inside one (early part preferred).
    static List<float[]> _zones; static object _zonesKey;
    public static List<float[]> BoostZones()
    {
        var circuit = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (circuit == null) return new List<float[]>();
        float length = circuit.BDFOFBNGLBA;
        float finish = Vehicles().Select(FinishAt).DefaultIfEmpty(0f).Max();
        if (finish > 0) length = Mathf.Min(length, finish); // the route keeps going past the finish line; nothing to boost for there
        object key = length + ":" + (circuit.IPOOGHHOEGD?.Length ?? 0);
        if (_zones != null && key.Equals(_zonesKey)) return _zones;
        var zones = new List<float[]>();
        if (length > 0)
        {
            const float step = 4f, look = 40f, maxTurn = 12f, maxSlope = 0.12f, minLen = 55f;
            float zoneStart = -1f;
            for (float d = 0; d + look < length; d += step)
            {
                var a = circuit.GetRoutePoint(d); var b = circuit.GetRoutePoint(d + look);
                var da = a.AHLOFCFINIO; da.y = 0; var db = b.AHLOFCFINIO; db.y = 0;
                float turn = Vector3.Angle(da, db);
                float slope = Mathf.Abs(b.PFHEOLIHHHD.y - a.PFHEOLIHHHD.y) / look;
                bool straight = turn < maxTurn && slope < maxSlope;
                if (straight && zoneStart < 0) zoneStart = d;
                if (!straight && zoneStart >= 0) { if (d - zoneStart >= minLen) zones.Add(new[] { zoneStart, d + look * 0.5f }); zoneStart = -1f; }
            }
            if (zoneStart >= 0 && length - zoneStart >= minLen) zones.Add(new[] { zoneStart, length });
        }
        _zones = zones; _zonesKey = key;
        return zones;
    }
    public static bool InBoostZone(float progress)
    {
        foreach (var z in BoostZones()) if (progress >= z[0] && progress <= z[1] - 20f) return true; // not right at the end of a straight
        return false;
    }
    public static object ZonesDto()
    {
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit();
        return new
        {
            map = GameController.NEPFAEJAMGI?.BFOGGNCDONA?.MHOHHJCNJOP, mapName = GameController.NEPFAEJAMGI?.BFOGGNCDONA?.FLAENOELFHN,
            length = c?.BDFOFBNGLBA ?? 0f, finishAt = Vehicles().Select(FinishAt).DefaultIfEmpty(0f).Max(),
            zones = BoostZones().Select(z => new { start = z[0], end = z[1], length = z[1] - z[0] }).ToList(),
        };
    }

    public static object TrackDto(bool raw = false)
    {
        var pts = raw ? (WaypointController.NEPFAEJAMGI?.GetCircuit()?.IPOOGHHOEGD?.Where(t => t != null).Select(t => t.position).ToList() ?? new List<Vector3>()) : TrackPoints();
        if (pts.Count == 0) return new { points = new List<float[]>(), bounds = (object)null };
        return new
        {
            points = pts.Select(p => new[] { p.x, p.z }).ToList(),
            bounds = new { minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x), minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z) },
        };
    }

    public static object Dto(CFBJLEBOFHJ v, int place) => new
    {
        place,
        id = v.JDDOIMHIFHK.DNJFGHLIAIM,
        login = v.JDDOIMHIFHK.JLDPKDLFPJP,
        displayName = v.JDDOIMHIFHK.AMCIKHEHBGM,
        color = "#" + ColorUtility.ToHtmlStringRGB(v.JDDOIMHIFHK.EKPDDGFGLNI),
        sub = v.JDDOIMHIFHK.HIMCIABAFNG,
        type = v.BDBCNPKDNMC.ToString(),
        progress = v.MNHJMCLOGPB,
        finishAt = FinishAt(v),
        pct = v.NIKOEDJIAFB ? 100f : Mathf.Clamp(v.MNHJMCLOGPB / Mathf.Max(1f, FinishAt(v)) * 100f, 0f, 100f),
        finished = v.NIKOEDJIAFB,
        boosts = Boosts(v),
        state = VehicleState(v),
        image = Images.TryGetValue(v.JDDOIMHIFHK.JLDPKDLFPJP ?? "", out var img) ? img : null,
        avatar = Avatar(v),
        title = Title(v),
        x = v.JPHIMKLIAAO != null ? (float?)v.JPHIMKLIAAO.transform.position.x : null,
        z = v.JPHIMKLIAAO != null ? (float?)v.JPHIMKLIAAO.transform.position.z : null,
    };

    // What the lobby row prints under the name: backend CustomTitle ("Developer", "Streamer", ...) else Subscriber / Normal Racer.
    public static string Title(CFBJLEBOFHJ v) => KPDGEINGANI.LJKJGOKBIJF(v.JDDOIMHIFHK);

    // The title arrives asynchronously from the game's backend after a join; wait for it, then announce devs.
    public static IEnumerator AnnounceTitle(CFBJLEBOFHJ v)
    {
        for (float t = 0; t < 8f && string.IsNullOrEmpty(v.JDDOIMHIFHK.GGCGDCLGPPL) && v.JDDOIMHIFHK.IGMMBKDMPLL == 0; t += 0.25f)
            yield return new WaitForSeconds(0.25f);
        if (!Vehicles().Contains(v)) yield break;
        if ((Title(v) ?? "").IndexOf("developer", System.StringComparison.OrdinalIgnoreCase) >= 0)
            Plugin.Emit("developer", EventDto(v));
        GrantPerks(v);
    }

    public static object EventDto(CFBJLEBOFHJ v) => Dto(v, Ranked().IndexOf(v) + 1);

    // Tiny per-frame payload for maps/overlays: [login, x, z, pct, place, finished] per car.
    public static object PosFrame()
    {
        var ranked = Ranked();
        var v = new List<object[]>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i]; var t = r.JPHIMKLIAAO != null ? r.JPHIMKLIAAO.transform.position : Vector3.zero;
            float pct = r.NIKOEDJIAFB ? 100f : Mathf.Clamp(r.MNHJMCLOGPB / Mathf.Max(1f, FinishAt(r)) * 100f, 0f, 100f);
            v.Add(new object[] { Login(r), Mathf.Round(t.x * 10f) / 10f, Mathf.Round(t.z * 10f) / 10f, Mathf.Round(pct * 10f) / 10f, i + 1, r.NIKOEDJIAFB ? 1 : 0, VehicleState(r) });
        }
        return new { t = Time.unscaledTime, v };
    }

    public static object Snapshot()
    {
        var ranked = Ranked();
        var gs = GameController.NEPFAEJAMGI?.BFOGGNCDONA;
        return new { running = Running, lobby = InLobby, streamer = StreamerLogin, map = gs == null ? null : new { id = gs.MHOHHJCNJOP, name = gs.FLAENOELFHN }, vehicles = ranked.Select((v, i) => Dto(v, i + 1)).ToList() };
    }

    // --- actions ---

    // 5s fuse + 4s stun in the game's explode coroutine
    const float BoomBusySeconds = 9f;
    static readonly Dictionary<CFBJLEBOFHJ, float> BoomedUntil = new();

    public static bool Boomable(CFBJLEBOFHJ v) =>
        !v.NIKOEDJIAFB && !(BoomedUntil.TryGetValue(v, out var t) && Time.unscaledTime < t);

    // Unlike the game's picker, skips cars already mid-boom. false = nobody left to hit.
    public static bool BoomRandom()
    {
        var pool = Vehicles().Where(Boomable).ToList();
        return pool.Count > 0 && Boom(pool[Random.Range(0, pool.Count)]);
    }

    public static bool Boom(CFBJLEBOFHJ v)
    {
        if (!Running || !Boomable(v)) return false;
        BoomedUntil[v] = Time.unscaledTime + BoomBusySeconds;
        v.NPGMKJHIGNO();
        return true;
    }

    public static bool Boost(CFBJLEBOFHJ v, float? force, float? seconds)
    {
        if (!Running || v.JPHIMKLIAAO == null) return false;
        var ai = v.JPHIMKLIAAO.GetComponent<CarAIControl>();
        if (ai == null || !ai.IsDriving()) return false;
        var car = v.JPHIMKLIAAO.GetComponent<Car>();
        car.Boost(force ?? Random.Range(2.5f, 15f), seconds ?? Random.Range(1.5f, 5f));
        Plugin.Emit("boost", EventDto(v));
        return true;
    }

    // Same as typing !boost: consumes one from the pool, random strength.
    public static bool UseBoost(CFBJLEBOFHJ v) => Running && v.MOEACENIMLO();

    // "On a straight" = the route keeps its heading for the next stretch ahead of the car.
    public static bool OnStraight(CFBJLEBOFHJ v, float lookAhead = 35f, float minDot = 0.96f)
    {
        var circuit = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (circuit == null || v.JPHIMKLIAAO == null) return false;
        var here = circuit.GetRoutePoint(v.MNHJMCLOGPB).AHLOFCFINIO.normalized;
        var ahead = circuit.GetRoutePoint(v.MNHJMCLOGPB + lookAhead).AHLOFCFINIO.normalized;
        var facing = v.JPHIMKLIAAO.transform.forward.normalized;
        return Vector3.Dot(here, ahead) > minDot && Vector3.Dot(facing, here) > 0.9f;
    }

    // Bot racers are AI cars nobody is typing !boost for, so drive their boosts: first try 0-4 s after
    // the start, then every 6-18 s, and only fire on a straight (fallback: fire anyway after waiting too long).
    // What a car is doing right now. driving | air | flipped | offroad | stunned (boomed) | stuck | finished
    public static string VehicleState(CFBJLEBOFHJ v)
    {
        if (v.NIKOEDJIAFB) return "finished";
        if (v.JPHIMKLIAAO == null) return "spawning";
        if (BoomedUntil.TryGetValue(v, out var t) && Time.unscaledTime < t) return "stunned";
        var tr = v.JPHIMKLIAAO.transform;
        if (Vector3.Dot(tr.up, Vector3.up) < 0.5f) return "flipped";
        var rb = v.JPHIMKLIAAO.GetComponent<Rigidbody>();
        bool grounded = Physics.Raycast(tr.position + Vector3.up * 0.5f, Vector3.down, 2.5f);
        if (!grounded || (rb != null && Mathf.Abs(rb.velocity.y) > 4f)) return "air";
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (c != null)
        {
            var p = c.GetRoutePoint(v.MNHJMCLOGPB).PFHEOLIHHHD;
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(tr.position.x, tr.position.z)) > 18f) return "offroad";
        }
        var ai = v.JPHIMKLIAAO.GetComponent<CarAIControl>();
        if (Running && ai != null && ai.IsDriving() && rb != null && rb.velocity.magnitude < 0.5f && Time.time - _runningSince > 10f) return "stuck";
        return "driving";
    }
    static float _runningSince = -1f; static bool _wasRunning;
    public static void NoteRaceStart() { }

    // Crash tracking: a car leaving "driving" for flipped/offroad/stuck (not a boom, not a jump) for > 1 s is a crash;
    // back to driving is a recovery. Emitted as SSE "crash" / "recovered"; recent crashes feed the director's pile-up rule.
    static readonly Dictionary<CFBJLEBOFHJ, string> _lastState = new();
    static readonly Dictionary<CFBJLEBOFHJ, float> _badSince = new();
    public static readonly Dictionary<CFBJLEBOFHJ, float> CrashedAt = new(); // still-crashed cars -> when
    static float _nextStateScan;
    public static void TrackStates()
    {
        if (Time.unscaledTime < _nextStateScan) return; _nextStateScan = Time.unscaledTime + 0.25f;
        if (!Running) { _wasRunning = false; if (_lastState.Count > 0) { _lastState.Clear(); _badSince.Clear(); CrashedAt.Clear(); } return; }
        if (!_wasRunning) { _wasRunning = true; _runningSince = Time.time; } // the green light, not the countdown
        foreach (var v in Vehicles())
        {
            string st = VehicleState(v);
            bool bad = st == "flipped" || st == "offroad" || st == "stuck";
            if (bad) { if (!_badSince.ContainsKey(v)) _badSince[v] = Time.time; }
            else _badSince.Remove(v);
            bool crashed = CrashedAt.ContainsKey(v);
            float need = st == "stuck" ? 4f : 1f; // a stall needs longer to count than a flip
            if (!crashed && bad && Time.time - _badSince[v] > need)
            {
                CrashedAt[v] = Time.time;
                Plugin.Emit("crash", new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, state = st, place = Ranked().IndexOf(v) + 1, pileup = CrashedAt.Values.Count(t => Time.time - t < 8f) });
            }
            else if (crashed && (st == "driving" || st == "finished" || st == "stunned"))
            {
                CrashedAt.Remove(v);
                Plugin.Emit("recovered", new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, place = Ranked().IndexOf(v) + 1 });
            }
            _lastState[v] = st;
        }
    }

    // A boost only makes sense with wheels on the road: not mid-boom, not airborne, not off in the grass.
    public static bool ReadyToBoost(CFBJLEBOFHJ v)
    {
        if (v.JPHIMKLIAAO == null || v.NIKOEDJIAFB) return false;
        if (BoomedUntil.TryGetValue(v, out var t) && Time.unscaledTime < t) return false;
        var tr = v.JPHIMKLIAAO.transform;
        var rb = v.JPHIMKLIAAO.GetComponent<Rigidbody>();
        if (rb != null && Mathf.Abs(rb.velocity.y) > 2.5f) return false;                       // flying / falling
        if (Vector3.Dot(tr.up, Vector3.up) < 0.7f) return false;                                // tipped over
        if (!Physics.Raycast(tr.position + Vector3.up * 0.5f, Vector3.down, 2.5f)) return false; // nothing under the car
        var c = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (c != null)
        {
            var p = c.GetRoutePoint(v.MNHJMCLOGPB).PFHEOLIHHHD;
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(tr.position.x, tr.position.z)) > 18f) return false; // off the road
        }
        return true;
    }

    public static IEnumerator AutoBoostRace()
    {
        while (!Running) yield return null;
        var next = new Dictionary<string, float>();
        var forced = new Dictionary<string, float>();
        foreach (var v in Vehicles().Where(v => Settings.IsBot(Login(v)) && Settings.AutoBoosts(Login(v)) && Login(v) != StreamerLogin))
            next[Login(v)] = Time.time + Random.Range(0f, 4f);
        while (Running)
        {
            yield return new WaitForSeconds(0.25f);
            foreach (var v in Vehicles())
            {
                string login = Login(v);
                if (!next.TryGetValue(login, out var at) || Time.time < at || v.NIKOEDJIAFB || Boosts(v) <= 0) continue;
                if (!Settings.AutoBoosts(login)) continue;                    // third-party controlled bot: leave its pool alone
                if (!ReadyToBoost(v)) { forced.Remove(login); continue; } // wait until they're back on the road, wheels down
                if (!forced.ContainsKey(login)) forced[login] = Time.time + 8f;
                bool good = InBoostZone(v.MNHJMCLOGPB) || (BoostZones().Count == 0 && OnStraight(v));
                if (!good && Time.time < forced[login]) continue;
                if (UseBoost(v)) { next[login] = Time.time + Random.Range(6f, 18f); forced.Remove(login); }
            }
        }
    }

    public static int Boosts(CFBJLEBOFHJ v) => (int)BoostsField.GetValue(v);
    public static void AddBoosts(CFBJLEBOFHJ v, int n) => BoostsField.SetValue(v, Boosts(v) + n);

    public static bool Respawn(CFBJLEBOFHJ v)
    {
        if (!Running || v.JPHIMKLIAAO == null || v.NIKOEDJIAFB) return false;
        var ai = v.JPHIMKLIAAO.GetComponent<CarAIControl>();
        if (ai == null) return false;
        ai.StartCoroutine("ANOOALMHGCC"); // the game's own stuck-car respawn coroutine
        Plugin.Emit("respawn", EventDto(v));
        return true;
    }

    // The AI driver resets the top-speed multiplier every frame, so we store the
    // slow here and re-apply it in a postfix on that update (see Patches).
    static readonly Dictionary<CarController, (float mult, float until)> Slows = new();

    public static bool Speed(CFBJLEBOFHJ v, float mult, float seconds)
    {
        if (!Running || v.JPHIMKLIAAO == null) return false;
        var cc = v.JPHIMKLIAAO.GetComponent<CarController>();
        if (cc == null) return false;
        Slows[cc] = (mult, Time.unscaledTime + seconds);
        return true;
    }

    public static void ApplySlow(CarController cc)
    {
        if (cc == null || !Slows.TryGetValue(cc, out var s)) return;
        if (Time.unscaledTime < s.until) cc.DAOEOLBGMGM *= s.mult;
        else Slows.Remove(cc);
    }

    // Optional per-login picture (URL or local path) shown in the lobby/results lists instead of the Twitch avatar.
    public static readonly Dictionary<string, string> Images = new();
    static readonly Dictionary<string, byte[]> ImageCache = new();

    // RawImage -> custom source, registered before the game's own avatar loader runs so a prefix can skip it.
    static readonly Dictionary<RawImage, string> RowImages = new();

    public static void RegisterRowImage(CKINOOFAKJL who, RawImage target)
    {
        if (who == null || target == null) return;
        if (Images.TryGetValue(who.JLDPKDLFPJP ?? "", out var src)) RowImages[target] = src; else RowImages.Remove(target);
    }

    public static bool HasCustomImage(RawImage target) => target != null && RowImages.ContainsKey(target);

    public static void ApplyImage(CKINOOFAKJL who, RawImage target, MonoBehaviour host)
    {
        if (target == null || !RowImages.TryGetValue(target, out var src)) return;
        host.StartCoroutine(LoadImage(src, target));
    }

    static IEnumerator LoadImage(string src, RawImage target)
    {
        if (!ImageCache.TryGetValue(src, out var bytes))
        {
            byte[] result = null; var done = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (File.Exists(src)) result = File.ReadAllBytes(src);
                    else { var wc = new WebClient(); wc.Headers["User-Agent"] = "StreamRacerApi"; result = wc.DownloadData(src); }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"image load failed for {src}: {e.Message}"); }
                done.Set();
            });
            while (!done.IsSet) yield return null;
            if (result == null) yield break;
            ImageCache[src] = bytes = result;
        }
        if (target == null) yield break;
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(bytes)) { Plugin.Log.LogWarning($"image decode failed for {src}"); yield break; }
        target.texture = tex;
        target.enabled = true;
    }

    static readonly string[] Palette = { "#ff3b30", "#ffd400", "#35e0ff", "#b07cff", "#3ddc84", "#ff8c42", "#ff5fa2", "#7ae7ff", "#c8ff4d", "#ff7a7a" };
    public static Color AutoColor(string login)
    {
        int h = 0; foreach (char c in login ?? "") h = h * 31 + c;
        ColorUtility.TryParseHtmlString(Palette[System.Math.Abs(h) % Palette.Length], out var c2); return c2;
    }
    public static bool Join(string id, string login, string displayName, string colorHex, bool sub, string image = null)
    {
        if (!string.IsNullOrWhiteSpace(image)) Images[login] = image; else Images.Remove(login);
        if (Running || Ended || VehicleManager.NEPFAEJAMGI == null) return false;
        if (VehicleManager.NEPFAEJAMGI.HasPlayerAlreadyJoined(login)) return false;
        if (!ColorUtility.TryParseHtmlString(colorHex ?? "", out var color)) color = AutoColor(login);
        VehicleManager.NEPFAEJAMGI.AddVehicle(new CFBJLEBOFHJ(new CKINOOFAKJL
        {
            DNJFGHLIAIM = id,
            JLDPKDLFPJP = login,
            AMCIKHEHBGM = string.IsNullOrEmpty(displayName) ? login : displayName,
            EKPDDGFGLNI = color,
            HIMCIABAFNG = sub,
            POKHEANEDLK = new GLBFHBCFHDF(),
        }));
        return VehicleManager.NEPFAEJAMGI.HasPlayerAlreadyJoined(login);
    }

    // Lobby only: kicking mid-race can end the race. Mirrors the row's own kick button (remove + destroy row).
    // What the lobby's JOIN GAME button does: adds the logged-in streamer with the STREAMER title.
    public static bool JoinStreamer(string colorHex = null)
    {
        if (Running || InGameScreenController.NEPFAEJAMGI == null || StreamerLogin == null) return false;
        if (VehicleManager.NEPFAEJAMGI.HasPlayerAlreadyJoined(StreamerLogin)) return false;
        InGameScreenController.NEPFAEJAMGI.JoinGameClicked();
        var me = Find(StreamerLogin);
        if (me != null && ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(colorHex) ? Settings.Current.streamerColor ?? "" : colorHex, out var c)) me.JDDOIMHIFHK.EKPDDGFGLNI = c;
        return me != null;
    }

    // The real finish: the FinishLine trigger object. Position, the route direction through it, and its route distance.
    static Vector3 _flPos, _flDir; static float _flDist = -1f; static object _flCircuit;
    public static bool FinishLine(out Vector3 pos, out Vector3 dir, out float dist)
    {
        var circuit = WaypointController.NEPFAEJAMGI?.GetCircuit();
        if (circuit == null) { pos = dir = Vector3.zero; dist = -1f; return false; }
        if (!ReferenceEquals(_flCircuit, circuit) || _flDist < 0)
        {
            GameObject go = null;
            try { go = GameObject.FindGameObjectsWithTag("FinishLine").FirstOrDefault(); } catch { }
            if (go == null) { pos = dir = Vector3.zero; dist = -1f; return false; }
            // nearest point along the route to the trigger -> that's the finish distance and heading
            float best = float.MaxValue, bestD = 0f; float length = circuit.BDFOFBNGLBA > 0 ? circuit.BDFOFBNGLBA : 5000f;
            for (float d = 0; d < length; d += 3f)
            {
                float e = Vector3.Distance(circuit.GetRoutePoint(d).PFHEOLIHHHD, go.transform.position);
                if (e < best) { best = e; bestD = d; }
            }
            var rp = circuit.GetRoutePoint(bestD);
            _flPos = go.transform.position; _flDir = rp.AHLOFCFINIO; _flDir.y = 0; _flDir.Normalize(); _flDist = bestD; _flCircuit = circuit;
        }
        pos = _flPos; dir = _flDir; dist = _flDist; return true;
    }

    // Streamer says "that car finished": run the car's own finish-line trigger handler, exactly what touching
    // the FinishLine collider does. For when a car crosses the line without the game noticing.
    static readonly System.Reflection.MethodInfo FinishTrigger = AccessTools.Method(typeof(Car), "AEHEKHFIAEK");
    public static bool MarkFinished(CFBJLEBOFHJ v)
    {
        if (!Running || v.NIKOEDJIAFB || v.JPHIMKLIAAO == null) return false;
        var car = v.JPHIMKLIAAO.GetComponent<Car>();
        if (car == null || FinishTrigger == null) return false;
        float at = FinishAt(v);
        if (at > 0 && v.MNHJMCLOGPB <= at) v.MNHJMCLOGPB = at + 1f; // the handler refuses cars the game thinks are too early
        FinishTrigger.Invoke(car, null);
        return v.NIKOEDJIAFB;
    }

    // ---- viewer tiers & perks ----
    public static bool IsSub(CFBJLEBOFHJ v) => v.JDDOIMHIFHK.HIMCIABAFNG;
    public static bool IsDev(CFBJLEBOFHJ v) => (Title(v) ?? "").IndexOf("developer", System.StringComparison.OrdinalIgnoreCase) >= 0;
    public static bool IsHost(CFBJLEBOFHJ v) => Login(v) != null && Login(v) == StreamerLogin;

    // Follower checks need a token with moderator:read:followers (the game's own token lacks it). Cached per login.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _followers = new();
    static readonly HashSet<string> _followPending = new();
    public static bool FollowerKnown(string login) => _followers.ContainsKey(login ?? "");
    public static bool IsFollower(string login) => login != null && _followers.TryGetValue(login, out var f) && f;
    public static bool FollowerChecksAvailable => !string.IsNullOrEmpty(Settings.Current.twitchToken) && !string.IsNullOrEmpty(Settings.Current.twitchClientId) && !string.IsNullOrEmpty(StreamerId);

    public static void CheckFollower(string login, string userId, System.Action<bool> then = null)
    {
        if (login == null || !FollowerChecksAvailable) { then?.Invoke(false); return; }
        if (_followers.TryGetValue(login, out var known)) { then?.Invoke(known); return; }
        lock (_followPending) { if (!_followPending.Add(login)) return; }
        string token = Settings.Current.twitchToken, cid = Settings.Current.twitchClientId, bid = StreamerId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool result = false;
            try
            {
                var wc = new WebClient(); wc.Headers["Authorization"] = "Bearer " + token; wc.Headers["Client-Id"] = cid;
                string uid = userId;
                if (string.IsNullOrEmpty(uid))
                {
                    var u = Newtonsoft.Json.Linq.JObject.Parse(wc.DownloadString("https://api.twitch.tv/helix/users?login=" + System.Uri.EscapeDataString(login)))["data"];
                    uid = u != null && u.HasValues ? (string)u[0]["id"] : null;
                }
                if (!string.IsNullOrEmpty(uid))
                {
                    var d = Newtonsoft.Json.Linq.JObject.Parse(wc.DownloadString($"https://api.twitch.tv/helix/channels/followers?broadcaster_id={bid}&user_id={uid}"))["data"];
                    result = d != null && d.HasValues;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("follower check failed for " + login + ": " + e.Message); }
            _followers[login] = result;
            lock (_followPending) _followPending.Remove(login);
            Plugin.RunOnMain(() => then?.Invoke(result));
        });
    }

    static bool TierAllows(string tier, CFBJLEBOFHJ v, string login)
    {
        switch ((tier ?? "everyone").ToLowerInvariant())
        {
            case "off": return false;
            case "everyone": case "free": return true;
            case "follower": return (v != null && (IsSub(v) || IsDev(v) || IsHost(v))) || IsFollower(login);
            case "subscriber": case "sub": return v != null && (IsSub(v) || IsDev(v) || IsHost(v));
        }
        return true;
    }
    public static bool MayUseColorCommand(string login) => TierAllows(Settings.Current.perks.colorCommand, Find(login), login);
    public static bool MayShowColoredName(CFBJLEBOFHJ v) => TierAllows(Settings.Current.perks.coloredNames, v, Login(v));

    // Extra boosts on join: follower + subscriber + developer + host, stacking. Runs once the backend title is in.
    public static readonly HashSet<CFBJLEBOFHJ> _perked = new();
    public static void GrantPerks(CFBJLEBOFHJ v)
    {
        if (v == null || _perked.Contains(v)) return;
        _perked.Add(v);
        var p = Settings.Current.perks; int extra = 0; var why = new List<string>();
        if (IsSub(v) && p.boostSubscriber != 0) { extra += p.boostSubscriber; why.Add("sub"); }
        if (IsDev(v) && p.boostDeveloper != 0) { extra += p.boostDeveloper; why.Add("dev"); }
        if (IsHost(v) && p.boostHost != 0) { extra += p.boostHost; why.Add("host"); }
        void Apply()
        {
            if (IsFollower(Login(v)) && p.boostFollower != 0) { extra += p.boostFollower; why.Add("follower"); }
            if (extra != 0 && Vehicles().Contains(v)) { AddBoosts(v, extra); Plugin.Emit("perk", new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, extraBoosts = extra, reasons = why, boosts = Boosts(v) }); }
        }
        if (p.boostFollower != 0 && FollowerChecksAvailable && !FollowerKnown(Login(v))) CheckFollower(Login(v), v.JDDOIMHIFHK.DNJFGHLIAIM, _ => Apply());
        else Apply();
    }

    // ---- colors ----
    static readonly Dictionary<string, string> NamedColors = new()
    {
        ["pink"] = "#ff5fa2", ["hotpink"] = "#ff69b4", ["gold"] = "#ffd400", ["lime"] = "#3ddc84", ["sky"] = "#35e0ff", ["violet"] = "#b07cff",
        ["orange"] = "#ff8a00", ["turquoise"] = "#40e0d0", ["salmon"] = "#fa8072", ["coral"] = "#ff7f50", ["indigo"] = "#4b0082", ["mint"] = "#98ff98",
    };
    public static bool ParseColor(string text, out Color c)
    {
        c = Color.white; if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().ToLowerInvariant();
        if (NamedColors.TryGetValue(t, out var hex)) t = hex;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, "^[0-9a-f]{6}$")) t = "#" + t;
        return ColorUtility.TryParseHtmlString(t, out c);
    }

    // Set a racer's color now (car label, leaderboard, overlays) and remember it for future joins.
    public static bool SetColor(string login, string text, bool persist = true)
    {
        if (login == null || !ParseColor(text, out var c)) return false;
        login = login.ToLowerInvariant();
        if (persist) { Settings.Current.colors[login] = "#" + ColorUtility.ToHtmlStringRGB(c); Settings.Persist(); }
        var v = Find(login);
        if (v != null) { v.JDDOIMHIFHK.EKPDDGFGLNI = c; RecolorLabel(v, c); }
        return true;
    }

    static void RecolorLabel(CFBJLEBOFHJ v, Color c)
    {
        var lbl = v.JPHIMKLIAAO?.GetComponent<CarLabel>();
        if (lbl == null) return;
        // the visible label is a separate object the game spawns (CarLabel.ODKBABJEGKK) carrying a VehicleLabel
        var go = AccessTools.Field(typeof(CarLabel), "ODKBABJEGKK")?.GetValue(lbl) as GameObject;
        var vl = go?.GetComponent<VehicleLabel>();
        if (vl?.BBLPLCOPBPP != null) vl.BBLPLCOPBPP.color = c;
        if (go != null) foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) t.color = c;
    }

    // Apply a remembered color when someone joins (runs from the AddVehicle postfix).
    public static void ApplySavedColor(CFBJLEBOFHJ v)
    {
        if (Settings.Current.colors.TryGetValue(Login(v) ?? "", out var hex) && ParseColor(hex, out var c)) v.JDDOIMHIFHK.EKPDDGFGLNI = c;
    }

    // In-game leaderboard: paint each row's name in the car's color (or back to white when turned off).
    static readonly System.Reflection.FieldInfo LbVehicle = AccessTools.Field(typeof(VehicleLeaderboardUIItem), "LOHGEJIOPJL");
    static VehicleLeaderboardUIItem[] _lbItems = new VehicleLeaderboardUIItem[0]; static float _lbScanned = -10f, _lbNext;
    public static void ColorLeaderboard()
    {
        if (Time.unscaledTime < _lbNext) return; _lbNext = Time.unscaledTime + 0.25f;
        if (!Running) return;
        if (Time.unscaledTime - _lbScanned > 2f) { _lbItems = Object.FindObjectsOfType<VehicleLeaderboardUIItem>(); _lbScanned = Time.unscaledTime; }
        bool on = Settings.Current.colorLeaderboard;
        foreach (var it in _lbItems)
        {
            if (it == null || it.AKAMDOKJAIE == null) continue;
            var v = LbVehicle?.GetValue(it) as CFBJLEBOFHJ;
            it.AKAMDOKJAIE.color = on && v != null && MayShowColoredName(v) ? v.JDDOIMHIFHK.EKPDDGFGLNI : Color.white;
        }
    }

    // Chat: "!color <value>" from a viewer sets their own color.
    public static void OnChatMessage(string login, string message)
    {
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(message)) return;
        string rc = (Settings.Current.respawnCommand ?? "").Trim();
        if (Settings.Current.respawnCommandEnabled && rc.Length > 0 && message.Trim().Equals(rc, System.StringComparison.OrdinalIgnoreCase))
        {
            var mine = Find(login); if (mine != null && Respawn(mine)) Plugin.Emit("respawn", EventDto(mine));
            return;
        }
        if (!Settings.Current.colorCommandEnabled) return;
        string cmd = (Settings.Current.colorCommand ?? "!color").Trim();
        if (cmd.Length == 0 || !message.StartsWith(cmd, System.StringComparison.OrdinalIgnoreCase)) return;
        string arg = message.Substring(cmd.Length).Trim();
        if (arg.Length == 0) return;
        var tier = Settings.Current.perks.colorCommand;
        if (tier == "follower" && FollowerChecksAvailable && !FollowerKnown(login) && Find(login) == null)
        {   // unknown follower status: look it up, then retry once
            CheckFollower(login, null, _ => { if (MayUseColorCommand(login) && SetColor(login, arg)) Plugin.Emit("color", new { login, color = Settings.Current.colors[login.ToLowerInvariant()] }); });
            return;
        }
        if (!MayUseColorCommand(login)) { Plugin.Emit("denied", new { login, command = cmd, tier }); return; }
        if (SetColor(login, arg)) Plugin.Emit("color", new { login, color = Settings.Current.colors[login.ToLowerInvariant()] });
    }

    public static bool Kick(CFBJLEBOFHJ v)
    {
        if (Running || !VehicleManager.NEPFAEJAMGI.RemoveVehicleByTwitchUsername(Login(v))) return false;
        foreach (var row in Object.FindObjectsOfType<PreGamePlayerListItem>())
            if (row.JDDOIMHIFHK?.JLDPKDLFPJP == Login(v)) Object.Destroy(row.gameObject);
        return true;
    }

    public static void ResetBoomTracking() { BoomedUntil.Clear(); Slows.Clear(); }

    // Lobby creation = what the map list's green play button does: put a playlist into the game's
    // static queue (first = chosen map) and call its "next map in queue", which builds the lobby
    // from the Play-tab settings (max cars, type, time) and loads the Play scene. Works from any screen.
    static readonly System.Reflection.FieldInfo QueueList = AccessTools.Field(typeof(LHAGKAJCCIB), "DCKCLNFBGFM");
    static readonly System.Reflection.FieldInfo QueueIndex = AccessTools.Field(typeof(LHAGKAJCCIB), "MCMLCNHMDPJ");

    public static List<MapOverviewListItem> Maps = new();
    static bool _mapsLoading;

    public static void FetchMaps(System.Action<List<MapOverviewListItem>> then = null)
    {
        if (_mapsLoading) return;
        _mapsLoading = true;
        KBCFIIFFLFL.JIMBAOHPFOG.LEMLINILCDG(resp =>
        {
            _mapsLoading = false;
            Maps = resp?.Maps?.ToList() ?? new List<MapOverviewListItem>();
            then?.Invoke(Maps);
        });
    }

    public static object MapDto(MapOverviewListItem m) => new { id = m.ID, name = m.Name, creator = m.Creator, official = m.Official, length = m.Length, avgTime = m.Time };

    public static string CreateLobby(string mapQuery)
    {
        if (Running || InLobby) return null;
        FetchMaps(maps =>
        {
            if (maps.Count == 0) { Plugin.Log.LogWarning("/lobby: map list empty"); return; }
            var q = (mapQuery ?? "").Trim().ToLowerInvariant();
            var first = maps.FirstOrDefault(m => q != "" && (m.ID.ToString() == q || (m.Name ?? "").ToLowerInvariant().Contains(q))) ?? maps[0];
            var queue = new List<MapOverviewListItem> { first };
            queue.AddRange(maps.Where(m => m != first));
            QueueList.SetValue(null, queue);
            QueueIndex.SetValue(null, 0);
            LHAGKAJCCIB.JKMCAIDPAAB();
        });
        return "opening";
    }

    // Where the game is right now: home | play | settings | lobby | racing | postgame | trackbuilder | <panel/scene name>
    public static object ScreenState()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string screen;
        if (Running) screen = "racing";
        else if (Ended && GameController.NEPFAEJAMGI != null && GameController.NEPFAEJAMGI.BFOGGNCDONA != null) screen = "postgame";
        else if (InGameScreenController.NEPFAEJAMGI != null && InLobby)
        {
            var st = InGameScreenController.NEPFAEJAMGI.GetCurrentGameScreenState().ToString().ToLowerInvariant();
            screen = st.Contains("post") ? "postgame" : "lobby";
        }
        else if (InGameScreenController.NEPFAEJAMGI != null) screen = InGameScreenController.NEPFAEJAMGI.GetCurrentGameScreenState().ToString().ToLowerInvariant();
        else
        {
            var tabs = Object.FindObjectOfType<Michsky.UI.Zone.MainPanelManager>();
            if (tabs != null && tabs.panels.Count > 0)
            {
                int i = Mathf.Clamp(Traverse.Create(tabs).Field("currentPanelIndex").GetValue<int>(), 0, tabs.panels.Count - 1);
                screen = tabs.panels[i].name.ToLowerInvariant().Replace(" panel", "").Replace("panel", "").Trim();
            }
            else screen = scene.ToLowerInvariant().Contains("builder") ? "trackbuilder" : scene.ToLowerInvariant();
        }
        return new { screen, scene, running = Running, lobby = InLobby, vehicles = Vehicles().Count };
    }

    // True from the end of a race until the next lobby (NewGame). The old cars are still in the scene then: joining or
    // starting would double the field, so those routes refuse until POST /race/next (or the Play tab) loads a lobby.
    public static bool Ended;
    public static bool InLobby => !Running && !Ended && GameController.NEPFAEJAMGI != null && GameController.NEPFAEJAMGI.BFOGGNCDONA != null && VehicleManager.NEPFAEJAMGI != null;

    // Default: what the lobby's START button does — kick off the lobby countdown (Settings → start countdown, 60 s),
    // the race starts when it hits zero. now = true skips the countdown and starts immediately.
    public static bool StartRace(bool now = false)
    {
        if (!InLobby || Vehicles().Count == 0) return false;
        var screen = InGameScreenController.NEPFAEJAMGI ?? Object.FindObjectOfType<InGameScreenController>();
        if (!now && screen != null) { screen.PCLCKCHAICB(); return true; }
        screen?.InGameSetup();
        GameController.NEPFAEJAMGI.StartCurrentGame();
        return true;
    }

    public static void EndRace() { if (Running) Ended = true; GameController.NEPFAEJAMGI.ForceEndGame(); }
    // Next map from the queue set by /lobby (or the Play tab). Works from the post-game screen or anywhere idle.
    public static bool NextRace()
    {
        if (Running || !LHAGKAJCCIB.BNKMAHINMGP()) return false;
        LHAGKAJCCIB.JKMCAIDPAAB();
        return true;
    }
}
