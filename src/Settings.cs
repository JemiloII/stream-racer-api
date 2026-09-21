using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Cage.StreamRacer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

// Server-side settings so the control page behaves the same from any browser/OBS dock.
static class Settings
{
    public class Entry { public string id = ""; public string login = ""; public string displayName; public string color; public bool sub; public string image; }
    // Who gets what. Tiers: "everyone" | "follower" | "subscriber" | "off". Extra boosts stack (a subscribed follower gets both).
    public class BotOpts { public bool autoBoost = true; }
    public class Webhook { public string @event = "race_end"; public string url = ""; public string method = "POST"; public string header = ""; public string body = ""; public bool enabled = true; }
    public static bool AutoBoosts(string login) => login != null && (!Current.botOptions.TryGetValue(login.ToLowerInvariant(), out var o) || o.autoBoost);

    public class Perks
    {
        public string colorCommand = "follower";   // who may use the chat color command
        public string coloredNames = "everyone";   // whose name shows in color on the leaderboard (others white)
        public int boostFollower = 0, boostSubscriber = 1, boostDeveloper = 1, boostHost = 0; // extra boosts on join
    }

    public class Model
    {
        public bool autoJoinEnabled = true; // kept for old settings files; the list itself is the switch now
        public bool autoJoinStreamer = false; // add the streamer's own car to every lobby (= the JOIN GAME button)
        public string streamerColor = "#ff8a00"; // the streamer's car/name color when joined through us
        public List<Entry> autoJoin = new();
        public JObject ui = new(); // free-form page defaults (boom count, slow mult, ...)
        public List<string> bots; // Twitch logins of the bot racers; null = defaults
        public List<Entry> customBots = new(); // non-Twitch bots: login, displayName, color, image (file path / url)
        public Dictionary<string, BotOpts> botOptions = new(); // per bot login: autoBoost (default true)
        public List<Webhook> webhooks = new();                  // fire an HTTP request when an event happens (race_end -> your bot)
        public bool colorLeaderboard = true;   // in-game leaderboard names in each car's color
        public bool colorCommandEnabled = true; // viewers can set their own color from chat
        public bool respawnCommandEnabled = true; // viewers can respawn their own car from chat
        public string respawnCommand = "!race respawn";
        public string colorCommand = "!race color";  // e.g. "!color #ff8800" or "!color red"
        public Dictionary<string, string> colors = new(); // login -> hex, persisted; applied whenever they join
        public Perks perks = new();
        public string twitchToken = "";    // optional: a token with moderator:read:followers (from your overlay/bot app) for follower checks
        public string twitchClientId = ""; // the client id that token belongs to
        public JObject overlay = new(); // look & feel of /overlay (size, board rows, colors, ...)
        public JObject minimap = new(); // in-game picture-in-picture map (enabled, x, y, w, h, marker, bg, ...)
    }

    // Famous enough that there's no way they're actually in chat. mrbeast6000 is the real MrBeast.
    public static readonly string[] DefaultBots =
    {
        "elonmusk", "mrbeast6000", "snoopdogg", "ishowspeed", "terrycrews", "jimmyfallon",
        "zackrawrr", "pewdiepie", "markiplier", "jacksepticeye", "filian", "faker",
    };

    public static List<string> Bots => Current.bots ?? DefaultBots.ToList();
    public static bool IsBot(string login) => login != null && (Bots.Contains(login.ToLowerInvariant()) || Current.customBots.Any(b => b.login == login.ToLowerInvariant()));
    public static Entry CustomBot(string login) => login == null ? null : Current.customBots.FirstOrDefault(b => b.login == login.ToLowerInvariant());

    static readonly string File = Path.Combine(Paths.ConfigPath, "shibiko.streamracer.settings.json");
    public static Model Current = Load();
    public static void Init() => Minimap.Configure(Current.minimap);

    static Model Load()
    {
        try { return JsonConvert.DeserializeObject<Model>(System.IO.File.ReadAllText(File)) ?? new Model(); }
        catch { return new Model(); }
    }

    public static void Persist() => System.IO.File.WriteAllText(File, JsonConvert.SerializeObject(Current, Formatting.Indented));

