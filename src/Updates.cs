// Latest-version lookup: fetches api.UpdateUrl (JSON {version, url}) at most once an hour, off the main thread.
// ponytail: no auto-install; the DLL is locked while the game runs, so "update" means download + swap on restart.
using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

public static class Updates
{
    public static string Latest, Url; static DateTime _checkedAt = DateTime.MinValue; static int _busy;
    public static bool? UpToDate => Latest == null ? null : (bool?)(Pure.CompareVersions(Plugin.Version, Latest) >= 0);
    public static void Kick()
    {
        string updateUrl = Plugin.UpdateUrl?.Value;
        if (string.IsNullOrWhiteSpace(updateUrl) || DateTime.UtcNow - _checkedAt < TimeSpan.FromHours(1) || Interlocked.Exchange(ref _busy, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var web = new WebClient(); web.Headers["User-Agent"] = "StreamRacerApi/" + Plugin.Version;
                var release = JObject.Parse(web.DownloadString(updateUrl));
                Latest = (string)release["version"]; Url = (string)release["url"]; _checkedAt = DateTime.UtcNow;
            }
            catch (Exception error) { Plugin.Log.LogWarning("update check: " + error.Message); _checkedAt = DateTime.UtcNow; }
            finally { _busy = 0; }
        });
    }
}
