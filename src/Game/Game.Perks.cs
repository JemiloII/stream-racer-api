// Game.Perks: viewer tiers (follower / subscriber / developer / host), the Helix follower lookup with its cache,
// the extra-boost grant on join and the GET /perks/:login diagnostics.
using System.Net;
using System.Threading;

namespace StreamRacerApi;

static partial class Game
{
    // Tiers pushed by an outside bot (voisona-bot) that holds the Twitch scopes the game lacks: PUT /tier/:login.
    // They win over the game's own flag / the mod's Helix lookup, and a grant is re-run so the extra boosts land.
    public class ExternalTier { public bool? follower, subscriber; public string source; public System.DateTime at; }
    static readonly Dictionary<string, ExternalTier> _externalTiers = new();
    public static ExternalTier ExternalTierOf(string login) => login != null && _externalTiers.TryGetValue(login.ToLowerInvariant(), out var tier) ? tier : null;
    public static bool AnyExternalTiers => _externalTiers.Count > 0;
    public static object SetExternalTier(string login, bool? follower, bool? subscriber, string source)
    {
        login = (login ?? "").ToLowerInvariant();
        var tier = ExternalTierOf(login) ?? new ExternalTier();
        if (follower != null) tier.follower = follower;
        if (subscriber != null) tier.subscriber = subscriber;
        tier.source = source ?? "bot"; tier.at = System.DateTime.UtcNow;
        _externalTiers[login] = tier;
        var vehicle = Find(login);
        if (vehicle != null) GrantPerks(vehicle); // idempotent: only the difference is added
        Plugin.Emit("tier", new { login, tier.follower, tier.subscriber, tier.source, inRace = vehicle != null });
        return new { login, tier.follower, tier.subscriber, tier.source, inRace = vehicle != null, granted = vehicle != null ? GrantedExtra(vehicle) : 0 };
    }
    public static void ForgetExternalTiers() => _externalTiers.Clear();

    public static bool IsSub(Vehicle vehicle) => ExternalTierOf(Login(vehicle))?.subscriber ?? vehicle.Profile().IsSubscriber();
    public static bool IsDev(Vehicle vehicle) => (Title(vehicle) ?? "").IndexOf("developer", System.StringComparison.OrdinalIgnoreCase) >= 0;
    public static bool IsHost(Vehicle vehicle) => Login(vehicle) != null && Login(vehicle) == StreamerLogin;

