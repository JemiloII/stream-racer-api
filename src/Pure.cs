using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreamRacerApi;

// Logic with no game or engine-runtime dependency (Vector3/Mathf are plain managed math), so it runs in unit tests.
// Game.cs / Minimap.cs / Updates.cs call into here; tests live in tests/unit.
public static class Pure
{
    // ---- boost zones ----
    // Straights worth boosting on: stretches where the route turns less than maxTurn° and stays flat over the next
    // `look` units, at least minLen long. `sample(d)` = (position, direction) at route distance d. Returns [start, end].
    public static List<float[]> Zones(Func<float, (Vector3 pos, Vector3 dir)> sample, float length,
        float step = 4f, float look = 40f, float maxTurn = 12f, float maxSlope = 0.12f, float minLen = 55f)
    {
        var zones = new List<float[]>();
        if (length <= 0 || sample == null) return zones;
        float zoneStart = -1f;
        for (float d = 0; d + look < length; d += step)
        {
            var a = sample(d); var b = sample(d + look);
            var da = a.dir; da.y = 0; var db = b.dir; db.y = 0;
            float turn = Vector3.Angle(da, db);
            float slope = Mathf.Abs(b.pos.y - a.pos.y) / look;
            bool straight = turn < maxTurn && slope < maxSlope;
            if (straight && zoneStart < 0) zoneStart = d;
            if (!straight && zoneStart >= 0) { if (d - zoneStart >= minLen) zones.Add(new[] { zoneStart, d + look * 0.5f }); zoneStart = -1f; }
        }
        if (zoneStart >= 0 && length - zoneStart >= minLen) zones.Add(new[] { zoneStart, length });
        return zones;
    }

    public static bool InZone(IEnumerable<float[]> zones, float progress, float tail = 20f)
    {
        foreach (var z in zones) if (progress >= z[0] && progress <= z[1] - tail) return true; // not right at the end of a straight
        return false;
    }

