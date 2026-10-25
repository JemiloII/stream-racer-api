// Latest-version lookup, at most once an hour, off the main thread: GitHub's latest release of this repo (tag v<version>,
// the release zip as an asset), or api.UpdateUrl (JSON {version, url}) when set. UpdatePrompt offers the install.
using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

public static class Updates
{
    const string GitHubLatest = "https://api.github.com/repos/JemiloII/stream-racer-api/releases/latest";

    public static string Latest, Url; static DateTime _checkedAt = DateTime.MinValue; static int _busy;
    public static bool? UpToDate => Latest == null ? null : (bool?)(Pure.CompareVersions(Plugin.Version, Latest) >= 0);
    public static void Kick()
    {
        string updateUrl = string.IsNullOrWhiteSpace(Plugin.UpdateUrl?.Value) ? GitHubLatest : Plugin.UpdateUrl.Value.Trim();
        if (updateUrl == "off" || DateTime.UtcNow - _checkedAt < TimeSpan.FromHours(1) || Interlocked.Exchange(ref _busy, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using var web = new WebClient(); web.Headers["User-Agent"] = "StreamRacerApi/" + Plugin.Version;
                var release = JObject.Parse(web.DownloadString(updateUrl));
                Url = (string)release["url"] ?? ZipAsset(release);
                Latest = (string)release["version"] ?? ((string)release["tag_name"])?.TrimStart('v');
                _checkedAt = DateTime.UtcNow;
            }
            catch (Exception error) { Plugin.Log.LogWarning("update check: " + error.Message); _checkedAt = DateTime.UtcNow; }
            finally { _busy = 0; }
        });
    }

    static string ZipAsset(JObject release) =>
        (string)(release["assets"] as JArray)?.FirstOrDefault(asset => ((string)asset["name"])?.EndsWith(".zip") == true)?["browser_download_url"];
}