    // Follower checks need a token with moderator:read:followers (the game's own token lacks it): Settings -> Perks, pasted
    // into settings.twitchToken + twitchClientId. Without one, followers are simply never detected (status "no token").
    // Successful lookups are cached per login for the session; failures are not (a 401 must not brand someone a non-follower).
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _followers = new();
    static readonly Dictionary<string, List<System.Action<bool>>> _followPending = new(); // login -> callbacks waiting on one lookup
    public static string FollowerCheckError; // last Helix failure, shown in /settings and /perks/:login
    public static bool FollowerKnown(string login) => _followers.ContainsKey((login ?? "").ToLowerInvariant());
    public static bool IsFollower(string login) => ExternalTierOf(login)?.follower ?? (login != null && _followers.TryGetValue(login.ToLowerInvariant(), out var follows) && follows);
    public static bool FollowerChecksAvailable => !string.IsNullOrEmpty(Settings.Current.twitchToken) && !string.IsNullOrEmpty(Settings.Current.twitchClientId) && !string.IsNullOrEmpty(StreamerId);
    public static string FollowerChecks => AnyExternalTiers ? "bot" : Pure.FollowerCheckStatus(!string.IsNullOrEmpty(Settings.Current.twitchToken), !string.IsNullOrEmpty(Settings.Current.twitchClientId), !string.IsNullOrEmpty(StreamerId), FollowerCheckError);
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
        string token = Settings.Current.twitchToken, clientId = Settings.Current.twitchClientId, broadcasterId = StreamerId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool follows = false, lookedUp = false;
            try
            {
                var web = new WebClient(); web.Headers["Authorization"] = "Bearer " + token; web.Headers["Client-Id"] = clientId;
                string resolvedId = userId;
                if (string.IsNullOrEmpty(resolvedId))
                {
                    var users = Newtonsoft.Json.Linq.JObject.Parse(web.DownloadString("https://api.twitch.tv/helix/users?login=" + System.Uri.EscapeDataString(login)))["data"];
                    resolvedId = users != null && users.HasValues ? (string)users[0]["id"] : null;
                }
                if (!string.IsNullOrEmpty(resolvedId))
                {
                    var followers = Newtonsoft.Json.Linq.JObject.Parse(web.DownloadString($"https://api.twitch.tv/helix/channels/followers?broadcaster_id={broadcasterId}&user_id={resolvedId}"))["data"];
                    follows = followers != null && followers.HasValues;
                }
                lookedUp = true; FollowerCheckError = null;
            }
            catch (WebException error)
            {
                var code = (error.Response as HttpWebResponse)?.StatusCode;
                FollowerCheckError = code == HttpStatusCode.Unauthorized ? "401: token rejected (needs moderator:read:followers for this channel; the client id must be the token's)" : code != null ? (int)code + ": " + error.Message : error.Message;
                Plugin.Log.LogWarning("follower check failed for " + login + ": " + FollowerCheckError);
            }
            catch (System.Exception error) { FollowerCheckError = error.Message; Plugin.Log.LogWarning("follower check failed for " + login + ": " + error.Message); }
            if (lookedUp) _followers[login] = follows;
            List<System.Action<bool>> callbacks;
            lock (_followPending) { _followPending.TryGetValue(login, out callbacks); _followPending.Remove(login); }
            Plugin.RunOnMain(() => { if (callbacks != null) foreach (var callback in callbacks) callback(follows); });
        });
    }

    static bool TierAllows(string tier, Vehicle vehicle, string login) =>
        Pure.TierAllows(tier, IsFollower(login), vehicle != null && IsSub(vehicle), vehicle != null && IsDev(vehicle), vehicle != null && IsHost(vehicle));
    public static bool MayUseColorCommand(string login) => TierAllows(Settings.Current.perks.colorCommand, Find(login), login);
    public static bool MayShowColoredName(Vehicle vehicle) => TierAllows(Settings.Current.perks.coloredNames, vehicle, Login(vehicle));

    // Extra boosts on join: follower + subscriber + developer + host, stacking (Pure.ExtraBoosts). Runs once the backend
    // title is in. Sub/dev/host are known at once; the follower part waits for the Helix lookup, then the whole grant applies.
    static readonly Dictionary<Vehicle, int> _granted = new(); // extra boosts already handed to this car this race
    public static void ForgetPerks() => _granted.Clear();
    public static int GrantedExtra(Vehicle vehicle) => _granted.TryGetValue(vehicle, out var extra) ? extra : 0;
    // Idempotent: works out what the car should have and adds only what it hasn't got yet, so a tier that arrives
    // later (the bot's PUT /tier, a follower lookup finishing) tops the pool up instead of double-granting.
    public static void GrantPerks(Vehicle vehicle)
    {
        if (vehicle == null) return;
        var perks = Settings.Current.perks;
        void Apply()
        {
            if (!Vehicles().Contains(vehicle)) return;
            var (extra, reasons) = Pure.ExtraBoosts(perks.boostFollower, perks.boostSubscriber, perks.boostDeveloper, perks.boostHost, IsFollower(Login(vehicle)), IsSub(vehicle), IsDev(vehicle), IsHost(vehicle));
            int already = GrantedExtra(vehicle), delta = extra - already;
            if (delta > 0) { AddBoosts(vehicle, delta); _granted[vehicle] = extra; }
            else if (!_granted.ContainsKey(vehicle)) _granted[vehicle] = 0;
            if (delta > 0 || already == 0) Plugin.Emit("perk", new { login = Login(vehicle), displayName = DisplayName(vehicle), extraBoosts = extra, added = System.Math.Max(0, delta), reasons, boosts = Boosts(vehicle), followerChecks = FollowerChecks, follower = IsFollower(Login(vehicle)) });
        }
        bool tierKnown = ExternalTierOf(Login(vehicle))?.follower != null;
        if (!tierKnown && perks.boostFollower != 0 && FollowerChecksAvailable && !FollowerKnown(Login(vehicle))) CheckFollower(Login(vehicle), vehicle.Profile().TwitchId(), _ => Apply());
        else Apply();
    }

    // GET /perks/:login: what someone gets and why (or why not). Unknown follower status kicks off a lookup; ask again.
    public static object PerksDto(string login)
    {
        login = (login ?? "").ToLowerInvariant();
        var vehicle = Find(login); var perks = Settings.Current.perks;
        bool subscriber = vehicle != null && IsSub(vehicle), developer = vehicle != null && IsDev(vehicle), host = login == (StreamerLogin ?? "").ToLowerInvariant();
        var external = ExternalTierOf(login);
        if (perks.boostFollower != 0 && FollowerChecksAvailable && !FollowerKnown(login)) CheckFollower(login, vehicle?.Profile().TwitchId());
        var (extra, why) = Pure.ExtraBoosts(perks.boostFollower, perks.boostSubscriber, perks.boostDeveloper, perks.boostHost, IsFollower(login), subscriber, developer, host);
        string status = FollowerChecks;
        if (perks.boostFollower == 0) why.Add("follower boost is 0 in Settings -> Perks");
        else if (status == "no token") why.Add("follower boost needs a Twitch token with moderator:read:followers + its client id (Settings -> Perks)");
        else if (status == "unknown") why.Add("follower lookup failed: " + (FollowerCheckError ?? "streamer not logged in"));
        else if (!FollowerKnown(login)) why.Add("follower lookup pending: ask again in a second");
        else if (!IsFollower(login)) why.Add("not a follower");
        return new
        {
            login, inRace = vehicle != null, follower = IsFollower(login), followerKnown = FollowerKnown(login), subscriber, developer, host,
            extraBoosts = extra, why, followerChecks = status, followerChecksError = FollowerCheckError,
            granted = vehicle != null ? GrantedExtra(vehicle) : 0, source = external?.source, boosts = vehicle != null ? (int?)Boosts(vehicle) : null, perks,
        };
    }
}
