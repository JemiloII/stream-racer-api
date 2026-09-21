using System;
using System.Net;
using System.Text;
using System.Threading;

namespace StreamRacerApi;

// settings.webhooks: [{ event, url, method, header, body, enabled }]. When `event` (or "*") is emitted the request is
// sent in the background. Empty body = the event's JSON; otherwise the body is sent as written with {json} replaced
// by the event JSON and {event} by its name. `header` is one "Name: value" line (e.g. Authorization).
// ponytail: fire and forget, 5 s timeout, errors go to the BepInEx log.
public static class Webhooks
{
    public static void Fire(string evt, string json)
    {
        var hooks = Settings.Current?.webhooks; if (hooks == null || hooks.Count == 0) return;
        foreach (var h in hooks)
        {
            if (!h.enabled || string.IsNullOrWhiteSpace(h.url) || (h.@event != evt && h.@event != "*")) continue;
            var hook = h;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(hook.url);
                    req.Method = string.IsNullOrWhiteSpace(hook.method) ? "POST" : hook.method.ToUpperInvariant();
                    req.Timeout = 5000;
                    int colon = (hook.header ?? "").IndexOf(':');
                    if (colon > 0) req.Headers[hook.header.Substring(0, colon).Trim()] = hook.header.Substring(colon + 1).Trim();
                    if (req.Method != "GET")
                    {
                        string body = string.IsNullOrWhiteSpace(hook.body) ? json : hook.body.Replace("{json}", json).Replace("{event}", evt);
                        req.ContentType = body.TrimStart().StartsWith("{") || body.TrimStart().StartsWith("[") ? "application/json" : "text/plain";
                        var bytes = Encoding.UTF8.GetBytes(body);
                        req.ContentLength = bytes.Length;
                        using var s = req.GetRequestStream(); s.Write(bytes, 0, bytes.Length);
                    }
                    using var res = (HttpWebResponse)req.GetResponse();
                }
                catch (Exception e) { Plugin.Log.LogWarning($"webhook {hook.@event} -> {hook.url}: {e.Message}"); }
            });
        }
    }
}
