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
        var zones = Pure.Zones(d => { var rp = circuit.GetRoutePoint(d); return (rp.PFHEOLIHHHD, rp.AHLOFCFINIO); }, length);
        _zones = zones; _zonesKey = key;
        return zones;
    }
    public static bool InBoostZone(float progress) => Pure.InZone(BoostZones(), progress);
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
        respawns = RespawnsLeft(Login(v)),
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
    public static void AddBoosts(CFBJLEBOFHJ v, int n)
    {
        BoostsField.SetValue(v, Boosts(v) + n);
        EmitBoosts(v, n);
    }
    // `boosts` = a pool changed without a boost being fired (add, perk, race start). Pool spends are the `boost` event (pool patch).
    public static void EmitBoosts(CFBJLEBOFHJ v, int delta = 0) =>
        Plugin.Emit("boosts", new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, boosts = Boosts(v), delta });
    public static void EmitBoostPools() { foreach (var v in Vehicles()) EmitBoosts(v); }

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

    public static Color AutoColor(string login) { ColorUtility.TryParseHtmlString(Pure.AutoColorHex(login), out var c); return c; }
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

    // Follower checks need a token with moderator:read:followers (the game's own token lacks it): Settings -> Perks, pasted
    // into settings.twitchToken + twitchClientId. Without one, followers are simply never detected (status "no token").
    // Successful lookups are cached per login for the session; failures are not (a 401 must not brand someone a non-follower).
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _followers = new();
    static readonly Dictionary<string, List<System.Action<bool>>> _followPending = new(); // login -> callbacks waiting on one lookup
    public static string FollowerCheckError; // last Helix failure, shown in /settings and /perks/:login
    public static bool FollowerKnown(string login) => _followers.ContainsKey((login ?? "").ToLowerInvariant());
    public static bool IsFollower(string login) => login != null && _followers.TryGetValue(login.ToLowerInvariant(), out var f) && f;
    public static bool FollowerChecksAvailable => !string.IsNullOrEmpty(Settings.Current.twitchToken) && !string.IsNullOrEmpty(Settings.Current.twitchClientId) && !string.IsNullOrEmpty(StreamerId);
    public static string FollowerChecks => Pure.FollowerCheckStatus(!string.IsNullOrEmpty(Settings.Current.twitchToken), !string.IsNullOrEmpty(Settings.Current.twitchClientId), !string.IsNullOrEmpty(StreamerId), FollowerCheckError);
    public static void ForgetFollowers() { _followers.Clear(); FollowerCheckError = null; }

    public static void CheckFollower(string login, string userId, System.Action<bool> then = null)
    {
        if (login == null || !FollowerChecksAvailable) { then?.Invoke(false); return; }
        login = login.ToLowerInvariant();
        if (_followers.TryGetValue(login, out var known)) { then?.Invoke(known); return; }
        lock (_followPending)
        {
            if (_followPending.TryGetValue(login, out var waiting)) { if (then != null) waiting.Add(then); return; } // one lookup, every caller told
            _followPending[login] = then != null ? new List<System.Action<bool>> { then } : new List<System.Action<bool>>();
        }
        string token = Settings.Current.twitchToken, cid = Settings.Current.twitchClientId, bid = StreamerId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool result = false, ok = false;
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
                ok = true; FollowerCheckError = null;
            }
            catch (WebException e)
            {
                var code = (e.Response as HttpWebResponse)?.StatusCode;
                FollowerCheckError = code == HttpStatusCode.Unauthorized ? "401: token rejected (needs moderator:read:followers for this channel; the client id must be the token's)" : code != null ? (int)code + ": " + e.Message : e.Message;
                Plugin.Log.LogWarning("follower check failed for " + login + ": " + FollowerCheckError);
            }
            catch (System.Exception e) { FollowerCheckError = e.Message; Plugin.Log.LogWarning("follower check failed for " + login + ": " + e.Message); }
            if (ok) _followers[login] = result;
            List<System.Action<bool>> callbacks;
            lock (_followPending) { _followPending.TryGetValue(login, out callbacks); _followPending.Remove(login); }
            Plugin.RunOnMain(() => { if (callbacks != null) foreach (var cb in callbacks) cb(result); });
        });
    }

    static bool TierAllows(string tier, CFBJLEBOFHJ v, string login) =>
        Pure.TierAllows(tier, IsFollower(login), v != null && IsSub(v), v != null && IsDev(v), v != null && IsHost(v));
    public static bool MayUseColorCommand(string login) => TierAllows(Settings.Current.perks.colorCommand, Find(login), login);
    public static bool MayShowColoredName(CFBJLEBOFHJ v) => TierAllows(Settings.Current.perks.coloredNames, v, Login(v));

    // Extra boosts on join: follower + subscriber + developer + host, stacking (Pure.ExtraBoosts). Runs once the backend
    // title is in. Sub/dev/host are known at once; the follower part waits for the Helix lookup, then the whole grant applies.
    public static readonly HashSet<CFBJLEBOFHJ> _perked = new();
    public static void GrantPerks(CFBJLEBOFHJ v)
    {
        if (v == null || _perked.Contains(v)) return;
        _perked.Add(v);
        var p = Settings.Current.perks;
        void Apply()
        {
            if (!Vehicles().Contains(v)) return;
            var (extra, why) = Pure.ExtraBoosts(p.boostFollower, p.boostSubscriber, p.boostDeveloper, p.boostHost, IsFollower(Login(v)), IsSub(v), IsDev(v), IsHost(v));
            if (extra != 0) AddBoosts(v, extra);
            Plugin.Emit("perk", new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, extraBoosts = extra, reasons = why, boosts = Boosts(v), followerChecks = FollowerChecks, follower = IsFollower(Login(v)) });
        }
        if (p.boostFollower != 0 && FollowerChecksAvailable && !FollowerKnown(Login(v))) CheckFollower(Login(v), v.JDDOIMHIFHK.DNJFGHLIAIM, _ => Apply());
        else Apply();
    }

    // GET /perks/:login: what someone gets and why (or why not). Unknown follower status kicks off a lookup; ask again.
    public static object PerksDto(string login)
    {
        login = (login ?? "").ToLowerInvariant();
        var v = Find(login); var p = Settings.Current.perks;
        bool sub = v != null && IsSub(v), dev = v != null && IsDev(v), host = login == (StreamerLogin ?? "").ToLowerInvariant();
        if (p.boostFollower != 0 && FollowerChecksAvailable && !FollowerKnown(login)) CheckFollower(login, v?.JDDOIMHIFHK.DNJFGHLIAIM);
        var (extra, why) = Pure.ExtraBoosts(p.boostFollower, p.boostSubscriber, p.boostDeveloper, p.boostHost, IsFollower(login), sub, dev, host);
        string status = FollowerChecks;
        if (p.boostFollower == 0) why.Add("follower boost is 0 in Settings -> Perks");
        else if (status == "no token") why.Add("follower boost needs a Twitch token with moderator:read:followers + its client id (Settings -> Perks)");
        else if (status == "unknown") why.Add("follower lookup failed: " + (FollowerCheckError ?? "streamer not logged in"));
        else if (!FollowerKnown(login)) why.Add("follower lookup pending: ask again in a second");
        else if (!IsFollower(login)) why.Add("not a follower");
        return new
        {
            login, inRace = v != null, follower = IsFollower(login), followerKnown = FollowerKnown(login), subscriber = sub, developer = dev, host,
            extraBoosts = extra, why, followerChecks = status, followerChecksError = FollowerCheckError,
            granted = v != null && _perked.Contains(v), boosts = v != null ? (int?)Boosts(v) : null, perks = p,
        };
    }

    // ---- chat replies ----
    // The game's TwitchLib client (TwitchCommandListener.LMGJJMLKMPA) is logged in as the streamer with the game token.
    // Sending needs chat:edit on that token; Twitch drops the message silently without it, so GET /chat reports the scopes.
    static readonly System.Reflection.FieldInfo ChatClientField = AccessTools.Field(typeof(TwitchCommandListener), "LMGJJMLKMPA");
    static TwitchLib.Client.Interfaces.ITwitchClient ChatClient => TwitchCommandListener.NEPFAEJAMGI == null ? null : ChatClientField?.GetValue(TwitchCommandListener.NEPFAEJAMGI) as TwitchLib.Client.Interfaces.ITwitchClient;
    public static string ChatChannel { get { try { var c = ChatClient; return c != null && c.JoinedChannels.Count > 0 ? c.JoinedChannels[0].Channel : StreamerLogin; } catch { return StreamerLogin; } } }
    public static bool ChatConnected { get { try { return ChatClient?.IsConnected == true; } catch { return false; } } }

    public static bool SayInChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TwitchAuth.SendChat(text)) { Plugin.Emit("chat", new { channel = StreamerLogin, text, via = "helix" }); return true; }
        try
        {
            var c = ChatClient; if (c == null || !c.IsConnected) return false;
            string ch = ChatChannel; if (string.IsNullOrEmpty(ch)) return false;
            c.SendMessage(ch, text.Length > 480 ? text.Substring(0, 480) : text);
            Plugin.Emit("chat", new { channel = ch, text });
            return true;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("chat send failed: " + e.Message); return false; }
    }
    static void Reply(string text) { if (Settings.Current.chatReplies) SayInChat(text); }

    // Scopes of the game's token (id.twitch.tv/oauth2/validate), refreshed hourly: chat:edit present = replies show up.
    static List<string> _scopes; static System.DateTime _scopesAt = System.DateTime.MinValue; static bool _scopesBusy; static string _scopesError;
    public static object ChatStatus()
    {
        string token = TwitchToken;
        if (!string.IsNullOrEmpty(token) && !_scopesBusy && (System.DateTime.UtcNow - _scopesAt).TotalSeconds > 3600)
        {
            _scopesBusy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var wc = new WebClient(); wc.Headers["Authorization"] = "OAuth " + token;
                    var o = Newtonsoft.Json.Linq.JObject.Parse(wc.DownloadString("https://id.twitch.tv/oauth2/validate"));
                    _scopes = o["scopes"]?.Select(s => (string)s).ToList() ?? new List<string>(); _scopesError = null;
                }
                catch (System.Exception e) { _scopesError = e.Message; }
                _scopesAt = System.DateTime.UtcNow; _scopesBusy = false; // no Unity API off the main thread
            });
        }
        bool? canSend = TwitchAuth.Has(TwitchAuth.ChatScope) ? true : _scopes == null ? (bool?)null : _scopes.Contains("chat:edit");
        return new { connected = ChatConnected, channel = ChatChannel, login = StreamerLogin, replies = Settings.Current.chatReplies, canSend, scopes = _scopes, scopesError = _scopesError,
                     helix = TwitchAuth.Has(TwitchAuth.ChatScope), twitch = TwitchAuth.Status(),
                     note = canSend == false ? "the game's token has no chat:edit: connect Twitch on the Settings page (user:write:chat) or relay the color/denied SSE events from your bot" : canSend == null ? "scopes not known yet: ask again" : null };
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
        if (!Running) foreach (var row in Object.FindObjectsOfType<PreGamePlayerListItem>()) // the lobby row is built once: repaint its name now
            if (row.JDDOIMHIFHK?.JLDPKDLFPJP == login) ColorRowName(row.JDDOIMHIFHK, row.AKAMDOKJAIE);
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

    // Chat: "!color <value>" from a viewer sets their own color; "!respawn" respawns their car. Aliases: settings.*Command ("a|b").
    public static void OnChatMessage(string login, string message, string displayName = null)
    {
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(message)) return;
        login = login.ToLowerInvariant(); displayName = string.IsNullOrEmpty(displayName) ? login : displayName;
        if (Settings.Current.respawnCommandEnabled && Pure.CommandArg(message, Settings.RespawnCommands) == "")
        {
            var mine = Find(login); if (mine == null) return;
            if (RespawnsLeft(login) == 0) { Plugin.Emit("denied", new { login, displayName, command = "respawn", reason = "no respawns left", respawns = 0 }); return; }
            if (Respawn(mine)) _respawnsUsed[login] = (_respawnsUsed.TryGetValue(login, out var used) ? used : 0) + 1; // Respawn emits the respawn event itself
            return;
        }
        if (!Settings.Current.colorCommandEnabled) return;
        var cmds = Settings.ColorCommands; string cmd = cmds.Count > 0 ? cmds[0] : "!race color";
        string arg = Pure.CommandArg(message, cmds);
        if (string.IsNullOrEmpty(arg)) return;
        var tier = Settings.Current.perks.colorCommand;
        void Done()
        {
            if (!MayUseColorCommand(login)) { Plugin.Emit("denied", new { login, displayName, command = cmd, tier, followerChecks = FollowerChecks }); Reply(Pure.ColorDeniedReply(displayName, tier)); return; }
            if (!SetColor(login, arg)) return;
            string hex = Settings.Current.colors[login];
            Plugin.Emit("color", new { login, displayName, color = hex }); Reply(Pure.ColorSetReply(displayName, hex));
        }
        if (tier == "follower" && FollowerChecksAvailable && !FollowerKnown(login) && Find(login) == null) CheckFollower(login, null, _ => Done()); // look it up, then decide
        else Done();
    }

    // Lobby / results row: the name in the car's color (same switch and tier rule as the leaderboard). Runs from the list-item postfix.
    public static void ColorRowName(CKINOOFAKJL who, TMPro.TextMeshProUGUI name)
    {
        if (who == null || name == null) return;
        var v = Find(who.JLDPKDLFPJP);
        bool on = Settings.Current.colorLeaderboard && Pure.TierAllows(Settings.Current.perks.coloredNames, IsFollower(who.JLDPKDLFPJP), who.HIMCIABAFNG || (v != null && IsSub(v)), v != null && IsDev(v), who.JLDPKDLFPJP != null && who.JLDPKDLFPJP == StreamerLogin);
        name.color = on ? who.EKPDDGFGLNI : Color.white;
    }

    public static bool Kick(CFBJLEBOFHJ v)
    {
        if (Running || !VehicleManager.NEPFAEJAMGI.RemoveVehicleByTwitchUsername(Login(v))) return false;
        foreach (var row in Object.FindObjectsOfType<PreGamePlayerListItem>())
            if (row.JDDOIMHIFHK?.JLDPKDLFPJP == Login(v)) Object.Destroy(row.gameObject);
        return true;
    }

    public static void ResetBoomTracking() { BoomedUntil.Clear(); Slows.Clear(); _respawnsUsed.Clear(); }

    // ---- respawn pool: chat respawns per racer per race; API respawns (the streamer) are free ----
    static readonly Dictionary<string, int> _respawnsUsed = new();
    public static int RespawnsLeft(string login) => Pure.RespawnsLeft(Settings.Current.respawnLimit, login != null && _respawnsUsed.TryGetValue(login.ToLowerInvariant(), out var used) ? used : 0);
    public static object InventoryDto(string login)
    {
        var v = Find(login); if (v == null) return null;
        return new { login = Login(v), displayName = v.JDDOIMHIFHK.AMCIKHEHBGM, boosts = Boosts(v), respawns = RespawnsLeft(Login(v)), respawnLimit = Settings.Current.respawnLimit, running = Running };
    }

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

    // EndCurrentGame is the path the game takes when the clock runs out (stops cars, tears down); ForceEndGame skips it and our race_end patch.
    public static void EndRace() { if (!Running) return; Ended = true; GameController.NEPFAEJAMGI.EndCurrentGame(); }
    // Next map from the queue set by /lobby (or the Play tab). Works from the post-game screen or anywhere idle.
    public static bool NextRace()
    {
        if (Running || !LHAGKAJCCIB.BNKMAHINMGP()) return false;
        LHAGKAJCCIB.JKMCAIDPAAB();
        return true;
    }
}