    // ---- versions ----
    // "1.2.10" vs "1.2.9" -> 1; missing parts count as 0; non-numeric parts count as 0.
    public static int CompareVersions(string a, string b)
    {
        var pa = (a ?? "0").Split('.'); var pb = (b ?? "0").Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length && int.TryParse(pa[i], out var vx) ? vx : 0, y = i < pb.Length && int.TryParse(pb[i], out var vy) ? vy : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // ---- mini map ----
    // "16:9", "4:3", "1:1", "21:9" -> width/height; anything else (e.g. "auto") -> the track's own ratio.
    public static float AspectRatio(string a, float track)
    {
        var p = (a ?? "").Trim().ToLowerInvariant().Split(':');
        if (p.Length == 2 && float.TryParse(p[0], out var w) && float.TryParse(p[1], out var h) && w > 0 && h > 0) return w / h;
        return track;
    }

    // In-game map box height (screen fraction) for a width fraction, the screen size and the wanted ratio.
    public static float MapHeight(float w, float screenW, float screenH, float aspect) =>
        Mathf.Clamp(w * screenW / screenH / Mathf.Max(0.01f, aspect), 0.03f, 0.95f);

    // ---- colors ----
    public static readonly string[] Palette = { "#ff3b30", "#ffd400", "#35e0ff", "#b07cff", "#3ddc84", "#ff8c42", "#ff5fa2", "#7ae7ff", "#c8ff4d", "#ff7a7a" };
    // Stable pick per login so a bot keeps its color between races.
    public static string AutoColorHex(string login)
    {
        uint h = 2166136261; foreach (char c in (login ?? "").ToLowerInvariant()) h = unchecked((h ^ c) * 16777619); // FNV-1a: spreads similar names apart
        return Palette[(int)(h % (uint)Palette.Length)];
    }

    // ---- chat ----
    // "!race color red" with command "!race color" -> "red"; null when it isn't that command.
    public static string CommandArg(string message, string command)
    {
        string cmd = (command ?? "").Trim(); if (cmd.Length == 0 || string.IsNullOrEmpty(message)) return null;
        string m = message.Trim();
        if (!m.StartsWith(cmd, StringComparison.OrdinalIgnoreCase)) return null;
        if (m.Length > cmd.Length && !char.IsWhiteSpace(m[cmd.Length])) return null; // "!race colorful" is not "!race color"
        return m.Substring(cmd.Length).Trim();
    }

    // Several aliases: the first one that matches wins ("!race respawn|!respawn"). Null when none does.
    public static string CommandArg(string message, IEnumerable<string> commands)
    {
        if (commands == null) return null;
        foreach (var c in commands) { var a = CommandArg(message, c); if (a != null) return a; }
        return null;
    }

    // "!race respawn|!respawn" or "!race respawn, !respawn" -> ["!race respawn", "!respawn"]. Empty parts are dropped.
    public static List<string> CommandAliases(string list)
    {
        var result = new List<string>();
        foreach (var part in (list ?? "").Split('|', ',')) { var t = part.Trim(); if (t.Length > 0) result.Add(t); }
        return result;
    }

    // Chat confirmations for the color command.
    public static string ColorSetReply(string displayName, string hex) => "@" + (displayName ?? "") + " color set to " + (hex ?? "");
    public static string ColorDeniedReply(string displayName, string tier) => "@" + (displayName ?? "") + " color is for " + (tier ?? "") + "+";

    // ---- perks ----
    // Tiers: "everyone" | "follower" | "subscriber" | "off". Subs, devs and the host count as followers.
    public static bool TierAllows(string tier, bool isFollower, bool isSub, bool isDev, bool isHost)
    {
        switch ((tier ?? "everyone").Trim().ToLowerInvariant())
        {
            case "off": return false;
            case "everyone": case "free": return true;
            case "follower": return isSub || isDev || isHost || isFollower;
            case "subscriber": case "sub": return isSub || isDev || isHost;
        }
        return true;
    }

    // Extra boosts on join: follower + subscriber + developer + host, stacking. `reasons` says which applied.
    public static (int count, List<string> reasons) ExtraBoosts(int boostFollower, int boostSubscriber, int boostDeveloper, int boostHost,
        bool isFollower, bool isSub, bool isDev, bool isHost)
    {
        int n = 0; var why = new List<string>();
        if (isFollower && boostFollower != 0) { n += boostFollower; why.Add("follower"); }
        if (isSub && boostSubscriber != 0) { n += boostSubscriber; why.Add("sub"); }
        if (isDev && boostDeveloper != 0) { n += boostDeveloper; why.Add("dev"); }
        if (isHost && boostHost != 0) { n += boostHost; why.Add("host"); }
        return (n, why);
    }

    // Why a follower boost may be missing: "ok" (checks can run), "no token" (nothing pasted on the Settings page,
    // so followers are never detected), "unknown" (a token is set but the last lookup failed or the streamer id is missing).
    public static string FollowerCheckStatus(bool tokenSet, bool clientIdSet, bool streamerKnown, string lastError)
    {
        if (!tokenSet || !clientIdSet) return "no token";
        if (!streamerKnown || !string.IsNullOrEmpty(lastError)) return "unknown";
        return "ok";
    }

    // Twitch tokens are pasted in every shape: "oauth:abc", "Bearer abc", "abc". Keep the bare token.
    public static string CleanToken(string token)
    {
        var t = (token ?? "").Trim();
        foreach (var prefix in new[] { "oauth:", "bearer " })
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) t = t.Substring(prefix.Length).Trim();
        return t;
    }

    // ---- camera director ----
    public static readonly string[] ShotKeys = { "grid", "high", "side", "sweep", "pack", "front", "chase", "orbit", "overhead", "prop", "finish", "duel", "pileup", "boom" };

    // Director shot key -> toggle name: "high2" is a high shot, "front:login" a front shot, "wide:login" has no toggle ("wide").
    public static string ShotKind(string key)
    {
        var k = (key ?? "").Split(':')[0].Trim().ToLowerInvariant();
        return k == "high2" ? "high" : k;
    }

    // Missing toggle = enabled: an old settings file (or a partial PUT) never switches a shot off by accident.
    public static bool ShotEnabled(IDictionary<string, bool> shots, string key)
    {
        var kind = ShotKind(key);
        return shots == null || !shots.TryGetValue(kind, out var on) || on;
    }
}
