// URL -> action. Plugin.Handle splits the path into segments and runs Dispatch on the game thread; every branch
// returns the object that becomes the JSON body and sets the status. Route reference: README "API".
using System.Collections.Specialized;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    public static object Dispatch(MonoBehaviour host, string method, string[] segments, NameValueCollection query, string body, out int status)
    {
        status = 200;
        string root = segments.Length > 0 ? segments[0] : "";
        string target = segments.Length > 1 ? segments[1] : null;   // :x, "all", "me", or a sub-route like "zones"
        string action = segments.Length > 2 ? segments[2] : null;   // third segment: /boost/:x/add, /camera/chase/:x

        if (method == "GET" && root == "race") return Game.Snapshot();
        if (method == "GET" && root == "maps")
        {
            if (Game.Maps.Count == 0) { Game.FetchMaps(); status = 202; }
            return new { loading = Game.Maps.Count == 0, maps = Game.Maps.Select(Game.MapDto).ToList() };
        }
        if (method == "GET" && root == "camera") return target == "pose" ? Cam.Pose() : Cam.State;
        if (method == "GET" && root == "screen") return Game.ScreenState();
        if (method == "GET" && root == "track") return target == "zones" ? Game.ZonesDto() : Game.TrackDto(query["raw"] == "1");
        if (method == "GET" && root == "find") // dev aid: GameObjects whose name contains q, with path + components + text
        {
            string needle = (query["q"] ?? "").ToLowerInvariant(); int max = int.TryParse(query["max"], out var parsedMax) ? parsedMax : 80;
            var found = new List<object>();
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform.gameObject.scene.name == null) continue; // prefabs/assets
                string path = transform.name; for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
                if (needle.Length > 0 && !path.ToLowerInvariant().Contains(needle)) continue;
                var text = transform.GetComponent<TMPro.TMP_Text>();
                var rect = transform as RectTransform;
                found.Add(new
                {
                    path, active = transform.gameObject.activeInHierarchy, comps = transform.GetComponents<Component>().Select(component => component?.GetType().Name).ToArray(),
                    text = text?.text?.Substring(0, System.Math.Min(80, text.text.Length)), children = transform.childCount,
                    rect = rect == null ? null : new { w = rect.rect.width, h = rect.rect.height, y = rect.anchoredPosition.y, pivotY = rect.pivot.y, anchorMinY = rect.anchorMin.y, anchorMaxY = rect.anchorMax.y },
                    pref = text?.preferredHeight,
                });
                if (found.Count >= max) break;
            }
            return found;
        }
        if (method == "POST" && root == "find" && target == "click") // dev aid: press the Button at ?path=
        {
            var button = GameObject.Find(query["path"] ?? "")?.GetComponent<UnityEngine.UI.Button>();
            if (button == null) { status = 404; return new { error = "no button at path" }; }
            button.onClick.Invoke(); return Ok(1);
        }
        if (method == "POST" && root == "find" && target == "scroll") // dev aid: ScrollRect at ?path= to ?pos= (1 top, 0 bottom)
        {
            var scrollRect = GameObject.Find(query["path"] ?? "")?.GetComponent<UnityEngine.UI.ScrollRect>();
            if (scrollRect == null) { status = 404; return new { error = "no ScrollRect at path" }; }
            scrollRect.verticalNormalizedPosition = float.TryParse(query["pos"], out var parsedPosition) ? parsedPosition : 0f; return Ok(1);
        }
        if (method == "POST" && root == "find" && target == "panel") // dev aid: open a main menu panel by name (Michsky panel manager)
        {
            var panels = Object.FindObjectOfType<Michsky.UI.Zone.MainPanelManager>();
            if (panels == null) { status = 404; return new { error = "no panel manager (menu only)" }; }
            int index = panels.panels.FindIndex(panel => panel.name.Equals(query["name"] ?? "Settings", System.StringComparison.OrdinalIgnoreCase));
            if (index < 0) { status = 404; return new { error = "no such panel", panels = panels.panels.Select(panel => panel.name).ToList() }; }
            panels.PanelAnim(index); return new { ok = true, panel = panels.panels[index].name };
        }
        if (root == "twitch" && target == "token")
        {
            if (method == "GET") return TwitchAuth.Status();
            if (method == "DELETE") { TwitchAuth.Forget(); return Ok(1); }
        }
        if (method == "GET" && root == "version")
        {
            Updates.Kick();
            return new
            {
                api = Plugin.Version, commit = Plugin.Commit, game = Application.version, unity = Application.unityVersion, bepinex = typeof(BepInEx.Paths).Assembly.GetName().Version.ToString(3),
                latest = Updates.Latest, upToDate = Updates.UpToDate, updateUrl = Updates.Url, developer = "Shibiko", twitch = "https://twitch.tv/ShibikoX",
            };
        }
        if (method == "GET" && root == "me") return new { id = Game.StreamerId, login = Game.StreamerLogin, inRace = Game.StreamerVehicle() != null };
        if (method == "GET" && root == "inventory")
        {
            if (string.IsNullOrEmpty(target)) { status = 400; return new { error = "need /inventory/:login" }; }
            var inventory = Game.InventoryDto(target);
            if (inventory == null) { status = 404; return new { error = "no vehicle", target }; }
            return inventory;
        }
        if (method == "GET" && root == "perks") { if (string.IsNullOrEmpty(target)) { status = 400; return new { error = "need /perks/:login" }; } return Game.PerksDto(target); }
        if (method == "GET" && root == "chat") return Game.ChatStatus();
        if (method == "GET" && root == "settings") return Settings.WithConfig();
        if (method == "PUT" && root == "config")
        {
            var result = Settings.ApplyConfig(JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body), out bool restart);
            Plugin.Emit("settings", Settings.WithConfig());
            if (restart) host.StartCoroutine(Plugin.Instance.RestartHttp());
            return result;
        }
        if (method == "PUT" && root == "settings") { Settings.Save(body); Plugin.Emit("settings", Settings.WithConfig()); return Settings.WithConfig(); }
        if (method != "POST") { status = 404; return new { error = "not found" }; }

        switch (root)
        {
            case "boom":
                if (!Game.Running) { status = 409; return new { error = "no race running" }; }
                if (target == null) return Ok(Game.BoomRandom() ? 1 : 0);
                if (target == "all")
                {
                    string except = query["except"];
                    int boomed = Game.Vehicles().Where(vehicle => except == null || (vehicle.Profile().TwitchId() != except && Game.Login(vehicle) != except)).Count(Game.Boom);
                    return Ok(boomed);
                }
                if (int.TryParse(target, out int count) && count <= 200)
                {
                    int hit = 0;
                    for (int i = 0; i < Mathf.Clamp(count, 1, 200) && Game.BoomRandom(); i++) hit++;
                    return Ok(hit);
                }
                return One(target, Game.Boom, ref status);

            case "boost":
            {
                if (target == null) { status = 400; return new { error = "need /boost/:target" }; }
                if (target == "me")
                {
                    var me = Game.StreamerVehicle();
                    if (me == null) { status = 404; return new { error = "streamer not in race" }; }
                    if (!Game.UseBoost(me)) { status = 409; return new { error = "no boosts left or not driving" }; }
                    return Ok(1);
                }
                if (action == "use")
                {
                    var vehicle = Game.Find(target);
                    if (vehicle == null) { status = 404; return new { error = "no vehicle", target }; }
                    if (!Game.UseBoost(vehicle)) { status = 409; return new { error = "no boosts left or not driving", boosts = Game.Boosts(vehicle) }; }
                    return new { ok = true, affected = 1, boosts = Game.Boosts(vehicle) };
                }
                if (action == "add")
                {
                    int amount = int.TryParse(query["n"], out var parsedAmount) ? parsedAmount : 1;
                    if (target == "all") return Targets(target, ref status, vehicle => { Game.AddBoosts(vehicle, amount); return true; });
                    var vehicle = Game.Find(target);
                    if (vehicle == null) { status = 404; return new { error = "no vehicle", target }; }
                    Game.AddBoosts(vehicle, amount);
                    return new { ok = true, affected = 1, boosts = Game.Boosts(vehicle) };
                }
                float? force = float.TryParse(query["force"], out var parsedForce) ? parsedForce : null;
                float? seconds = float.TryParse(query["seconds"], out var parsedSeconds) ? parsedSeconds : null;
                return Targets(target, ref status, vehicle => Game.Boost(vehicle, force, seconds));
            }

            case "speed":
            {
                if (target == null) { status = 400; return new { error = "need /speed/:target" }; }
                float multiplier = float.TryParse(query["mult"], out var parsedMultiplier) ? parsedMultiplier : 0.5f;
                float seconds = float.TryParse(query["seconds"], out var parsedSeconds) ? parsedSeconds : 5f;
                return Targets(target, ref status, vehicle => Game.Speed(vehicle, multiplier, seconds));
            }

            case "respawn":
                if (target == null) { status = 400; return new { error = "need /respawn/:target" }; }
                return Targets(target, ref status, Game.Respawn);

            case "join":
            {
                if (Game.Running || Game.Ended) { status = 409; return new { error = Game.Running ? "race running" : "race over: POST /race/next first" }; }
                if (target == "me") { if (!Game.JoinStreamer(query["color"])) { status = 409; return new { error = "no lobby, not logged in, or already joined" }; } return Ok(1); }
                var parsed = JToken.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
                var items = parsed is JArray array ? array.Children<JObject>().ToList() : new List<JObject> { (JObject)parsed };
                foreach (var item in items) if (item["autoBoost"] != null && !string.IsNullOrWhiteSpace((string)item["login"]))
                    Settings.Current.botOptions[((string)item["login"]).ToLowerInvariant()] = new Settings.BotOpts { autoBoost = (bool)item["autoBoost"] };
                if (items.Any(item => item["autoBoost"] != null)) Settings.Persist();
                int joined = items.Count(item => Game.Join(
                    (string)item["id"] ?? "", (string)item["login"] ?? "", (string)item["displayName"],
                    (string)item["color"], (bool?)item["sub"] ?? false, (string)item["image"]));
                return Ok(joined);
            }

            case "color":
            {
                if (target == null) { status = 400; return new { error = "need /color/:x?color=" }; }
                string color = query["color"];
                if (target == "reset") { Settings.Current.colors.Clear(); Settings.Persist(); return Ok(0); }
                if (color == null) { Settings.Current.colors.Remove(target.ToLowerInvariant()); Settings.Persist(); return Ok(1); }
                if (!Game.SetColor(target, color)) { status = 400; return new { error = "bad color", color }; }
                Plugin.Emit("color", new { login = target.ToLowerInvariant(), color = Settings.Current.colors[target.ToLowerInvariant()] });
                return Ok(1);
            }

            case "chat":
            {
                if (target != "say") break;
                string text = query["text"];
                if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(body)) { try { text = (string)JObject.Parse(body)["text"]; } catch { } }
                if (string.IsNullOrWhiteSpace(text)) { status = 400; return new { error = "need ?text= (or {text} body)" }; }
                if (!Game.SayInChat(text)) { status = 409; return new { error = "chat not connected (game not logged in to Twitch)", chat = Game.ChatStatus() }; }
                return new { ok = true, affected = 1, channel = Game.ChatChannel, text };
            }

            case "finish":
                if (target == null) { status = 400; return new { error = "need /finish/:target" }; }
                return One(target, Game.MarkFinished, ref status);

            case "kick":
                if (target == null) { status = 400; return new { error = "need /kick/:target" }; }
                return One(target, Game.Kick, ref status);

            case "autojoin":
                if (target == "join") { if (Game.Running) { status = 409; return new { error = "race running" }; } return Ok(Settings.JoinAll()); }
                break;

            case "camera":
            {
                float seconds = float.TryParse(query["seconds"], out var parsedSeconds) ? parsedSeconds : 8f;
                float fovFrom = float.TryParse(query["fov"], out var parsedFov) ? parsedFov : 0f;
                float fovTo = float.TryParse(query["fovTo"], out var parsedFovTo) ? parsedFovTo : fovFrom;
                bool ok;
                switch (target)
                {
                    case "auto":
                        Cam.SetAuto(query["on"] == null ? !Cam.Auto : query["on"] == "1" || query["on"] == "true");
                        return Cam.State;
                    case "focus":
                    {
                        var vehicle = action == null ? null : Game.Find(action);
                        if (vehicle == null) { status = 404; return new { error = "no vehicle", target = action }; }
                        ok = Cam.Shot(() => Cam.Follow(vehicle), seconds); break;
                    }
                    case "leader": ok = Cam.Shot(() => Cam.Follow(Cam.Leader()), seconds); break;
                    case "wide":
                    {
                        var vehicle = action == null ? Cam.Leader() : Game.Find(action);
                        if (vehicle == null) { status = 404; return new { error = "no vehicle", target = action }; }
                        ok = Cam.Shot(() => Cam.FollowWide(vehicle), seconds); break;
                    }
                    case "overhead": ok = Cam.Shot(() => Cam.Overhead(seconds, fovFrom, fovTo), seconds); break;
                    case "boom": ok = Cam.LastBoomed != null && Cam.Shot(() => Cam.Orbit(Cam.LastBoomed, seconds, fovFrom, fovTo), seconds); break;
                    case "pack": ok = Cam.Shot(() => Cam.Pack(seconds, fovFrom, fovTo), seconds); break;
                    case "prop": ok = Cam.Shot(() => Cam.PropNearLeader(), seconds); break;
                    case "sweep": ok = Cam.Shot(() => Cam.Sweep(seconds, fovFrom, fovTo), seconds); break;
                    case "grid": ok = Cam.Shot(() => Cam.Grid(seconds), seconds); break;
                    case "side": ok = Cam.Shot(() => Cam.Side(seconds, fovFrom, fovTo), seconds); break;
                    case "finish": ok = Cam.Shot(() => Cam.Finish(seconds, fovFrom, fovTo), seconds); break;
                    case "high": ok = Cam.Shot(() => Cam.High(seconds, fovFrom, fovTo), seconds); break;
                    case "chase":
                    case "front":
                    case "orbit":
                    {
                        var vehicle = action == null ? Cam.Leader() : Game.Find(action);
                        if (vehicle == null) { status = 404; return new { error = "no vehicle", target = action }; }
                        ok = Cam.Shot(() => target == "chase" ? Cam.Chase(vehicle, seconds, fovFrom, fovTo) : target == "front" ? Cam.Front(vehicle, seconds, fovFrom, fovTo) : Cam.Orbit(vehicle, seconds, fovFrom, fovTo), seconds); break;
                    }
                    case "free": ok = Cam.Shot(Cam.Free, 0); break;
                    default: status = 404; return new { error = "not found" };
                }
                if (!ok) { status = 409; return new { error = "no race running (or nothing boomed yet)" }; }
                return Cam.State;
            }

            case "minimap":
            {
                if (query["on"] != null) Minimap.Current.enabled = query["on"] == "1" || query["on"] == "true"; else Minimap.Current.enabled = !Minimap.Current.enabled;
                Settings.Current.minimap = JObject.FromObject(Minimap.Current);
                Settings.Save(JsonConvert.SerializeObject(Settings.Current));
                Plugin.Emit("settings", Settings.WithConfig());
                return Minimap.State;
            }

            case "lobby":
            {
                if (target == "exit")
                {
                    if (!Game.InLobby) { status = 409; return new { error = "not in a lobby" }; }
                    var exit = GameObject.Find("PreGameCanvas/PreGameScreen/Content/Box 1/Game Settings Overview/Buttons/Exit")?.GetComponent<UnityEngine.UI.Button>();
                    if (exit == null) { status = 500; return new { error = "exit button not found" }; }
                    exit.onClick.Invoke(); return Ok(1);
                }
                var state = Game.CreateLobby(query["map"]);
                if (state == null) { status = 409; return new { error = Game.Running ? "race running" : Game.InLobby ? "already in a lobby" : "not on the home screen" }; }
                if (state == "opening") status = 202;
                return new { ok = true, state };
            }

            case "race":
                if (target == "start") { if (!Game.StartRace(query["now"] == "1" || query["now"] == "true")) { status = 409; return new { error = Game.Ended ? "race over: POST /race/next first" : "no lobby with cars" }; } return Ok(1); }
                if (target == "end") { Game.EndRace(); return Ok(1); }
                if (target == "next") { if (!Game.NextRace()) { status = 409; return new { error = Game.Running ? "race running" : "no maps queued, call /lobby" }; } return Ok(1); }
                break;
        }
        status = 404; return new { error = "not found" };
    }

    static object Ok(int affected) => new { ok = true, affected };

    static object One(string target, System.Func<Vehicle, bool> act, ref int status)
    {
        var vehicle = Game.Find(target);
        if (vehicle == null) { status = 404; return new { error = "no vehicle", target }; }
        if (!act(vehicle)) { status = 409; return new { error = "not applicable", target }; }
        return Ok(1);
    }

    static object Targets(string target, ref int status, System.Func<Vehicle, bool> act)
    {
        if (target != "all") return One(target, act, ref status);
        return Ok(Game.Vehicles().Count(act));
    }
}
