// Game.Chat: Twitch chat. Sending through the game's own TwitchLib client (or Helix when the mod has its own token),
// the token-scope check behind GET /chat, and the viewer commands (!respawn, !color) with their replies.
using System.Threading;
using HarmonyLib;

namespace StreamRacerApi;

static partial class Game
{
    // The game's TwitchLib client is logged in as the streamer with the game token. Sending needs chat:edit on that
    // token; Twitch drops the message silently without it, so GET /chat reports the scopes.
    static readonly System.Reflection.FieldInfo ChatClientField = AccessTools.Field(typeof(TwitchCommandListener), GameNames.ChatClient);
    static TwitchLib.Client.Interfaces.ITwitchClient ChatClient =>
        Instances.TwitchListener == null ? null : ChatClientField?.GetValue(Instances.TwitchListener) as TwitchLib.Client.Interfaces.ITwitchClient;
    public static string ChatChannel
    {
        get { try { var client = ChatClient; return client != null && client.JoinedChannels.Count > 0 ? client.JoinedChannels[0].Channel : StreamerLogin; } catch { return StreamerLogin; } }
    }
    public static bool ChatConnected { get { try { return ChatClient?.IsConnected == true; } catch { return false; } } }

    public static bool SayInChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TwitchAuth.SendChat(text)) { Plugin.Emit("chat", new { channel = StreamerLogin, text, via = "helix" }); return true; }
        try
        {
            var client = ChatClient; if (client == null || !client.IsConnected) return false;
            string channel = ChatChannel; if (string.IsNullOrEmpty(channel)) return false;
            client.SendMessage(channel, text.Length > 480 ? text.Substring(0, 480) : text);
            Plugin.Emit("chat", new { channel, text });
            return true;
        }
        catch (System.Exception error) { Plugin.Log.LogWarning("chat send failed: " + error.Message); return false; }
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
                    var web = new System.Net.WebClient(); web.Headers["Authorization"] = "OAuth " + token;
                    var validation = Newtonsoft.Json.Linq.JObject.Parse(web.DownloadString("https://id.twitch.tv/oauth2/validate"));
                    _scopes = validation["scopes"]?.Select(scope => (string)scope).ToList() ?? new List<string>(); _scopesError = null;
                }
                catch (System.Exception error) { _scopesError = error.Message; }
                _scopesAt = System.DateTime.UtcNow; _scopesBusy = false; // no Unity API off the main thread
            });
        }
        bool? canSend = TwitchAuth.Has(TwitchAuth.ChatScope) ? true : _scopes == null ? (bool?)null : _scopes.Contains("chat:edit");
        return new
        {
            connected = ChatConnected, channel = ChatChannel, login = StreamerLogin, replies = Settings.Current.chatReplies, canSend, scopes = _scopes, scopesError = _scopesError,
            helix = TwitchAuth.Has(TwitchAuth.ChatScope), twitch = TwitchAuth.Status(),
            note = canSend == false ? "the game's token has no chat:edit: connect Twitch on the Settings page (user:write:chat) or relay the color/denied SSE events from your bot" : canSend == null ? "scopes not known yet: ask again" : null,
        };
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
            if (Respawn(mine)) NoteChatRespawn(login); // Respawn emits the respawn event itself
            return;
        }
        if (!Settings.Current.colorCommandEnabled) return;
        var commands = Settings.ColorCommands; string command = commands.Count > 0 ? commands[0] : "!race color";
        string argument = Pure.CommandArg(message, commands);
        if (string.IsNullOrEmpty(argument)) return;
        var tier = Settings.Current.perks.colorCommand;
        void Decide()
        {
            if (!MayUseColorCommand(login)) { Plugin.Emit("denied", new { login, displayName, command, tier, followerChecks = FollowerChecks }); Reply(Pure.ColorDeniedReply(displayName, tier)); return; }
            if (!SetColor(login, argument)) return;
            string hex = Settings.Current.colors[login];
            Plugin.Emit("color", new { login, displayName, color = hex }); Reply(Pure.ColorSetReply(displayName, hex));
        }
        if (tier == "follower" && FollowerChecksAvailable && !FollowerKnown(login) && Find(login) == null) CheckFollower(login, null, _ => Decide()); // look it up, then decide
        else Decide();
    }
}
