using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StreamRacerApi;

static class Routes
{
    // POST /boom            random x1
    // POST /boom/:n         random xN (n <= 200)
    // POST /boom/:id|login  targeted
    // POST /boom/all?except=id|login
    // POST /boost/:x|all?force=&seconds=      fire a boost now
    // POST /boost/:x|all/add?n=1              add to the !boost pool
    // POST /speed/:x|all?mult=0.5&seconds=5   top-speed multiplier
    // POST /respawn/:x|all
    // POST /join   {id,login,displayName,color,sub} or [..]
    // POST /kick/:x
    // POST /race/end   POST /race/next   GET /race
    public static object Dispatch(MonoBehaviour host, string method, string[] p, NameValueCollection q, string body, out int status)
    {
        status = 200;
        string root = p.Length > 0 ? p[0] : "";
        string x = p.Length > 1 ? p[1] : null;
        string sub = p.Length > 2 ? p[2] : null;

        if (method == "GET" && root == "race") return Game.Snapshot();
        if (method == "GET" && root == "maps")
        {
            if (Game.Maps.Count == 0) { Game.FetchMaps(); status = 202; }
            return new { loading = Game.Maps.Count == 0, maps = Game.Maps.Select(Game.MapDto).ToList() };
        }
        if (method == "GET" && root == "camera") return x == "pose" ? Cam.Pose() : Cam.State;
        if (method == "GET" && root == "screen") return Game.ScreenState();
        if (method == "GET" && root == "track") return x == "zones" ? Game.ZonesDto() : Game.TrackDto(q["raw"] == "1");
        if (method == "GET" && root == "find") // dev aid: GameObjects whose name contains q, with path + components + text
        {
            string needle = (q["q"] ?? "").ToLowerInvariant(); int max = int.TryParse(q["max"], out var mx) ? mx : 80;
            var list = new List<object>();
            foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.gameObject.scene.name == null) continue; // prefabs/assets
                string path = t.name; for (var par = t.parent; par != null; par = par.parent) path = par.name + "/" + path;
                if (needle.Length > 0 && !path.ToLowerInvariant().Contains(needle)) continue;
                var tmp = t.GetComponent<TMPro.TMP_Text>();
                var rt = t as RectTransform;
                list.Add(new { path, active = t.gameObject.activeInHierarchy, comps = t.GetComponents<Component>().Select(c => c?.GetType().Name).ToArray(), text = tmp?.text?.Substring(0, System.Math.Min(80, tmp.text.Length)), children = t.childCount,
                    rect = rt == null ? null : new { w = rt.rect.width, h = rt.rect.height, y = rt.anchoredPosition.y, pivotY = rt.pivot.y, anchorMinY = rt.anchorMin.y, anchorMaxY = rt.anchorMax.y }, pref = tmp?.preferredHeight });
                if (list.Count >= max) break;
            }
            return list;
        }
        if (method == "POST" && root == "find" && x == "click") // dev aid: press the Button at ?path=
        {
            var go = GameObject.Find(q["path"] ?? ""); var btn = go?.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) { status = 404; return new { error = "no button at path" }; }
            btn.onClick.Invoke(); return Ok(1);
        }
        if (method == "POST" && root == "find" && x == "scroll") // dev aid: ScrollRect at ?path= to ?pos= (1 top, 0 bottom)
        {
            var sr = GameObject.Find(q["path"] ?? "")?.GetComponent<UnityEngine.UI.ScrollRect>();
            if (sr == null) { status = 404; return new { error = "no ScrollRect at path" }; }
            sr.verticalNormalizedPosition = float.TryParse(q["pos"], out var ps) ? ps : 0f; return Ok(1);
        }
        if (method == "POST" && root == "find" && x == "panel") // dev aid: open a main menu panel by name (Michsky panel manager)
        {
            var pm = UnityEngine.Object.FindObjectOfType<Michsky.UI.Zone.MainPanelManager>();
            if (pm == null) { status = 404; return new { error = "no panel manager (menu only)" }; }
            int idx = pm.panels.FindIndex(pn => pn.name.Equals(q["name"] ?? "Settings", System.StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { status = 404; return new { error = "no such panel", panels = pm.panels.Select(pn => pn.name).ToList() }; }
            pm.PanelAnim(idx); return new { ok = true, panel = pm.panels[idx].name };
        }
        if (method == "GET" && root == "version")
        {
            Updates.Kick();
            return new { api = Plugin.Version, commit = Plugin.Commit, game = UnityEngine.Application.version, unity = UnityEngine.Application.unityVersion, bepinex = typeof(BepInEx.Paths).Assembly.GetName().Version.ToString(3),
                         latest = Updates.Latest, upToDate = Updates.UpToDate, updateUrl = Updates.Url, developer = "Shibiko", twitch = "https://twitch.tv/ShibikoX" };
        }
        if (method == "GET" && root == "me") return new { id = Game.StreamerId, login = Game.StreamerLogin, inRace = Game.StreamerVehicle() != null };
        if (method == "GET" && root == "settings") return Settings.WithConfig();
        if (method == "PUT" && root == "config")
        {
            var res = Settings.ApplyConfig(JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body), out bool restart);
            Plugin.Emit("settings", Settings.WithConfig());
            if (restart) host.StartCoroutine(Plugin.Instance.RestartHttp());
            return res;
        }
        if (method == "PUT" && root == "settings") { Settings.Save(body); Plugin.Emit("settings", Settings.WithConfig()); return Settings.WithConfig(); }
        if (method != "POST") { status = 404; return new { error = "not found" }; }

        switch (root)
        {
            case "boom":
                if (!Game.Running) { status = 409; return new { error = "no race running" }; }
                if (x == null) return Ok(Game.BoomRandom() ? 1 : 0);
                if (x == "all")
                {
                    string except = q["except"];
                    int n = Game.Vehicles().Where(v => except == null || (v.JDDOIMHIFHK.DNJFGHLIAIM != except && Game.Login(v) != except)).Count(Game.Boom);
                    return Ok(n);
                }
                if (int.TryParse(x, out int count) && count <= 200)
                {
                    int hit = 0;
                    for (int i = 0; i < Mathf.Clamp(count, 1, 200) && Game.BoomRandom(); i++) hit++;
                    return Ok(hit);
                }
                return One(x, Game.Boom, ref status);

            case "boost":
            {
                if (x == null) { status = 400; return new { error = "need /boost/:target" }; }
                if (x == "me")
                {
                    var me = Game.StreamerVehicle();
                    if (me == null) { status = 404; return new { error = "streamer not in race" }; }
                    if (!Game.UseBoost(me)) { status = 409; return new { error = "no boosts left or not driving" }; }
                    return Ok(1);
                }
                if (sub == "use")
                {
                    var t = Game.Find(x);
                    if (t == null) { status = 404; return new { error = "no vehicle", target = x }; }
                    if (!Game.UseBoost(t)) { status = 409; return new { error = "no boosts left or not driving", boosts = Game.Boosts(t) }; }
                    return new { ok = true, affected = 1, boosts = Game.Boosts(t) };
                }
                if (sub == "add")
                {
                    int n = int.TryParse(q["n"], out var v) ? v : 1;
                    return Targets(x, ref status, t => { Game.AddBoosts(t, n); return true; });
                }
                float? force = float.TryParse(q["force"], out var f) ? f : null;
                float? secs = float.TryParse(q["seconds"], out var s) ? s : null;
                return Targets(x, ref status, t => Game.Boost(t, force, secs));
            }

            case "speed":
            {
                if (x == null) { status = 400; return new { error = "need /speed/:target" }; }
                float mult = float.TryParse(q["mult"], out var m) ? m : 0.5f;
                float secs = float.TryParse(q["seconds"], out var s) ? s : 5f;
                return Targets(x, ref status, t => Game.Speed(t, mult, secs));
            }

            case "respawn":
                if (x == null) { status = 400; return new { error = "need /respawn/:target" }; }
                return Targets(x, ref status, Game.Respawn);

            case "join":
            {
                if (Game.Running || Game.Ended) { status = 409; return new { error = Game.Running ? "race running" : "race over: POST /race/next first" }; }
                if (x == "me") { if (!Game.JoinStreamer(q["color"])) { status = 409; return new { error = "no lobby, not logged in, or already joined" }; } return Ok(1); }
                var tok = JToken.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
                var items = tok is JArray a ? a.Children<JObject>().ToList() : new List<JObject> { (JObject)tok };
                foreach (var o in items) if (o["autoBoost"] != null && !string.IsNullOrWhiteSpace((string)o["login"]))
                    Settings.Current.botOptions[((string)o["login"]).ToLowerInvariant()] = new Settings.BotOpts { autoBoost = (bool)o["autoBoost"] };
                if (items.Any(o => o["autoBoost"] != null)) Settings.Persist();
                int n = items.Count(o => Game.Join(
                    (string)o["id"] ?? "", (string)o["login"] ?? "", (string)o["displayName"],
                    (string)o["color"], (bool?)o["sub"] ?? false, (string)o["image"]));
                return Ok(n);
            }

            case "color":
            {
                if (x == null) { status = 400; return new { error = "need /color/:x?color=" }; }
                string c = q["color"];
                if (x == "reset") { Settings.Current.colors.Clear(); Settings.Persist(); return Ok(0); }
                if (c == null) { Settings.Current.colors.Remove(x.ToLowerInvariant()); Settings.Persist(); return Ok(1); }
                if (!Game.SetColor(x, c)) { status = 400; return new { error = "bad color", color = c }; }
                Plugin.Emit("color", new { login = x.ToLowerInvariant(), color = Settings.Current.colors[x.ToLowerInvariant()] });
                return Ok(1);
            }

            case "finish":
                if (x == null) { status = 400; return new { error = "need /finish/:target" }; }
                return One(x, Game.MarkFinished, ref status);

            case "kick":
                if (x == null) { status = 400; return new { error = "need /kick/:target" }; }
                return One(x, Game.Kick, ref status);

            case "autojoin":
                if (x == "join") { if (Game.Running) { status = 409; return new { error = "race running" }; } return Ok(Settings.JoinAll()); }
                break;

            case "camera":
            {
                float secs = float.TryParse(q["seconds"], out var sv) ? sv : 8f;
                float fov1 = float.TryParse(q["fov"], out var f1) ? f1 : 0f;
                float fov2 = float.TryParse(q["fovTo"], out var f2) ? f2 : fov1;
                bool ok;
                switch (x)
                {
                    case "auto":
                        Cam.SetAuto(q["on"] == null ? !Cam.Auto : q["on"] == "1" || q["on"] == "true");
                        return Cam.State;
                    case "focus":
                    {
                        var t = sub == null ? null : Game.Find(sub);
                        if (t == null) { status = 404; return new { error = "no vehicle", target = sub }; }
                        ok = Cam.Shot(() => Cam.Follow(t), secs); break;
                    }
                    case "leader": ok = Cam.Shot(() => Cam.Follow(Cam.Leader()), secs); break;
                    case "wide":
                    {
                        var t = sub == null ? Cam.Leader() : Game.Find(sub);
                        if (t == null) { status = 404; return new { error = "no vehicle", target = sub }; }
                        ok = Cam.Shot(() => Cam.FollowWide(t), secs); break;
                    }
                    case "overhead": ok = Cam.Shot(() => Cam.Overhead(secs, fov1, fov2), secs); break;
                    case "boom": ok = Cam.LastBoomed != null && Cam.Shot(() => Cam.Orbit(Cam.LastBoomed, secs, fov1, fov2), secs); break;
                    case "pack": ok = Cam.Shot(() => Cam.Pack(secs, fov1, fov2), secs); break;
                    case "prop": ok = Cam.Shot(() => Cam.PropNearLeader(), secs); break;
                    case "sweep": ok = Cam.Shot(() => Cam.Sweep(secs, fov1, fov2), secs); break;
                    case "grid": ok = Cam.Shot(() => Cam.Grid(secs), secs); break;
                    case "side": ok = Cam.Shot(() => Cam.Side(secs, fov1, fov2), secs); break;
                    case "finish": ok = Cam.Shot(() => Cam.Finish(secs, fov1, fov2), secs); break;
                    case "high": ok = Cam.Shot(() => Cam.High(secs, fov1, fov2), secs); break;
                    case "chase":
                    case "front":
                    case "orbit":
                    {
                        var t = sub == null ? Cam.Leader() : Game.Find(sub);
                        if (t == null) { status = 404; return new { error = "no vehicle", target = sub }; }
                        ok = Cam.Shot(() => x == "chase" ? Cam.Chase(t, secs, fov1, fov2) : x == "front" ? Cam.Front(t, secs, fov1, fov2) : Cam.Orbit(t, secs, fov1, fov2), secs); break;
                    }
                    case "free": ok = Cam.Shot(Cam.Free, 0); break;
                    default: status = 404; return new { error = "not found" };
                }
                if (!ok) { status = 409; return new { error = "no race running (or nothing boomed yet)" }; }
                return Cam.State;
            }

            case "minimap":
            {
                if (q["on"] != null) Minimap.Current.enabled = q["on"] == "1" || q["on"] == "true"; else Minimap.Current.enabled = !Minimap.Current.enabled;
                Settings.Current.minimap = JObject.FromObject(Minimap.Current);
                Settings.Save(JsonConvert.SerializeObject(Settings.Current));
                Plugin.Emit("settings", Settings.WithConfig());
                return Minimap.State;
            }

            case "lobby":
            {
                if (x == "exit")
                {
                    if (!Game.InLobby) { status = 409; return new { error = "not in a lobby" }; }
                    var exit = GameObject.Find("PreGameCanvas/PreGameScreen/Content/Box 1/Game Settings Overview/Buttons/Exit")?.GetComponent<UnityEngine.UI.Button>();
                    if (exit == null) { status = 500; return new { error = "exit button not found" }; }
                    exit.onClick.Invoke(); return Ok(1);
                }
                var r = Game.CreateLobby(q["map"]);
                if (r == null) { status = 409; return new { error = Game.Running ? "race running" : Game.InLobby ? "already in a lobby" : "not on the home screen" }; }
                if (r == "opening") status = 202;
                return new { ok = true, state = r };
            }

            case "race":
                if (x == "start") { if (!Game.StartRace(q["now"] == "1" || q["now"] == "true")) { status = 409; return new { error = Game.Ended ? "race over: POST /race/next first" : "no lobby with cars" }; } return Ok(1); }
                if (x == "end") { Game.EndRace(); return Ok(1); }
                if (x == "next") { if (!Game.NextRace()) { status = 409; return new { error = Game.Running ? "race running" : "no maps queued, call /lobby" }; } return Ok(1); }
                break;
        }
        status = 404; return new { error = "not found" };
    }

    static object Ok(int affected) => new { ok = true, affected };

    static object One(string x, System.Func<CFBJLEBOFHJ, bool> act, ref int status)
    {
        var v = Game.Find(x);
        if (v == null) { status = 404; return new { error = "no vehicle", target = x }; }
        if (!act(v)) { status = 409; return new { error = "not applicable", target = x }; }
        return Ok(1);
    }

    static object Targets(string x, ref int status, System.Func<CFBJLEBOFHJ, bool> act)
    {
        if (x != "all") return One(x, act, ref status);
        return Ok(Game.Vehicles().Count(act));
    }
}
