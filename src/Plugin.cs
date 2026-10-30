// The BepInEx plugin: config entries, the HttpListener with its worker thread, SSE fan-out, bearer-token auth,
// the embedded control page (ui/**), per-frame ticks (positions, pos, screen changes) and the boost hotkey.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;

namespace StreamRacerApi;

[BepInPlugin("shibiko.streamracer.api", "StreamRacerApi", Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Version = "1.49.3"; // semver, bumped by scripts/post-commit from the commit message
    public static string Commit =>
        typeof(Plugin).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false) is System.Reflection.AssemblyInformationalVersionAttribute[] attributes && attributes.Length > 0
            ? attributes[0].InformationalVersion : "dev";
    public static ConfigEntry<string> UpdateUrl;
    public static ConfigEntry<int> Port;
    public static ConfigEntry<KeyCode> CamUp, CamDown, BoostKey;
    public static ConfigEntry<float> TickHz, PosHz;
    public static ConfigEntry<string> Token;
    public static ConfigEntry<bool> BindAll;

    static Plugin _instance;
    public static Plugin Instance => _instance;
    public static void RunOnMain(Action action) => MainThread.Enqueue(action);
    public static BepInEx.Logging.ManualLogSource Log;
    static readonly ConcurrentQueue<Action> MainThread = new();
    static readonly List<StreamWriter> SseClients = new();
    HttpListener _http;
    float _nextTick, _nextPos;

    void Awake()
    {
        _instance = this; Log = Logger;
        Port = Config.Bind("api", "Port", 8793, "HTTP/SSE port");
        TickHz = Config.Bind("api", "TickHz", 4f, "full 'positions' snapshot rate while racing");
        PosHz = Config.Bind("api", "PosHz", 60f, "light 'pos' event rate while racing (login, x, z, pct, place per car) for smooth maps/overlays");
        UpdateUrl = Config.Bind("api", "UpdateUrl", "", "where to look for updates: empty = this mod's GitHub releases, a URL of a JSON {version, url}, or off");
        Token = Config.Bind("api", "Token", "", "if set, every API call needs 'Authorization: Bearer <token>' (or ?token= for SSE). The control page still loads; enter the token on its Settings page.");
        BindAll = Config.Bind("api", "BindAll", false, "listen on all interfaces so a bot on another machine can reach the API. Set a Token first. Windows needs once: netsh http add urlacl url=http://+:PORT/ user=Everyone");
        CamUp = Config.Bind("camera", "Up", KeyCode.Space, "free cam up (game's E still works)");
        CamDown = Config.Bind("camera", "Down", KeyCode.C, "free cam down (game's Q still works)");
        BoostKey = Config.Bind("hotkeys", "Boost", KeyCode.R, "use one of the streamer's own boosts (same as typing !boost)");

        Updates.Kick();
        if (TooManyFailedLaunches())
        {
            Off = SafeMode = true;
            Logger.LogError($"the game didn't reach its menu the last {FailedLaunchLimit} times with the mod on, "
                + "so the mod is off (safe mode); the in-game window turns it back on");
            return;
        }
        if (!PatchGame())
        {
            return;
        }
        Settings.Init();

        StartHttp();
    }

    // A game update renames every obfuscated method, so patches stop finding their targets. Then the mod takes back
    // whatever it did patch and stays off for this session: the game runs untouched until a mod build for it exists.
    public static bool Off;        // nothing patched, no server, only the UpdatePrompt: a game mismatch or safe mode
    public static bool SafeMode;   // off because recent launches never reached the menu

    // Safe mode: every launch counts as failed until the game shows its home screen. After FailedLaunchLimit failures
    // in a row (crash or hang while loading, the mod's fault or not) the mod stays off until the player turns it back on.
    const int FailedLaunchLimit = 2;
    static string LaunchFile => Path.Combine(Paths.ConfigPath, "shibiko.streamracer.launches");
    bool _reachedMenu;

    static bool TooManyFailedLaunches()
    {
        int failed = 0;
        if (File.Exists(LaunchFile))
        {
            int.TryParse(File.ReadAllText(LaunchFile).Trim(), out failed);
        }
        if (failed >= FailedLaunchLimit)
        {
            return true;
        }
        File.WriteAllText(LaunchFile, (failed + 1).ToString());
        return false;
    }

    public static void ResetLaunches() => File.WriteAllText(LaunchFile, "0");

    void OnGUI() => UpdatePrompt.Draw();

    bool PatchGame()
    {
        var harmony = new Harmony("shibiko.streamracer.api");
        try
        {
            harmony.PatchAll();
            return true;
        }
        catch (System.Exception error)
        {
            harmony.UnpatchSelf();
            Off = true;
            Logger.LogError($"game {Application.version} doesn't match this mod build ({Version}), mod is off; "
                + $"get the latest from https://github.com/JemiloII/stream-racer-api. ({error.GetBaseException().Message})");
            return false;
        }
    }

    void StartHttp()
    {
        _http = new HttpListener();
        if (BindAll.Value && !string.IsNullOrEmpty(Token.Value)) _http.Prefixes.Add($"http://+:{Port.Value}/");
        else
        {
            _http.Prefixes.Add($"http://127.0.0.1:{Port.Value}/");
            _http.Prefixes.Add($"http://localhost:{Port.Value}/");
            if (BindAll.Value) Logger.LogWarning("BindAll ignored: set api.Token first");
        }
        try { _http.Start(); }
        catch (HttpListenerException error)
        {
            Logger.LogError($"listen failed ({error.Message}). For BindAll run once as admin: netsh http add urlacl url=http://+:{Port.Value}/ user=Everyone  — falling back to localhost");
            _http = new HttpListener();
            _http.Prefixes.Add($"http://127.0.0.1:{Port.Value}/");
            _http.Prefixes.Add($"http://localhost:{Port.Value}/");
            _http.Start();
        }
        var listener = _http;
        new Thread(() => Listen(listener)) { IsBackground = true }.Start();
        Logger.LogInfo($"listening on http://127.0.0.1:{Port.Value}/");
        Chaos.PreloadSiren();   // so the first raid of the session already has the real recording
    }

    // Port / bind changed at runtime: drop every client (SSE included; they reconnect on their own) and rebind.
    public System.Collections.IEnumerator RestartHttp()
    {
        yield return new WaitForSeconds(0.4f); // let the response that asked for this go out first
        try { _http?.Stop(); _http?.Close(); } catch { }
        lock (SseClients) { foreach (var writer in SseClients) { try { writer.Close(); } catch { } } SseClients.Clear(); }
        StartHttp();
    }

    // Camera.main drawn into a 1280x720 texture (no UI overlay): lets tests see the 3D scene with the window minimized.
    // ?near=<login>: a throwaway camera beside and in front of that car instead, so a test can look closely without
    // ever moving the streamer's camera.
    static byte[] CameraShot(string near)
    {
        var main = Camera.main;
        var car = string.IsNullOrEmpty(near) ? null : Game.Find(near)?.Car();
        Camera camera = main;
        GameObject temporary = null;
        if (car != null)
        {
            temporary = new GameObject("sr-test-camera");
            camera = temporary.AddComponent<Camera>();
            var forward = car.transform.forward;
            temporary.transform.position = car.transform.position + forward * 6f + car.transform.right * 3f + Vector3.up * 2f;
            temporary.transform.LookAt(car.transform.position + Vector3.up * 1.4f);
            camera.enabled = false;
        }
        if (camera == null)
        {
            return null;
        }
        var target = RenderTexture.GetTemporary(1280, 720, 24);
        var before = camera.targetTexture;
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = before;
        var active = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(target);
        var png = texture.EncodeToPNG();
        Destroy(texture);
        if (temporary != null)
        {
            Destroy(temporary);
        }
        return png;
    }

    public static string BaseUrl => $"http://127.0.0.1:{Port.Value}/";

    void LateUpdate() { Minimap.Tick(); try { Game.DecorateLeaderboard(); Game.TrackStates(); } catch { } }

    string _lastScreen; float _nextScreen;

    void Update()
    {
        if (Off)
        {
            return;
        }
        while (MainThread.TryDequeue(out var action)) action();
        try { Credits.Tick(); } catch (Exception error) { if (Time.frameCount % 600 == 0) Log.LogWarning("credits: " + error.Message); }
        try { VisualsSetting.Tick(); } catch (Exception error) { if (Time.frameCount % 600 == 0) Log.LogWarning("visuals setting: " + error.Message); }
        if (Time.unscaledTime >= _nextScreen)
        {
            _nextScreen = Time.unscaledTime + 0.25f;
            try
            {
                var screenState = Game.ScreenState();
                string key = JsonConvert.SerializeObject(screenState);
                if (key != _lastScreen) { _lastScreen = key; Emit("screen", screenState); }
                if (!_reachedMenu && key.Contains("\"screen\":\"home\""))
                {
                    _reachedMenu = true;
                    ResetLaunches();
                }
            }
            catch { }
        }
        Cam.PlaneCamInput();     // any key / click / scroll leaves the air-raid plane cam
        if (Cam.Auto && Game.Running && !Cam.OnPlane && CameraInput()) Cam.NoteUserInput();
        Weather.Tick();          // rain/snow particles + surface spawns (fails soft inside)
        Game.SurfacesTick();     // ice/puddles/mud/snow under each car (fails soft inside)
        Chaos.Tick();            // random chaos events (fails soft inside)
        if (Input.GetKeyDown(BoostKey.Value))
        {
            var me = Game.StreamerVehicle();
            if (me != null) Game.UseBoost(me);
        }
        if (Game.Running && Time.unscaledTime >= _nextTick)
        {
            _nextTick = Time.unscaledTime + 1f / Mathf.Max(0.5f, TickHz.Value);
            Emit("positions", Game.Snapshot());
        }
        if (Game.Running && PosHz.Value > 0 && Time.unscaledTime >= _nextPos)
        {
            _nextPos = Time.unscaledTime + 1f / Mathf.Max(1f, PosHz.Value);
            Emit("pos", Game.PosFrame());
        }
    }

    // Anything a streamer uses to drive the game camera: 1-0 (follow), F (prop cam), WASD/QE (free cam), mouse look/zoom.
    static readonly KeyCode[] CameraKeys =
    {
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
        KeyCode.F, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.Mouse0, KeyCode.Mouse1,
    };
    static bool CameraInput()
    {
        foreach (var key in CameraKeys) if (Input.GetKey(key)) return true;
        if (Input.GetKey(CamUp.Value) || Input.GetKey(CamDown.Value)) return true;
        return Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.5f || Input.GetAxisRaw("Mouse ScrollWheel") != 0f;
    }

    void OnDestroy() => _http?.Stop();

    // ---- SSE ----

    public static void Emit(string eventName, object data)
    {
        string json = JsonConvert.SerializeObject(data);
        string payload = $"event: {eventName}\ndata: {json}\n\n";
        Webhooks.Fire(eventName, json);
        lock (SseClients)
            SseClients.RemoveAll(writer =>
            {
                try { writer.Write(payload); writer.Flush(); return false; }
                catch { return true; }
            });
    }

    // ---- embedded control page (ui/**) ----

    static readonly Dictionary<string, string> Mime = new()
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png", [".json"] = "application/json",
    };

    static readonly HashSet<string> Pages = new() { "controls", "camera", "bots", "settings", "api" };
    static void WriteJson(HttpListenerResponse response, int status, object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        response.StatusCode = status; response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
        try { response.OutputStream.Write(bytes, 0, bytes.Length); response.Close(); } catch { }
    }

    static bool ServeUi(string path, HttpListenerResponse response)
    {
        if (path == "") path = "index.html";
        if (path == "overlay") path = "overlay.html";
        if (path == "minimap") path = "minimap.html";
        if (path == "leaderboard") path = "leaderboard.html";
        if (path == "rankings") path = "rankings.html";
        var extension = Path.GetExtension(path);
        if (!Mime.TryGetValue(extension, out var mime)) return false;
        var assembly = typeof(Plugin).Assembly;
        // MSBuild's RecursiveDir uses backslashes on Windows
        using var resource = assembly.GetManifestResourceStream("ui/" + path) ?? assembly.GetManifestResourceStream("ui/" + path.Replace('/', '\\'));
        if (resource == null) return false;
        response.ContentType = mime;
        response.AddHeader("Cache-Control", "no-cache");
        resource.CopyTo(response.OutputStream); response.Close();
        return true;
    }

    // GET /twitch/users?logins=a,b  -> [{id, login, displayName, image, description}] via Helix with the game's token.
    static void TwitchUsers(string logins, HttpListenerResponse response)
    {
        int status = 200; object result;
        try
        {
            var names = (logins ?? "").Split(',').Select(login => login.Trim().ToLowerInvariant()).Where(login => login.Length > 0).Distinct().Take(100).ToList();
            string token = Game.TwitchToken;
            if (names.Count == 0) { status = 400; result = new { error = "logins required" }; }
            else if (string.IsNullOrEmpty(token)) { status = 409; result = new { error = "not logged in to Twitch in-game" }; }
            else
            {
                var web = new WebClient();
                web.Headers["Authorization"] = "Bearer " + token;
                web.Headers["Client-Id"] = Game.TwitchClientId;
                string url = "https://api.twitch.tv/helix/users?" + string.Join("&", names.Select(login => "login=" + Uri.EscapeDataString(login)));
                var users = Newtonsoft.Json.Linq.JObject.Parse(web.DownloadString(url))["data"];
                result = new
                {
                    users = users.Select(user => new
                    {
                        id = (string)user["id"], login = (string)user["login"], displayName = (string)user["display_name"],
                        image = (string)user["profile_image_url"], description = (string)user["description"],
                    }).ToList(),
                };
            }
        }
        catch (Exception error) { status = 502; result = new { error = error.Message }; }
        WriteJson(response, status, result);
    }

    static bool Authorized(HttpListenerRequest request)
    {
        string token = Token.Value;
        string header = request.Headers["Authorization"];
        if (header != null && header.StartsWith("Bearer ") && header.Substring(7) == token) return true;
        return request.QueryString["token"] == token;
    }

    // ---- HTTP ----

    void Listen(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = listener.GetContext(); } catch { break; }
            ThreadPool.QueueUserWorkItem(_ => Handle(context));
        }
    }

    void Handle(HttpListenerContext context)
    {
        var request = context.Request; var response = context.Response;
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
        if (request.HttpMethod == "OPTIONS") { response.StatusCode = 204; response.Close(); return; }

        string path = request.Url.AbsolutePath.Trim('/');
        if (request.HttpMethod == "GET" && Pages.Contains(path) && (request.Headers["Accept"] ?? "").Contains("text/html")) path = "index.html"; // app routes
        if (request.HttpMethod == "GET" && ServeUi(path, response)) return;
        if (!string.IsNullOrEmpty(Token.Value) && !Authorized(request))
        {
            byte[] denied = Encoding.UTF8.GetBytes("{\"error\":\"unauthorized\"}");
            response.StatusCode = 401; response.ContentType = "application/json"; response.ContentLength64 = denied.Length;
            try { response.OutputStream.Write(denied, 0, denied.Length); response.Close(); } catch { }
            return;
        }
        if (request.HttpMethod == "GET" && path == "events")
        {
            response.ContentType = "text/event-stream";
            response.AddHeader("Cache-Control", "no-cache");
            response.SendChunked = true;
            var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false));
            writer.Write(": connected\n\n"); writer.Flush();
            lock (SseClients) SseClients.Add(writer);
            return; // stays open; Emit() removes it on write failure
        }

        if (request.HttpMethod == "GET" && path == "twitch/users") { TwitchUsers(request.QueryString["logins"], response); return; }
        if (request.HttpMethod == "GET" && path == "twitch/auth")
        {
            string clientId = request.QueryString["clientId"] ?? Settings.Current.twitchClientId;
            if (string.IsNullOrWhiteSpace(clientId)) { WriteJson(response, 400, new { error = "set settings.twitchClientId first (your Twitch app's client id)", redirectUri = TwitchAuth.RedirectUri }); return; }
            if (clientId != Settings.Current.twitchClientId) { Settings.Current.twitchClientId = clientId.Trim(); Settings.Persist(); }
            response.StatusCode = 302; response.RedirectLocation = TwitchAuth.AuthorizeUrl(clientId.Trim(), TwitchAuth.NewState()); response.Close(); return;
        }
        if (request.HttpMethod == "GET" && path == "twitch/callback")
        {
            byte[] page = Encoding.UTF8.GetBytes(TwitchAuth.CallbackHtml);
            response.ContentType = "text/html; charset=utf-8"; response.ContentLength64 = page.Length;
            try { response.OutputStream.Write(page, 0, page.Length); response.Close(); } catch { }
            return;
        }
        if (request.HttpMethod == "POST" && path == "twitch/token")
        {
            string data = request.HasEntityBody ? new StreamReader(request.InputStream).ReadToEnd() : "";
            Newtonsoft.Json.Linq.JObject parsed = null; try { parsed = Newtonsoft.Json.Linq.JObject.Parse(data); } catch { }
            string token = (string)parsed?["token"], state = (string)parsed?["state"];
            if (string.IsNullOrWhiteSpace(token)) { WriteJson(response, 400, new { error = "need {token}" }); return; }
            if (!string.IsNullOrEmpty(state) && !TwitchAuth.StateOk(state)) { WriteJson(response, 400, new { error = "state mismatch: start again from /twitch/auth" }); return; }
            var stored = TwitchAuth.Store(token);
            WriteJson(response, stored["error"] != null ? 401 : 200, stored); return;
        }
        if (request.HttpMethod == "PUT" && path.StartsWith("image/"))
        {
            string login = path.Substring(6);
            string data = request.HasEntityBody ? new StreamReader(request.InputStream).ReadToEnd() : "";
            string saved = null;
            try { saved = Game.SaveImage(login, Newtonsoft.Json.Linq.JObject.Parse(data)["data"]?.ToString()); } catch { }
            WriteJson(response, saved == null ? 400 : 200, saved == null ? new { error = "send {data: 'data:image/png;base64,…'}" } : (object)new { ok = true, path = saved, url = "/image/" + login });
            return;
        }
        if (request.HttpMethod == "GET" && path == "standings")   // file reads: off the game thread
        {
            int.TryParse(request.QueryString["limit"], out var limit);
            WriteJson(response, 200, Game.StandingsDto((request.QueryString["period"] ?? "week").ToLowerInvariant(), limit));
            return;
        }
        if (request.HttpMethod == "GET" && path == "find/shot.png") // dev aid: what the game camera sees, even minimized
        {
            byte[] shot = null;
            var rendered = new ManualResetEventSlim();
            MainThread.Enqueue(() =>
            {
                try
                {
                    shot = CameraShot(request.QueryString["near"]);
                }
                catch
                {
                }
                finally
                {
                    rendered.Set();
                }
            });
            rendered.Wait(5000);
            if (shot == null)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }
            response.ContentType = "image/png";
            response.ContentLength64 = shot.Length;
            try
            {
                response.OutputStream.Write(shot, 0, shot.Length);
                response.Close();
            }
            catch
            {
            }
            return;
        }
        if (request.HttpMethod == "GET" && path == "minimap/track.png")
        {
            byte[] png = null;
            var read = new ManualResetEventSlim();
            MainThread.Enqueue(() => { try { png = Minimap.TrackPng(); } catch { } finally { read.Set(); } });
            read.Wait(5000);
            if (png == null) { response.StatusCode = 404; response.Close(); return; }
            response.ContentType = "image/png"; response.ContentLength64 = png.Length;
            try { response.OutputStream.Write(png, 0, png.Length); response.Close(); } catch { }
            return;
        }
        if (request.HttpMethod == "GET" && path.StartsWith("image/"))
        {
            var image = Game.CustomImageBytes(path.Substring(6));
            if (image == null) { response.StatusCode = 404; response.Close(); return; }
            response.ContentType = image.Length > 3 && image[0] == 0x89 ? "image/png" : image.Length > 2 && image[0] == 0xFF ? "image/jpeg" : image.Length > 3 && image[0] == 'G' ? "image/gif" : "application/octet-stream";
            response.ContentLength64 = image.Length;
            try { response.OutputStream.Write(image, 0, image.Length); response.Close(); } catch { }
            return;
        }

        string body = request.HasEntityBody ? new StreamReader(request.InputStream).ReadToEnd() : "";
        var query = request.QueryString;
        int status = 200; object result = null;
        var done = new ManualResetEventSlim();
        MainThread.Enqueue(() =>
        {
            try { result = Routes.Dispatch(_instance, request.HttpMethod, path.Split('/'), query, body, out status); }
            catch (Exception error) { status = 501; result = new { error = error.GetType().Name, message = error.Message }; }
            finally { done.Set(); }
        });
        if (!done.Wait(5000)) { status = 504; result = new { error = "game thread busy" }; }

        WriteJson(response, status, result ?? new { ok = true });
    }
}