    public static void Save(string json)
    {
        // Merge onto the current model: a bot can PUT { customBots, botOptions } without wiping everything else.
        var m = JsonConvert.DeserializeObject<Model>(JsonConvert.SerializeObject(Current)) ?? new Model();
        JsonConvert.PopulateObject(string.IsNullOrWhiteSpace(json) ? "{}" : json, m, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Include });
        if (m.twitchToken == null) m.twitchToken = Current.twitchToken; // the page never sends the token back; keep it
        m.autoJoin = (m.autoJoin ?? new()).Where(e => !string.IsNullOrWhiteSpace(e.login)).ToList();
        m.ui ??= new JObject();
        m.overlay ??= new JObject();
        m.minimap ??= new JObject();
        Minimap.Configure(m.minimap);
        if (m.bots != null) m.bots = m.bots.Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0).Distinct().ToList();
        m.colors ??= new Dictionary<string, string>();
        m.perks ??= new Perks(); m.twitchToken ??= ""; m.twitchClientId ??= ""; m.botOptions ??= new Dictionary<string, BotOpts>(); m.webhooks ??= new List<Webhook>();
        m.customBots = (m.customBots ?? new()).Where(e => !string.IsNullOrWhiteSpace(e.login)).Select(e => { e.login = e.login.Trim().ToLowerInvariant(); e.id ??= ""; return e; }).ToList();
        Current = m;
        System.IO.File.WriteAllText(File, JsonConvert.SerializeObject(m, Formatting.Indented));
    }

    public static object ConfigDto() => new { port = Plugin.Port.Value, tokenRequired = !string.IsNullOrEmpty(Plugin.Token.Value), bindAll = Plugin.BindAll.Value, tickHz = Plugin.TickHz.Value, posHz = Plugin.PosHz.Value, hotkeyBoost = Plugin.BoostKey.Value.ToString(), camUp = Plugin.CamUp.Value.ToString(), camDown = Plugin.CamDown.Value.ToString() };

    public static object WithConfig() => new
    {
        Current.autoJoinStreamer, Current.streamerColor, Current.autoJoin, Current.customBots,
        Current.colorLeaderboard, Current.colorCommandEnabled, Current.colorCommand, Current.respawnCommandEnabled, Current.respawnCommand, Current.colors, Current.perks, Current.botOptions, Current.webhooks, Current.twitchClientId, twitchTokenSet = !string.IsNullOrEmpty(Current.twitchToken), Current.ui, Current.overlay, minimap = Minimap.State, bots = Bots,
        config = ConfigDto(),
    };

    // Runs after NewGame: wait for the lobby scene to actually be up, then add the streamer and the list.
    public static System.Collections.IEnumerator OnLobby()
    {
        // NewGame reloads the Play scene; the old lobby controller is still alive when we get here. Wait for a fresh
        // one (or, first time, for one to exist), then for the lobby to be up, then a beat for its lists to initialise.
        var old = InGameScreenController.NEPFAEJAMGI;
        for (float t = 0; t < 40f; t += 0.25f)
        {
            var cur = InGameScreenController.NEPFAEJAMGI;
            if (cur != null && !ReferenceEquals(cur, old) && Game.InLobby && !Game.Running) break;
            if (old == null && cur != null && Game.InLobby && !Game.Running && t > 2f) break;
            yield return new UnityEngine.WaitForSeconds(0.25f);
        }
        if (!Game.InLobby || Game.Running) yield break;
        yield return new UnityEngine.WaitForSeconds(1.5f);
        Plugin.Emit("lobby", Game.Snapshot()); // now joinable: bots listening on /events can POST /join
        if (Current.autoJoinStreamer) Game.JoinStreamer();
        JoinAll();
    }

    // Live edit of the BepInEx config (no restart). Returns what changed; a port/bind change restarts the HTTP server.
    public static object ApplyConfig(JObject o, out bool restart)
    {
        restart = false;
        var errors = new List<string>();
        UnityEngine.KeyCode Key(string s, UnityEngine.KeyCode cur) { if (s == null) return cur; if (System.Enum.TryParse<UnityEngine.KeyCode>(s.Trim(), true, out var k)) return k; errors.Add("unknown key: " + s); return cur; }
        if (o["port"] != null)
        {
            int p = (int)o["port"];
            if (p < 1024 || p > 65535) errors.Add("port must be 1024-65535");
            else if (p != Plugin.Port.Value) { Plugin.Port.Value = p; restart = true; }
        }
        if (o["bindAll"] != null && (bool)o["bindAll"] != Plugin.BindAll.Value) { Plugin.BindAll.Value = (bool)o["bindAll"]; restart = true; }
        if (o["token"] != null) { var t = (string)o["token"] ?? ""; if (t != Plugin.Token.Value) { Plugin.Token.Value = t; if (Plugin.BindAll.Value) restart = true; } }
        if (o["hotkeyBoost"] != null) Plugin.BoostKey.Value = Key((string)o["hotkeyBoost"], Plugin.BoostKey.Value);
        if (o["camUp"] != null) Plugin.CamUp.Value = Key((string)o["camUp"], Plugin.CamUp.Value);
        if (o["camDown"] != null) Plugin.CamDown.Value = Key((string)o["camDown"], Plugin.CamDown.Value);
        if (o["tickHz"] != null) Plugin.TickHz.Value = UnityEngine.Mathf.Clamp((float)o["tickHz"], 0.5f, 60f);
        if (o["posHz"] != null) Plugin.PosHz.Value = UnityEngine.Mathf.Clamp((float)o["posHz"], 0f, 120f);
        Plugin.Instance.Config.Save();
        return new { ok = errors.Count == 0, errors, restarting = restart, url = Plugin.BaseUrl, config = ConfigDto() };
    }

    public static int JoinAll() =>
        Current.autoJoin.Count(e => Game.Join(e.id, e.login.ToLowerInvariant(), e.displayName, e.color, e.sub, e.image));
}
