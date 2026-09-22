using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreamRacerApi;

// Logic with no game or engine-runtime dependency (Vector3/Mathf are plain managed math), so it runs in unit tests.
// Game/*.cs, Minimap.cs and Updates.cs call into here; tests live in tests/unit. Compiled on its own by the test
// project, so it keeps its own usings and never touches GameNames.
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
        for (float distance = 0; distance + look < length; distance += step)
        {
            var here = sample(distance); var ahead = sample(distance + look);
            var hereDirection = here.dir; hereDirection.y = 0; var aheadDirection = ahead.dir; aheadDirection.y = 0;
            float turn = Vector3.Angle(hereDirection, aheadDirection);
            float slope = Mathf.Abs(ahead.pos.y - here.pos.y) / look;
            bool straight = turn < maxTurn && slope < maxSlope;
            if (straight && zoneStart < 0) zoneStart = distance;
            if (!straight && zoneStart >= 0) { if (distance - zoneStart >= minLen) zones.Add(new[] { zoneStart, distance + look * 0.5f }); zoneStart = -1f; }
        }
        if (zoneStart >= 0 && length - zoneStart >= minLen) zones.Add(new[] { zoneStart, length });
        return zones;
    }

    public static bool InZone(IEnumerable<float[]> zones, float progress, float tail = 20f)
    {
        foreach (var zone in zones) if (progress >= zone[0] && progress <= zone[1] - tail) return true; // not right at the end of a straight
        return false;
    }

    // ---- versions ----
    // "1.2.10" vs "1.2.9" -> 1; missing parts count as 0; non-numeric parts count as 0.
    public static int CompareVersions(string left, string right)
    {
        var leftParts = (left ?? "0").Split('.'); var rightParts = (right ?? "0").Split('.');
        for (int i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            int leftPart = i < leftParts.Length && int.TryParse(leftParts[i], out var parsedLeft) ? parsedLeft : 0;
            int rightPart = i < rightParts.Length && int.TryParse(rightParts[i], out var parsedRight) ? parsedRight : 0;
            if (leftPart != rightPart) return leftPart.CompareTo(rightPart);
        }
        return 0;
    }

    // ---- mini map ----
    // "16:9", "4:3", "1:1", "21:9" -> width/height; anything else (e.g. "auto") -> the track's own ratio.
    public static float AspectRatio(string aspect, float trackAspect)
    {
        var parts = (aspect ?? "").Trim().ToLowerInvariant().Split(':');
        if (parts.Length == 2 && float.TryParse(parts[0], out var width) && float.TryParse(parts[1], out var height) && width > 0 && height > 0) return width / height;
        return trackAspect;
    }

    // In-game map box height (screen fraction) for a width fraction, the screen size and the wanted ratio.
    public static float MapHeight(float widthFraction, float screenWidth, float screenHeight, float aspect) =>
        Mathf.Clamp(widthFraction * screenWidth / screenHeight / Mathf.Max(0.01f, aspect), 0.03f, 0.95f);

    // ---- colors ----
    public static readonly string[] Palette = { "#ff3b30", "#ffd400", "#35e0ff", "#b07cff", "#3ddc84", "#ff8c42", "#ff5fa2", "#7ae7ff", "#c8ff4d", "#ff7a7a" };
    // Stable pick per login so a bot keeps its color between races.
    public static string AutoColorHex(string login)
    {
        uint hash = 2166136261; foreach (char character in (login ?? "").ToLowerInvariant()) hash = unchecked((hash ^ character) * 16777619); // FNV-1a: spreads similar names apart
        return Palette[(int)(hash % (uint)Palette.Length)];
    }

    // ---- respawns ----
    // limit 0 = unlimited (-1); otherwise what's left, never below 0
    public static int RespawnsLeft(int limit, int used) => limit <= 0 ? -1 : Math.Max(0, limit - used);

    // ---- chat ----
    // "!race color red" with command "!race color" -> "red"; null when it isn't that command.
    public static string CommandArg(string message, string command)
    {
        string prefix = (command ?? "").Trim(); if (prefix.Length == 0 || string.IsNullOrEmpty(message)) return null;
        string text = message.Trim();
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (text.Length > prefix.Length && !char.IsWhiteSpace(text[prefix.Length])) return null; // "!race colorful" is not "!race color"
        return text.Substring(prefix.Length).Trim();
    }

    // Several aliases: the first one that matches wins ("!race respawn|!respawn"). Null when none does.
    public static string CommandArg(string message, IEnumerable<string> commands)
    {
        if (commands == null) return null;
        foreach (var command in commands) { var argument = CommandArg(message, command); if (argument != null) return argument; }
        return null;
    }

    // "!race respawn|!respawn" or "!race respawn, !respawn" -> ["!race respawn", "!respawn"]. Empty parts are dropped.
    public static List<string> CommandAliases(string list)
    {
        var result = new List<string>();
        foreach (var part in (list ?? "").Split('|', ',')) { var alias = part.Trim(); if (alias.Length > 0) result.Add(alias); }
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
        int count = 0; var reasons = new List<string>();
        if (isFollower && boostFollower != 0) { count += boostFollower; reasons.Add("follower"); }
        if (isSub && boostSubscriber != 0) { count += boostSubscriber; reasons.Add("sub"); }
        if (isDev && boostDeveloper != 0) { count += boostDeveloper; reasons.Add("dev"); }
        if (isHost && boostHost != 0) { count += boostHost; reasons.Add("host"); }
        return (count, reasons);
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
        var cleaned = (token ?? "").Trim();
        foreach (var prefix in new[] { "oauth:", "bearer " })
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring(prefix.Length).Trim();
        return cleaned;
    }

    // ---- camera director ----
    public static readonly string[] ShotKeys = { "grid", "high", "side", "sweep", "pack", "front", "chase", "orbit", "overhead", "prop", "finish", "duel", "pileup", "boom" };

    // Director shot key -> toggle name: "high2" is a high shot, "front:login" a front shot, "wide:login" has no toggle ("wide").
    public static string ShotKind(string key)
    {
        var kind = (key ?? "").Split(':')[0].Trim().ToLowerInvariant();
        return kind == "high2" ? "high" : kind;
    }

    // Missing toggle = enabled: an old settings file (or a partial PUT) never switches a shot off by accident.
    public static bool ShotEnabled(IDictionary<string, bool> shots, string key)
    {
        var kind = ShotKind(key);
        return shots == null || !shots.TryGetValue(kind, out var enabled) || enabled;
    }
}
