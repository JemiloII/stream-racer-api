// Game.Images: pictures. Twitch avatars resolved through Helix for the overlays, custom join images (URL or local
// file) served by the plugin and swapped into the lobby/results rows in place of the game's avatar loader.
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine.UI;

namespace StreamRacerApi;

static partial class Game
{
    // ---- Twitch avatars ----

    // Twitch profile pictures per login, resolved in the background via Helix so overlays get a picture.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Avatars = new();
    static readonly HashSet<string> AvatarPending = new();

    public static void EnsureAvatars()
    {
        var needed = Vehicles().Select(Login).Where(login => !string.IsNullOrEmpty(login) && !Avatars.ContainsKey(login) && !AvatarPending.Contains(login)).Distinct().Take(100).ToList();
        if (needed.Count == 0 || string.IsNullOrEmpty(TwitchToken)) return;
        foreach (var login in needed) AvatarPending.Add(login);
        string token = TwitchToken, clientId = TwitchClientId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var web = new WebClient();
                web.Headers["Authorization"] = "Bearer " + token; web.Headers["Client-Id"] = clientId;
                string url = "https://api.twitch.tv/helix/users?" + string.Join("&", needed.Select(login => "login=" + System.Uri.EscapeDataString(login)));
                var users = Newtonsoft.Json.Linq.JObject.Parse(web.DownloadString(url))["data"];
                foreach (var user in users) Avatars[(string)user["login"]] = (string)user["profile_image_url"];
                foreach (var login in needed) Avatars.TryAdd(login, null); // not on twitch -> remember that too
            }
            catch (System.Exception error) { Plugin.Log.LogWarning("avatar lookup failed: " + error.Message); }
            lock (AvatarPending) foreach (var login in needed) AvatarPending.Remove(login);
        });
    }

    // Custom images are served by the plugin (they may be local files), Twitch avatars are direct CDN urls.
    public static string Avatar(Vehicle vehicle) =>
        ImageSource(Login(vehicle)) != null ? "/image/" + Login(vehicle) : Avatars.TryGetValue(Login(vehicle) ?? "", out var url) ? url : null;

    // ---- custom images ----

    // Optional per-login picture (URL or local path) shown in the lobby/results lists instead of the Twitch avatar.
    public static readonly Dictionary<string, string> Images = new();
    static readonly Dictionary<string, byte[]> ImageCache = new();

    // Where a login's picture comes from: a live join, a custom bot, or an auto-join entry.
    public static string ImageSource(string login)
    {
        if (login == null) return null;
        if (Images.TryGetValue(login, out var source) && !string.IsNullOrEmpty(source)) return source;
        var customBot = Settings.CustomBot(login); if (!string.IsNullOrEmpty(customBot?.image)) return customBot.image;
        var autoJoinEntry = Settings.Current.autoJoin.FirstOrDefault(entry => entry.login == login.ToLowerInvariant()); if (!string.IsNullOrEmpty(autoJoinEntry?.image)) return autoJoinEntry.image;
        return null;
    }

    public static string ImageDir => Path.Combine(BepInEx.Paths.ConfigPath, "shibiko.streamracer.images");

    // Save an uploaded picture (data URL) for a login; returns the file path to store in settings.
    public static string SaveImage(string login, string dataUrl)
    {
        var match = System.Text.RegularExpressions.Regex.Match(dataUrl ?? "", @"^data:image/(png|jpe?g|gif|webp);base64,(.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!match.Success) return null;
        Directory.CreateDirectory(ImageDir);
        string extension = match.Groups[1].Value.ToLowerInvariant() == "jpeg" ? "jpg" : match.Groups[1].Value.ToLowerInvariant();
        string safeLogin = System.Text.RegularExpressions.Regex.Replace(login.ToLowerInvariant(), "[^a-z0-9_]", "");
        string path = Path.Combine(ImageDir, safeLogin + "." + extension);
        File.WriteAllBytes(path, System.Convert.FromBase64String(match.Groups[2].Value));
        ImageCache.Remove(path);
        return path.Replace('\\', '/');
    }

    // Bytes of a custom image (file or url), cached. Null if none / failed.
    public static byte[] CustomImageBytes(string login)
    {
        var source = ImageSource(login);
        if (source == null) return null;
        if (ImageCache.TryGetValue(source, out var bytes)) return bytes;
        try
        {
            if (File.Exists(source)) bytes = File.ReadAllBytes(source);
            else { var web = new WebClient(); web.Headers["User-Agent"] = "StreamRacerApi"; bytes = web.DownloadData(source); }
            ImageCache[source] = bytes; return bytes;
        }
        catch { return null; }
    }

    // ---- lobby / results rows ----

    // RawImage -> custom source, registered before the game's own avatar loader runs so a prefix can skip it.
    static readonly Dictionary<RawImage, string> RowImages = new();

    public static void RegisterRowImage(RacerProfile profile, RawImage target)
    {
        if (profile == null || target == null) return;
        if (Images.TryGetValue(profile.Login() ?? "", out var source)) RowImages[target] = source; else RowImages.Remove(target);
    }

    public static bool HasCustomImage(RawImage target) => target != null && RowImages.ContainsKey(target);

    public static void ApplyImage(RawImage target, MonoBehaviour host)
    {
        if (target == null || !RowImages.TryGetValue(target, out var source)) return;
        host.StartCoroutine(LoadImage(source, target));
    }

    static IEnumerator LoadImage(string source, RawImage target)
    {
        if (!ImageCache.TryGetValue(source, out var bytes))
        {
            byte[] loaded = null; var done = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (File.Exists(source)) loaded = File.ReadAllBytes(source);
                    else { var web = new WebClient(); web.Headers["User-Agent"] = "StreamRacerApi"; loaded = web.DownloadData(source); }
                }
                catch (System.Exception error) { Plugin.Log.LogWarning($"image load failed for {source}: {error.Message}"); }
                done.Set();
            });
            while (!done.IsSet) yield return null;
            if (loaded == null) yield break;
            ImageCache[source] = bytes = loaded;
        }
        if (target == null) yield break;
        var texture = new Texture2D(2, 2);
        if (!texture.LoadImage(bytes)) { Plugin.Log.LogWarning($"image decode failed for {source}"); yield break; }
        target.texture = texture;
        target.enabled = true;
    }
}
