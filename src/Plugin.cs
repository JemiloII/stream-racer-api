using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace StreamRacerApi;

[BepInPlugin("shibiko.streamracer.api", "StreamRacerApi", Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Version = "1.1.0"; // semver, bumped by scripts/commit-msg from the commit message
    public static string Commit => typeof(Plugin).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false) is System.Reflection.AssemblyInformationalVersionAttribute[] a && a.Length > 0 ? a[0].InformationalVersion : "dev";
    public static ConfigEntry<string> UpdateUrl;
    public static ConfigEntry<int> Port;
    public static ConfigEntry<KeyCode> CamUp, CamDown, BoostKey;
    public static ConfigEntry<float> TickHz, PosHz;
    public static ConfigEntry<string> Token;
    public static ConfigEntry<bool> BindAll;

    static Plugin _i;
    public static Plugin Instance => _i;
    public static void RunOnMain(Action a) => MainThread.Enqueue(a);
    public static BepInEx.Logging.ManualLogSource Log;
    static readonly ConcurrentQueue<Action> MainThread = new();
    static readonly List<StreamWriter> Sse = new();
    HttpListener _http;
    float _nextTick, _nextPos;

    void Awake()
    {
        _i = this; Log = Logger;
        Port = Config.Bind("api", "Port", 8793, "HTTP/SSE port");
        TickHz = Config.Bind("api", "TickHz", 4f, "full 'positions' snapshot rate while racing");
        PosHz = Config.Bind("api", "PosHz", 60f, "light 'pos' event rate while racing (login, x, z, pct, place per car) for smooth maps/overlays");
        UpdateUrl = Config.Bind("api", "UpdateUrl", "", "URL of a JSON {version, url} describing the latest release; GET /version then reports upToDate and the pages color the version. Empty = no check.");
        Token = Config.Bind("api", "Token", "", "if set, every API call needs 'Authorization: Bearer <token>' (or ?token= for SSE). The control page still loads; enter the token on its Settings page.");
        BindAll = Config.Bind("api", "BindAll", false, "listen on all interfaces so a bot on another machine can reach the API. Set a Token first. Windows needs once: netsh http add urlacl url=http://+:PORT/ user=Everyone");
        CamUp = Config.Bind("camera", "Up", KeyCode.Space, "free cam up (game's E still works)");
        CamDown = Config.Bind("camera", "Down", KeyCode.C, "free cam down (game's Q still works)");
        BoostKey = Config.Bind("hotkeys", "Boost", KeyCode.R, "use one of the streamer's own boosts (same as typing !boost)");

        new Harmony("shibiko.streamracer.api").PatchAll();
        Settings.Init();

        StartHttp();
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
        catch (HttpListenerException e)
        {
            Logger.LogError($"listen failed ({e.Message}). For BindAll run once as admin: netsh http add urlacl url=http://+:{Port.Value}/ user=Everyone  — falling back to localhost");
            _http = new HttpListener();
            _http.Prefixes.Add($"http://127.0.0.1:{Port.Value}/");
            _http.Prefixes.Add($"http://localhost:{Port.Value}/");
            _http.Start();
        }
        var l = _http;
        new Thread(() => Listen(l)) { IsBackground = true }.Start();
        Logger.LogInfo($"listening on http://127.0.0.1:{Port.Value}/");
    }

    // Port / bind changed at runtime: drop every client (SSE included; they reconnect on their own) and rebind.
    public System.Collections.IEnumerator RestartHttp()
    {
        yield return new WaitForSeconds(0.4f); // let the response that asked for this go out first
        try { _http?.Stop(); _http?.Close(); } catch { }
        lock (Sse) { foreach (var w in Sse) { try { w.Close(); } catch { } } Sse.Clear(); }
        StartHttp();
    }

    public static string BaseUrl => $"http://127.0.0.1:{Port.Value}/";

    void LateUpdate() { Minimap.Tick(); try { Game.ColorLeaderboard(); Game.TrackStates(); } catch { } }

    string _lastScreen; float _nextScreen;

    void Update()
    {
        while (MainThread.TryDequeue(out var a)) a();
        try { Credits.Tick(); } catch (Exception e) { if (Time.frameCount % 600 == 0) Log.LogWarning("credits: " + e.Message); }
        if (Time.unscaledTime >= _nextScreen)
        {
            _nextScreen = Time.unscaledTime + 0.25f;
            try
            {
                var st = Game.ScreenState();
                string key = JsonConvert.SerializeObject(st);
                if (key != _lastScreen) { _lastScreen = key; Emit("screen", st); }
            }
            catch { }
        }
        if (Cam.Auto && Game.Running && CameraInput()) Cam.NoteUserInput();
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
    static readonly KeyCode[] CamKeys =
    {
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
        KeyCode.F, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.Mouse0, KeyCode.Mouse1,
    };
    static bool CameraInput()
    {
        foreach (var k in CamKeys) if (Input.GetKey(k)) return true;
        if (Input.GetKey(CamUp.Value) || Input.GetKey(CamDown.Value)) return true;
        return Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.5f || Input.GetAxisRaw("Mouse ScrollWheel") != 0f;
    }

    void OnDestroy() => _http?.Stop();

    // ---- SSE ----

    public static void Emit(string evt, object data)
    {
        string json = JsonConvert.SerializeObject(data);
        string payload = $"event: {evt}\ndata: {json}\n\n";
        Webhooks.Fire(evt, json);
        lock (Sse)
            Sse.RemoveAll(w =>
            {
                try { w.Write(payload); w.Flush(); return false; }
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
    static bool ServeUi(string path, HttpListenerResponse res)
    {
        if (path == "") path = "index.html";
        if (path == "overlay") path = "overlay.html";
        if (path == "minimap") path = "minimap.html";
        var ext = Path.GetExtension(path);
        if (!Mime.TryGetValue(ext, out var mime)) return false;
        var asm = typeof(Plugin).Assembly;
        // MSBuild's RecursiveDir uses backslashes on Windows
        using var s = asm.GetManifestResourceStream("ui/" + path) ?? asm.GetManifestResourceStream("ui/" + path.Replace('/', '\\'));
        if (s == null) return false;
        res.ContentType = mime;
        res.AddHeader("Cache-Control", "no-cache");
        s.CopyTo(res.OutputStream); res.Close();
        return true;
    }

    // GET /twitch/users?logins=a,b  -> [{id, login, displayName, image, description}] via Helix with the game's token.
    static void TwitchUsers(string logins, HttpListenerResponse res)
    {
        int status = 200; object result;
        try
        {
            var names = (logins ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().Take(100).ToList();
            string token = Game.TwitchToken;
            if (names.Count == 0) { status = 400; result = new { error = "logins required" }; }
            else if (string.IsNullOrEmpty(token)) { status = 409; result = new { error = "not logged in to Twitch in-game" }; }
            else
            {
                var wc = new System.Net.WebClient();
                wc.Headers["Authorization"] = "Bearer " + token;
                wc.Headers["Client-Id"] = Game.TwitchClientId;
                string url = "https://api.twitch.tv/helix/users?" + string.Join("&", names.Select(n => "login=" + Uri.EscapeDataString(n)));
                var data = Newtonsoft.Json.Linq.JObject.Parse(wc.DownloadString(url))["data"];
                result = new
                {
                    users = data.Select(u => new
                    {
                        id = (string)u["id"], login = (string)u["login"], displayName = (string)u["display_name"],
                        image = (string)u["profile_image_url"], description = (string)u["description"],
                    }).ToList(),
                };
            }
        }
        catch (Exception e) { status = 502; result = new { error = e.Message }; }
        byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
        res.StatusCode = status; res.ContentType = "application/json"; res.ContentLength64 = bytes.Length;
        try { res.OutputStream.Write(bytes, 0, bytes.Length); res.Close(); } catch { }
    }

    static bool Authorized(HttpListenerRequest req)
    {
        string t = Token.Value;
        string h = req.Headers["Authorization"];
        if (h != null && h.StartsWith("Bearer ") && h.Substring(7) == t) return true;
        return req.QueryString["token"] == t;
    }

    // ---- HTTP ----

    void Listen(HttpListener l)
    {
        while (l.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = l.GetContext(); } catch { break; }
            ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request; var res = ctx.Response;
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
        if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }

        string path = req.Url.AbsolutePath.Trim('/');
        if (req.HttpMethod == "GET" && Pages.Contains(path) && (req.Headers["Accept"] ?? "").Contains("text/html")) path = "index.html"; // app routes
        if (req.HttpMethod == "GET" && ServeUi(path, res)) return;
        if (!string.IsNullOrEmpty(Token.Value) && !Authorized(req))
        {
            byte[] b = Encoding.UTF8.GetBytes("{\"error\":\"unauthorized\"}");
            res.StatusCode = 401; res.ContentType = "application/json"; res.ContentLength64 = b.Length;
            try { res.OutputStream.Write(b, 0, b.Length); res.Close(); } catch { }
            return;
        }
        if (req.HttpMethod == "GET" && path == "events")
        {
            res.ContentType = "text/event-stream";
            res.AddHeader("Cache-Control", "no-cache");
            res.SendChunked = true;
            var w = new StreamWriter(res.OutputStream, new UTF8Encoding(false));
            w.Write(": connected\n\n"); w.Flush();
            lock (Sse) Sse.Add(w);
            return; // stays open; Emit() removes it on write failure
        }

        if (req.HttpMethod == "GET" && path == "twitch/users") { TwitchUsers(req.QueryString["logins"], res); return; }
        if (req.HttpMethod == "PUT" && path.StartsWith("image/"))
        {
            string login = path.Substring(6);
            string data = req.HasEntityBody ? new StreamReader(req.InputStream).ReadToEnd() : "";
            string saved = null;
            try { saved = Game.SaveImage(login, Newtonsoft.Json.Linq.JObject.Parse(data)["data"]?.ToString()); } catch { }
            byte[] b = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(saved == null ? new { error = "send {data: 'data:image/png;base64,…'}" } : (object)new { ok = true, path = saved, url = "/image/" + login }));
            res.StatusCode = saved == null ? 400 : 200; res.ContentType = "application/json"; res.ContentLength64 = b.Length;
            try { res.OutputStream.Write(b, 0, b.Length); res.Close(); } catch { }
            return;
        }
        if (req.HttpMethod == "GET" && path.StartsWith("image/"))
        {
            var img = Game.CustomImageBytes(path.Substring(6));
            if (img == null) { res.StatusCode = 404; res.Close(); return; }
            res.ContentType = img.Length > 3 && img[0] == 0x89 ? "image/png" : img.Length > 2 && img[0] == 0xFF ? "image/jpeg" : img.Length > 3 && img[0] == 'G' ? "image/gif" : "application/octet-stream";
            res.ContentLength64 = img.Length;
            try { res.OutputStream.Write(img, 0, img.Length); res.Close(); } catch { }
            return;
        }

        string body = req.HasEntityBody ? new StreamReader(req.InputStream).ReadToEnd() : "";
        var q = req.QueryString;
        int status = 200; object result = null;
        var done = new ManualResetEventSlim();
        MainThread.Enqueue(() =>
        {
            try { result = Routes.Dispatch(_i, req.HttpMethod, path.Split('/'), q, body, out status); }
            catch (Exception e) { status = 501; result = new { error = e.GetType().Name, message = e.Message }; }
            finally { done.Set(); }
        });
        if (!done.Wait(5000)) { status = 504; result = new { error = "game thread busy" }; }

        byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result ?? new { ok = true }));
        res.StatusCode = status;
        res.ContentType = "application/json";
        res.ContentLength64 = bytes.Length;
        try { res.OutputStream.Write(bytes, 0, bytes.Length); res.Close(); } catch { }
    }
}
