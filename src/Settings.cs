// Server-side settings so the control page behaves the same from any browser/OBS dock: the persisted model
// (shibiko.streamracer.settings.json), merge-on-PUT, the live BepInEx config edit, and the auto-join on lobby.
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

static class Settings
{
    public class Entry { public string id = ""; public string login = ""; public string displayName; public string color; public bool sub; public string image; }
    // Who gets what. Tiers: "everyone" | "follower" | "subscriber" | "off". Extra boosts stack (a subscribed follower gets both).
    public class BotOpts { public bool autoBoost = true; }
    public class Webhook { public string @event = "race_end"; public string url = ""; public string method = "POST"; public string header = ""; public string body = ""; public bool enabled = true; }
    public static bool AutoBoosts(string login) => login != null && (!Current.botOptions.TryGetValue(login.ToLowerInvariant(), out var options) || options.autoBoost);

    // Camera director: which shots it may pick. Missing key = on (Pure.ShotEnabled). Manual POST /camera/<shot> ignores this.
    public class CameraOpts { public Dictionary<string, bool> shots = DefaultShots(); }
    public static Dictionary<string, bool> DefaultShots() => Pure.ShotKeys.ToDictionary(key => key, _ => true);

    public class Perks
    {
        public string colorCommand = "follower";   // who may use the chat color command
        public string coloredNames = "everyone";   // whose name shows in color on the leaderboard (others white)
        public int boostFollower = 0, boostSubscriber = 0, boostDeveloper = 0, boostHost = 0; // extra boosts on join (all off: the game's creators asked mods not to hand out boosts)
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
        public bool leaderboardAvatars = true; // picture on each in-game leaderboard row
        public bool leaderboardPercent = true; // completion percent on each in-game leaderboard row
        public bool colorCommandEnabled = true; // viewers can set their own color from chat
        public bool respawnCommandEnabled = true; // viewers can respawn their own car from chat
        public int respawnLimit = 0;               // chat respawns per racer per race: 0 = off (default), -1 = unlimited
        public string respawnCommand = "!race respawn|!respawn"; // aliases separated by | or , (Pure.CommandAliases)
        public string showCommand = "!race show|!show";   // pops your name onto the mini map for a few seconds
        public string colorCommand = "!race color|!color";       // e.g. "!color #ff8800" or "!color red"
        public bool chatReplies = true; // confirm chat commands in Twitch chat through the game's own connection (Game.SayInChat)
        public CameraOpts camera = new(); // director shot toggles: camera.shots.{grid,high,side,sweep,pack,front,chase,orbit,overhead,prop,finish,duel,pileup,boom}
        public Dictionary<string, string> colors = new(); // login -> hex, persisted; applied whenever they join
        public Perks perks = new();
        public string twitchToken = "";    // optional: a token with moderator:read:followers (from your overlay/bot app) for follower checks
        public string twitchClientId = ""; // the client id that token belongs to
        public string twitchLogin = "", twitchUserId = ""; // who the mod's token belongs to (set by /twitch/auth)
        public List<string> twitchScopes = new();          // its scopes (moderator:read:followers, user:write:chat, …)
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
    public static bool IsBot(string login) => login != null && (Bots.Contains(login.ToLowerInvariant()) || Current.customBots.Any(bot => bot.login == login.ToLowerInvariant()));
    public static Entry CustomBot(string login) => login == null ? null : Current.customBots.FirstOrDefault(bot => bot.login == login.ToLowerInvariant());
    public static List<string> RespawnCommands => Pure.CommandAliases(Current.respawnCommand);
    public static List<string> ShowCommands => Pure.CommandAliases(Current.showCommand);
    public static List<string> ColorCommands => Pure.CommandAliases(Current.colorCommand);
    public static Dictionary<string, bool> Shots => Current.camera?.shots;

    static readonly string FilePath = Path.Combine(Paths.ConfigPath, "shibiko.streamracer.settings.json");
    public static Model Current = Load();
    public static void Init() => Minimap.Configure(Current.minimap);

