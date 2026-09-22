// The mod's own Twitch login. The game's token has only chat:read + channel_check_subscription, so it can't check
// followers or post replies. This is an OAuth implicit-grant flow through the local server, using the streamer's
// Twitch app (client id from Settings). The token lands in settings.twitchToken with its scopes; follower checks and
// Helix chat replies use it.
//
//   GET  /twitch/auth      -> 302 to id.twitch.tv (needs settings.twitchClientId; add http://localhost:<port>/twitch/callback
//                            to the app's OAuth Redirect URLs first)
//   GET  /twitch/callback  -> tiny page that reads #access_token from the fragment and POSTs it to /twitch/token
//   POST /twitch/token     -> {token, scope} validate + store; GET -> status {login, userId, scopes, features}
//   DELETE /twitch/token   -> forget
using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

public static class TwitchAuth
{
    public static readonly string[] Scopes = { "moderator:read:followers", "user:write:chat", "user:read:chat", "channel:read:subscriptions" };
    public const string ChatScope = "user:write:chat", FollowScope = "moderator:read:followers";

    public static string RedirectUri => $"http://localhost:{Plugin.Port.Value}/twitch/callback";
    public static string AuthorizeUrl(string clientId, string state) =>
        "https://id.twitch.tv/oauth2/authorize?response_type=token&client_id=" + Uri.EscapeDataString(clientId) +
        "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&scope=" + Uri.EscapeDataString(string.Join(" ", Scopes)) +
        "&state=" + Uri.EscapeDataString(state) + "&force_verify=true";

    static string _state;
    public static string NewState() => _state = Guid.NewGuid().ToString("N");

    // Validate against id.twitch.tv and remember login/user id/scopes alongside the token. Off the main thread.
    public static JObject Store(string token)
    {
        token = Pure.CleanToken(token);
        JObject result;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using var web = new WebClient();
            web.Headers["Authorization"] = "OAuth " + token;
            var validation = JObject.Parse(web.DownloadString("https://id.twitch.tv/oauth2/validate"));
            var settings = Settings.Current;
            settings.twitchToken = token; settings.twitchClientId = (string)validation["client_id"] ?? settings.twitchClientId;
            settings.twitchLogin = (string)validation["login"]; settings.twitchUserId = (string)validation["user_id"];
            settings.twitchScopes = validation["scopes"]?.Select(scope => (string)scope).ToList() ?? new List<string>();
            Settings.Persist(); Game.ForgetFollowers();
            result = Status();
        }
        catch (Exception error) { result = new JObject { ["error"] = "token rejected: " + error.Message }; }
        Plugin.RunOnMain(() => Plugin.Emit("settings", Settings.WithConfig()));
        return result;
    }

    public static void Forget()
    {
        var settings = Settings.Current; settings.twitchToken = ""; settings.twitchLogin = ""; settings.twitchUserId = ""; settings.twitchScopes = new List<string>();
        Settings.Persist(); Game.ForgetFollowers(); Plugin.Emit("settings", Settings.WithConfig());
    }

    public static bool Has(string scope) => !string.IsNullOrEmpty(Settings.Current.twitchToken) && (Settings.Current.twitchScopes?.Contains(scope) ?? false);

    public static JObject Status()
    {
        var settings = Settings.Current;
        return JObject.FromObject(new
        {
            connected = !string.IsNullOrEmpty(settings.twitchToken), login = settings.twitchLogin, userId = settings.twitchUserId, clientId = settings.twitchClientId,
            scopes = settings.twitchScopes ?? new List<string>(), wanted = Scopes, redirectUri = RedirectUri,
            features = new { followerChecks = Has(FollowScope), chatReplies = Has(ChatScope) },
            missing = Scopes.Where(scope => !Has(scope)).ToList(),
        });
    }

    // Helix "send chat message" as the logged-in user into the streamer's channel.
    public static bool SendChat(string text)
    {
        var settings = Settings.Current; string broadcaster = Game.StreamerId ?? settings.twitchUserId;
        if (!Has(ChatScope) || string.IsNullOrEmpty(broadcaster) || string.IsNullOrEmpty(settings.twitchUserId)) return false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var web = new WebClient();
                web.Headers["Authorization"] = "Bearer " + settings.twitchToken; web.Headers["Client-Id"] = settings.twitchClientId; web.Headers["Content-Type"] = "application/json";
                web.UploadString("https://api.twitch.tv/helix/chat/messages", JsonConvert.SerializeObject(new { broadcaster_id = broadcaster, sender_id = settings.twitchUserId, message = text.Length > 480 ? text.Substring(0, 480) : text }));
            }
            catch (Exception error) { Plugin.Log.LogWarning("helix chat send failed: " + error.Message); }
        });
        return true;
    }

    // The callback page: the token arrives in the URL fragment, which only the browser can see.
    public const string CallbackHtml = @"<!doctype html><meta charset=utf-8><title>Stream Racer · Twitch</title>
<style>body{font:15px system-ui;background:#0b0d10;color:#eef0f2;display:grid;place-items:center;height:100vh;margin:0}p{max-width:520px;text-align:center}</style>
<p id=m>Connecting…</p>
<script>
const h=new URLSearchParams(location.hash.slice(1));const m=document.getElementById('m');
if(!h.get('access_token')){m.textContent='Twitch said: '+(h.get('error_description')||h.get('error')||'no token');}
else fetch('/twitch/token',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token:h.get('access_token'),state:h.get('state')})})
 .then(r=>r.json()).then(j=>{m.innerHTML=j.error?('Failed: '+j.error):('Connected as <b>'+j.login+'</b> with '+j.scopes.join(', ')+'.<br>You can close this tab.');history.replaceState(null,'',location.pathname);})
 .catch(e=>m.textContent='Failed: '+e);
</script>";

    public static bool StateOk(string state) => !string.IsNullOrEmpty(_state) && state == _state;
}
