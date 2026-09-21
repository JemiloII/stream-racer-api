using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

// Latest-version lookup: fetches api.UpdateUrl (JSON {version, url}) at most once an hour, off the main thread.
// ponytail: no auto-install; the DLL is locked while the game runs, so "update" means download + swap on restart.
public static class Updates
{
    public static string Latest, Url; static DateTime _checked = DateTime.MinValue; static int _busy;
    public static bool? UpToDate => Latest == null ? null : (bool?)(CompareVersions(Plugin.Version, Latest) >= 0);
    public static void Kick()
    {
        string u = Plugin.UpdateUrl?.Value; if (string.IsNullOrWhiteSpace(u) || DateTime.UtcNow - _checked < TimeSpan.FromHours(1) || Interlocked.Exchange(ref _busy, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var wc = new WebClient(); wc.Headers["User-Agent"] = "StreamRacerApi/" + Plugin.Version;
                var j = JObject.Parse(wc.DownloadString(u));
                Latest = (string)j["version"]; Url = (string)j["url"]; _checked = DateTime.UtcNow;
            }
            catch (Exception e) { Plugin.Log.LogWarning("update check: " + e.Message); _checked = DateTime.UtcNow; }
            finally { _busy = 0; }
        });
    }
    static int CompareVersions(string a, string b)
    {
        var pa = (a ?? "0").Split('.'); var pb = (b ?? "0").Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length && int.TryParse(pa[i], out var vx) ? vx : 0, y = i < pb.Length && int.TryParse(pb[i], out var vy) ? vy : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