    static Model Load()
    {
        Model model;
        try { model = JsonConvert.DeserializeObject<Model>(File.ReadAllText(FilePath)) ?? new Model(); }
        catch { model = new Model(); }
        return Upgrade(model);
    }

    // Settings files written before the alias lists hold the old single commands: give them the short aliases too.
    static Model Upgrade(Model model)
    {
        if ((model.respawnCommand ?? "").Trim() == "!race respawn") model.respawnCommand = "!race respawn|!respawn";
        if ((model.colorCommand ?? "").Trim() == "!race color") model.colorCommand = "!race color|!color";
        model.camera ??= new CameraOpts(); model.camera.shots ??= DefaultShots();
        return model;
    }

    public static void Persist() => File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));

    public static void Save(string json)
    {
        // Merge onto the current model: a bot can PUT { customBots, botOptions } without wiping everything else.
        var model = JsonConvert.DeserializeObject<Model>(JsonConvert.SerializeObject(Current)) ?? new Model();
        JsonConvert.PopulateObject(string.IsNullOrWhiteSpace(json) ? "{}" : json, model, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Include });
        if (model.twitchToken == null) model.twitchToken = Current.twitchToken; // the page never sends the token back; keep it
        model.twitchLogin ??= Current.twitchLogin; model.twitchUserId ??= Current.twitchUserId; model.twitchScopes ??= Current.twitchScopes ?? new List<string>();
        if (model.twitchToken != Current.twitchToken && model.twitchToken == "") { model.twitchLogin = ""; model.twitchUserId = ""; model.twitchScopes = new List<string>(); }
        model.autoJoin = (model.autoJoin ?? new()).Where(entry => !string.IsNullOrWhiteSpace(entry.login)).ToList();
        model.ui ??= new JObject();
        model.overlay ??= new JObject();
        model.minimap ??= new JObject();
        Minimap.Configure(model.minimap);
        if (model.bots != null) model.bots = model.bots.Select(login => login.Trim().ToLowerInvariant()).Where(login => login.Length > 0).Distinct().ToList();
        model.colors ??= new Dictionary<string, string>();
        model.perks ??= new Perks(); model.twitchToken ??= ""; model.twitchClientId ??= ""; model.botOptions ??= new Dictionary<string, BotOpts>(); model.webhooks ??= new List<Webhook>();
        model.twitchToken = Pure.CleanToken(model.twitchToken); model.twitchClientId = (model.twitchClientId ?? "").Trim();
        if (model.twitchToken != Current.twitchToken || model.twitchClientId != Current.twitchClientId) Game.ForgetFollowers(); // a new token: redo the lookups
        if (string.IsNullOrWhiteSpace(model.respawnCommand)) model.respawnCommand = "!race respawn|!respawn";
        if (string.IsNullOrWhiteSpace(model.showCommand)) model.showCommand = "!race show|!show";
        if (string.IsNullOrWhiteSpace(model.colorCommand)) model.colorCommand = "!race color|!color";
        Upgrade(model);
        foreach (var key in Pure.ShotKeys) if (!model.camera.shots.ContainsKey(key)) model.camera.shots[key] = true; // a partial PUT never switches shots off by accident
        model.customBots = (model.customBots ?? new()).Where(entry => !string.IsNullOrWhiteSpace(entry.login)).Select(entry => { entry.login = entry.login.Trim().ToLowerInvariant(); entry.id ??= ""; return entry; }).ToList();
        Current = model;
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(model, Formatting.Indented));
    }

    public static object ConfigDto() => new { port = Plugin.Port.Value, tokenRequired = !string.IsNullOrEmpty(Plugin.Token.Value), bindAll = Plugin.BindAll.Value, tickHz = Plugin.TickHz.Value, posHz = Plugin.PosHz.Value, hotkeyBoost = Plugin.BoostKey.Value.ToString(), camUp = Plugin.CamUp.Value.ToString(), camDown = Plugin.CamDown.Value.ToString() };

    public static object WithConfig() => new
    {
        Current.autoJoinStreamer, Current.streamerColor, Current.autoJoin, Current.customBots,
        Current.colorLeaderboard, Current.leaderboardAvatars, Current.leaderboardPercent, Current.colorCommandEnabled, Current.colorCommand, Current.respawnCommandEnabled, Current.respawnCommand, Current.showCommand, Current.respawnLimit, Current.chatReplies, Current.colors, Current.perks, Current.botOptions, Current.webhooks, Current.twitchClientId, twitchTokenSet = !string.IsNullOrEmpty(Current.twitchToken), twitch = TwitchAuth.Status(),
        followerChecks = Game.FollowerChecks, followerChecksError = Game.FollowerCheckError, camera = Current.camera, Current.ui, Current.overlay, minimap = Minimap.State, bots = Bots,
        config = ConfigDto(),
    };

    // Runs after NewGame: wait for the lobby scene to actually be up, then add the streamer and the list.
    public static System.Collections.IEnumerator OnLobby()
    {
        // NewGame reloads the Play scene; the old lobby controller is still alive when we get here. Wait for a fresh
        // one (or, first time, for one to exist), then for the lobby to be up, then a beat for its lists to initialise.
        var oldScreen = Instances.InGameScreen;
        for (float waited = 0; waited < 40f; waited += 0.25f)
        {
            var screen = Instances.InGameScreen;
            if (screen != null && !ReferenceEquals(screen, oldScreen) && Game.InLobby && !Game.Running) break;
            if (oldScreen == null && screen != null && Game.InLobby && !Game.Running && waited > 2f) break;
            yield return new WaitForSeconds(0.25f);
        }
        if (!Game.InLobby || Game.Running) yield break;
        yield return new WaitForSeconds(1.5f);
        Plugin.Emit("lobby", Game.Snapshot()); // now joinable: bots listening on /events can POST /join
        if (Current.autoJoinStreamer) Game.JoinStreamer();
        JoinAll();
    }

    // Live edit of the BepInEx config (no restart). Returns what changed; a port/bind change restarts the HTTP server.
    public static object ApplyConfig(JObject config, out bool restart)
    {
        restart = false;
        var errors = new List<string>();
        KeyCode Key(string name, KeyCode current)
        {
            if (name == null) return current;
            if (System.Enum.TryParse<KeyCode>(name.Trim(), true, out var key)) return key;
            errors.Add("unknown key: " + name); return current;
        }
        if (config["port"] != null)
        {
            int port = (int)config["port"];
            if (port < 1024 || port > 65535) errors.Add("port must be 1024-65535");
            else if (port != Plugin.Port.Value) { Plugin.Port.Value = port; restart = true; }
        }
        if (config["bindAll"] != null && (bool)config["bindAll"] != Plugin.BindAll.Value) { Plugin.BindAll.Value = (bool)config["bindAll"]; restart = true; }
        if (config["token"] != null) { var token = (string)config["token"] ?? ""; if (token != Plugin.Token.Value) { Plugin.Token.Value = token; if (Plugin.BindAll.Value) restart = true; } }
        if (config["hotkeyBoost"] != null) Plugin.BoostKey.Value = Key((string)config["hotkeyBoost"], Plugin.BoostKey.Value);
        if (config["camUp"] != null) Plugin.CamUp.Value = Key((string)config["camUp"], Plugin.CamUp.Value);
        if (config["camDown"] != null) Plugin.CamDown.Value = Key((string)config["camDown"], Plugin.CamDown.Value);
        if (config["tickHz"] != null) Plugin.TickHz.Value = Mathf.Clamp((float)config["tickHz"], 0.5f, 60f);
        if (config["posHz"] != null) Plugin.PosHz.Value = Mathf.Clamp((float)config["posHz"], 0f, 120f);
        Plugin.Instance.Config.Save();
        return new { ok = errors.Count == 0, errors, restarting = restart, url = Plugin.BaseUrl, config = ConfigDto() };
    }

    public static int JoinAll() =>
        Current.autoJoin.Count(entry => Game.Join(entry.id, entry.login.ToLowerInvariant(), entry.displayName, entry.color, entry.sub, entry.image));
}
