// settings.webhooks: [{ event, url, method, header, body, enabled }]. When `event` (or "*") is emitted the request is
// sent in the background. Empty body = the event's JSON; otherwise the body is sent as written with {json} replaced
// by the event JSON and {event} by its name. `header` is one "Name: value" line (e.g. Authorization).
// ponytail: fire and forget, 5 s timeout, errors go to the BepInEx log.
using System;
using System.Net;
using System.Text;
using System.Threading;

namespace StreamRacerApi;

public static class Webhooks
{
    public static void Fire(string eventName, string json)
    {
        var hooks = Settings.Current?.webhooks; if (hooks == null || hooks.Count == 0) return;
        foreach (var candidate in hooks)
        {
            if (!candidate.enabled || string.IsNullOrWhiteSpace(candidate.url) || (candidate.@event != eventName && candidate.@event != "*")) continue;
            var hook = candidate;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(hook.url);
                    request.Method = string.IsNullOrWhiteSpace(hook.method) ? "POST" : hook.method.ToUpperInvariant();
                    request.Timeout = 5000;
                    int colon = (hook.header ?? "").IndexOf(':');
                    if (colon > 0) request.Headers[hook.header.Substring(0, colon).Trim()] = hook.header.Substring(colon + 1).Trim();
                    if (request.Method != "GET")
                    {
                        string body = string.IsNullOrWhiteSpace(hook.body) ? json : hook.body.Replace("{json}", json).Replace("{event}", eventName);
                        request.ContentType = body.TrimStart().StartsWith("{") || body.TrimStart().StartsWith("[") ? "application/json" : "text/plain";
                        var bytes = Encoding.UTF8.GetBytes(body);
                        request.ContentLength = bytes.Length;
                        using var stream = request.GetRequestStream(); stream.Write(bytes, 0, bytes.Length);
                    }
                    using var response = (HttpWebResponse)request.GetResponse();
                }
                catch (Exception error) { Plugin.Log.LogWarning($"webhook {hook.@event} -> {hook.url}: {error.Message}"); }
            });
        }
    }
}
