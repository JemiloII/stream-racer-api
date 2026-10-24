using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StreamRacerApi;

// Logic with no game or engine-runtime dependency (Vector3/Mathf are plain managed math), so it runs in unit tests.
// Game/*.cs and Updates.cs call into here; tests live in tests/unit. Compiled on its own by the test
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
            float turn = Math.Abs(FlatTurn(here.dir, ahead.dir));
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

    // ---- drift (Game.Drift) ----
    // Heading change from `here` to `ahead`, seen from above (height ignored), in degrees: + = bends right, - = left.
    public static float FlatTurn(Vector3 here, Vector3 ahead)
    {
        here.y = 0f;
        ahead.y = 0f;
        return Vector3.SignedAngle(here, ahead, Vector3.up);
    }

    // How far the road turns over the `look` units after route distance `from`, summed in `step`s so a hairpin past
    // 180 degrees still reads as one big turn and an S-bend cancels out. Signed like FlatTurn.
    public static float TurnAhead(Func<float, (Vector3 pos, Vector3 dir)> sample, float from, float look, float step = 4f)
    {
        if (sample == null || look <= 0f || step <= 0f)
        {
            return 0f;
        }
        float total = 0f;
        var previous = sample(from).dir;
        int steps = (int)Math.Ceiling(look / step);   // evenly spaced, the last one exactly `look` ahead
        for (int i = 1; i <= steps; i++)
        {
            var current = sample(from + look * i / steps).dir;
            total += FlatTurn(previous, current);
            previous = current;
        }
        return total;
    }

    // Yaw rate (rad/s) a drifting car should have, measured INTO the turn (+ = nose swinging toward the inside).
    // `slip` = degrees the nose points inside the direction of travel. Under `target` the nose is pushed in at
    // `pushRate` at least; between target and `max` the rate is capped at pushRate; past `max` it may not rotate
    // further in at all, so the car slides instead of spinning.
    public static float DriftYawRate(float slip, float currentRate, float target, float max, float pushRate)
    {
        if (slip >= max)
        {
            return Math.Min(currentRate, 0f);
        }
        return slip < target ? Math.Max(currentRate, pushRate) : Math.Min(currentRate, pushRate);
    }

    // A slide ends at maxSeconds, or (after minSeconds) when the road ahead stops turning the drift's way or the car
    // has straightened back up. `turnAhead` and `slip` are measured into the drift's turn, like DriftYawRate.
    public static bool DriftOver(float elapsed, float turnAhead, float slip, float minSeconds, float maxSeconds,
        float exitTurn, float realignedSlip)
    {
        if (elapsed >= maxSeconds)
        {
            return true;
        }
        return elapsed >= minSeconds && (turnAhead < exitTurn || slip < realignedSlip);
    }

    // Mini-turbo force after a drift: nothing under minSeconds, then minForce growing with the drift time to maxForce
    // at fullSeconds (capped there).
    public static float DriftTurbo(float driftSeconds, float minSeconds, float fullSeconds, float minForce, float maxForce)
    {
        if (driftSeconds < minSeconds)
        {
            return 0f;
        }
        float share = Mathf.Clamp01((driftSeconds - minSeconds) / Math.Max(0.01f, fullSeconds - minSeconds));
        return minForce + (maxForce - minForce) * share;
    }

    public static string DriftNoBoostsReply(string displayName) => "@" + (displayName ?? "") + " no boosts left for a drift";

    // ---- the track ----
    // The game's route is a closed loop: the waypoint list ends with a hop straight back to the start so the AI can
    // keep driving forever. On a point-to-point map that hop is a long jump across the map and is not road anyone
    // races on, which is why the loop's length is not the racing distance. Road spacing is fairly even, so a tail
    // segment many times the typical one is the hop. Returns how much of the loop is real road, 0..1.
    public static float RoadFraction(IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints == null || waypoints.Count < 7) return 1f;
        var segments = new List<float>(waypoints.Count);
        float total = 0f;
        for (int i = 0; i + 1 < waypoints.Count; i++) { float length = Vector3.Distance(waypoints[i], waypoints[i + 1]); segments.Add(length); total += length; }
        if (total <= 0f) return 1f;
        var sorted = new List<float>(segments); sorted.Sort();
        float median = sorted[sorted.Count / 2], limit = Math.Max(60f, median * 6f);
        float road = 0f;
        for (int i = 0; i < segments.Count; i++)
        {
            if (i >= (int)(segments.Count * 0.8f) && segments[i] > limit) break;   // the hop home lives at the tail
            road += segments[i];
        }
        return Math.Min(1f, road / total);
    }

    /// Where the road crosses the finish line, as a distance along the route: positions[i] is the road at along[i].
    /// A lap passes its line twice, leaving the grid and coming home, so the crossing that ends the race is the last
    /// one. Only a crossing within halfWidth of the line's middle counts, so road that merely passes nearby is
    /// ignored. Returns -1 when the road never crosses it.
    public static float FinishCrossing(IReadOnlyList<Vector3> positions, IReadOnlyList<float> along, Vector3 line, Vector3 normal, float halfWidth = 45f)
    {
        if (positions == null || along == null || positions.Count < 2 || positions.Count != along.Count) return -1f;
        normal.y = 0f;
        if (normal.sqrMagnitude < 1e-4f) return -1f;
        normal.Normalize();
        float Side(int i) { var offset = positions[i] - line; offset.y = 0f; return Vector3.Dot(offset, normal); }
        float found = -1f, previous = Side(0);
        for (int i = 1; i < positions.Count; i++)
        {
            float current = Side(i);
            if (previous < 0f && current >= 0f)                                    // crossed it going forwards
            {
                float fraction = current == previous ? 0f : -previous / (current - previous);
                var at = Vector3.Lerp(positions[i - 1], positions[i], fraction);
                var offset = at - line; offset.y = 0f;
                if (offset.magnitude <= halfWidth) found = along[i - 1] + (along[i] - along[i - 1]) * fraction;
            }
            previous = current;
        }
        return found;
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

    // ---- ranked ----
    // Ranked on (settings.sendResults): only the game's own racing counts, so the API refuses what the mod adds on top
    // (free / extra boosts, slow, grow, drift, weather, surfaces, marking a finish, every respawn incl. your own,
    // API booms, the game's chaos effects fired through the API). Allowed: lobby, joins, colours, camera, spending a car's own boost pool (/boost/me, /boost/:x/use).
    static readonly HashSet<string> RankedBlockedRoots = new() { "boom", "speed", "grow", "drift", "surface", "weather", "finish", "respawn", "effect", "chaos" };
    public static bool RankedBlocks(string root, string target, string action)
    {
        if (RankedBlockedRoots.Contains(root ?? "")) return true;
        if (root == "boost") return target != "me" && action != "use";
        return false;
    }

    // ---- local standings ----
    // One finished race as recorded on this PC (Game.Results). Place counts finishers only; a DNF has Finished = false.
    public class RaceRecord
    {
        public DateTime At;
        public List<RaceEntry> Racers = new();
    }
    public class RaceEntry
    {
        public string Login, Name, Color;
        public int Place;
        public bool Finished;
    }
    public class Standing
    {
        public string Login, Name, Color;
        public int Races, Wins, Podiums, Finishes, Points, Best;
    }

    public static readonly string[] Periods = { "day", "week", "month", "all" };

    // Where a period starts, in the PC's local time: today 00:00, Monday 00:00, the 1st 00:00; "all" (or anything else) = no start.
    public static DateTime PeriodStart(string period, DateTime now)
    {
        var today = now.Date;
        switch ((period ?? "").Trim().ToLowerInvariant())
        {
            case "day": return today;
            case "week": return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            case "month": return new DateTime(today.Year, today.Month, 1);
            default: return DateTime.MinValue;
        }
    }

    // A finisher scores the field size minus their place plus one (1st of 8 = 8 points, last finisher = 1+); a DNF scores 0.
    public static int RacePoints(int place, bool finished, int fieldSize) => finished && place > 0 ? Math.Max(1, fieldSize - place + 1) : 0;

    // Totals per racer over the races since the period started, best first: points, then wins, podiums, fewest races.
    public static List<Standing> Standings(IEnumerable<RaceRecord> races, string period, DateTime now)
    {
        var since = PeriodStart(period, now);
        var byLogin = new Dictionary<string, Standing>();
        foreach (var race in races.Where(race => race.At >= since).OrderBy(race => race.At))
        {
            foreach (var entry in race.Racers.Where(entry => !string.IsNullOrEmpty(entry.Login)))
            {
                string login = entry.Login.ToLowerInvariant();
                if (!byLogin.TryGetValue(login, out var standing)) byLogin[login] = standing = new Standing { Login = login };
                standing.Name = entry.Name ?? standing.Name ?? login;   // latest name and colour win
                standing.Color = entry.Color ?? standing.Color;
                standing.Races++;
                standing.Points += RacePoints(entry.Place, entry.Finished, race.Racers.Count);
                if (!entry.Finished) continue;
                standing.Finishes++;
                if (entry.Place == 1) standing.Wins++;
                if (entry.Place <= 3) standing.Podiums++;
                if (standing.Best == 0 || entry.Place < standing.Best) standing.Best = entry.Place;
            }
        }
        return byLogin.Values
            .OrderByDescending(standing => standing.Points).ThenByDescending(standing => standing.Wins)
            .ThenByDescending(standing => standing.Podiums).ThenBy(standing => standing.Races).ThenBy(standing => standing.Login)
            .ToList();
    }

    // ---- surfaces (ice, puddles, mud, snow) ----
    // A patch lives in route coordinates: distance along the route x lateral offset from its centre line (+ = right
    // of the direction of travel). Stretches cover [Start, End] across [Lateral - HalfWidth, Lateral + HalfWidth];
    // a puddle is the oval inscribed in that box. Game.Surfaces places them, draws them and applies their effect.
    public enum SurfaceKind
    {
        Ice,
        Puddle,
        Mud,
        Snow,
    }

    public class SurfacePatch
    {
        public SurfaceKind Kind;
        public float Start, End;
        public float Lateral, HalfWidth;
        public bool Oval;                                      // an oval (puddle, mud, snow blob) instead of a full-width stretch
        public float Strength = 1f;                            // 1 = fresh; snow packs down as cars pass
        public readonly HashSet<string> PassedBy = new();      // cars already counted once on this patch
        public float Centre => (Start + End) * 0.5f;
    }

    // What the surfaces under a car do to it: top-speed multiplier, grip (friction) multiplier, and drag = the
    // fraction of the car's ground speed taken away per second.
    public struct SurfaceEffect
    {
        public float Speed, Grip, Drag;
        public static SurfaceEffect None => new SurfaceEffect { Speed = 1f, Grip = 1f, Drag = 0f };
        public bool IsNone => Speed >= 1f && Grip >= 1f && Drag <= 0f;
    }

    // A point in the route frame: how far along the route direction it sits from the route point (added to the
    // car's route progress) and how far to the side. Flat (XZ) maths: hills don't skew it.
    public static (float along, float lateral) RouteFrame(Vector3 position, Vector3 routePosition, Vector3 routeDirection)
    {
        var forward = routeDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
        {
            return (0f, 0f);
        }
        forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);
        var offset = position - routePosition;
        offset.y = 0f;
        return (Vector3.Dot(offset, forward), Vector3.Dot(offset, right));
    }

    public static bool Contains(SurfacePatch patch, float distance, float lateral)
    {
        if (!patch.Oval)
        {
            return distance >= patch.Start && distance <= patch.End
                && Mathf.Abs(lateral - patch.Lateral) <= patch.HalfWidth;
        }
        float radiusAlong = (patch.End - patch.Start) * 0.5f;
        if (radiusAlong <= 0f || patch.HalfWidth <= 0f)
        {
            return false;
        }
        float alongPart = (distance - patch.Centre) / radiusAlong;
        float lateralPart = (lateral - patch.Lateral) / patch.HalfWidth;
        return alongPart * alongPart + lateralPart * lateralPart <= 1f;
    }

    // Snow packs: every car that drives over it the first time takes `perPass` of what is left, down to `floor`, so
    // the leaders carve a safer path for the cars behind. Returns true when this car had not been counted yet.
    public static bool NotePass(SurfacePatch patch, string car, float perPass, float floor)
    {
        if (string.IsNullOrEmpty(car) || !patch.PassedBy.Add(car))
        {
            return false;
        }
        if (patch.Kind == SurfaceKind.Snow)
        {
            patch.Strength = Mathf.Max(floor, patch.Strength * (1f - perPass));
        }
        return true;
    }

    // Mud/snow drag per second: a base pull, plus a bog term that grows as the car slows below `bogSpeed`, so a
    // slow car loses what speed it has left and can stick (the game's own stuck-car respawn then rescues it).
    public static float BogDrag(float speed, float strength, float baseDrag, float bogDrag, float bogSpeed)
    {
        float slowness = bogSpeed > 0f ? Mathf.Clamp01(1f - speed / bogSpeed) : 0f;
        return Mathf.Max(0f, strength) * (baseDrag + bogDrag * slowness);
    }

    // Several patches at once: top speed and grip multipliers multiply, drags add up.
    public static SurfaceEffect Combine(SurfaceEffect first, SurfaceEffect second) => new SurfaceEffect
    {
        Speed = first.Speed * second.Speed,
        Grip = first.Grip * second.Grip,
        Drag = first.Drag + second.Drag,
    };

    // Stretches of `length` route units inside [from, to], none closer than `gap` to another patch of the same kind.
    // `weightAt(start)` biases the pick (ice: corners); null = uniform. Fewer than `count` come back when the road
    // is too short or too full.
    public static List<SurfacePatch> PlaceStretches(System.Random random, SurfaceKind kind, int count, float length,
        float from, float to, float halfWidth, Func<float, float> weightAt, IEnumerable<SurfacePatch> existing,
        float gap = 10f)
    {
        var placed = new List<SurfacePatch>();
        if (count <= 0 || length <= 0f || to - from < length)
        {
            return placed;
        }
        var taken = (existing ?? Enumerable.Empty<SurfacePatch>()).Where(patch => patch.Kind == kind).ToList();
        var candidates = new List<(float start, float weight)>();
        for (int i = 0; i < count * 24; i++)
        {
            float start = from + (float)random.NextDouble() * (to - from - length);
            candidates.Add((start, weightAt == null ? 1f : Mathf.Max(0f, weightAt(start))));
        }
        while (placed.Count < count)
        {
            candidates.RemoveAll(candidate => Collides(taken, candidate.start, candidate.start + length, gap));
            if (candidates.Count == 0)
            {
                break;
            }
            float start = PickWeighted(random, candidates);
            var patch = new SurfacePatch
            {
                Kind = kind,
                Start = start,
                End = start + length,
                Lateral = 0f,
                HalfWidth = halfWidth,
            };
            placed.Add(patch);
            taken.Add(patch);
        }
        return placed;
    }

    // Oval puddles at random spots inside [from, to], each fully on the road (|lateral| + radius <= roadHalfWidth),
    // never overlapping another puddle. Longer along the road than across it.
    public static List<SurfacePatch> PlacePuddles(System.Random random, int count, float from, float to,
        float roadHalfWidth, float minRadius, float maxRadius, IEnumerable<SurfacePatch> existing,
        SurfaceKind kind = SurfaceKind.Puddle)
    {
        var placed = new List<SurfacePatch>();
        var taken = (existing ?? Enumerable.Empty<SurfacePatch>())
            .Where(patch => patch.Oval)
            .ToList();
        for (int attempt = 0; attempt < count * 20 && placed.Count < count; attempt++)
        {
            float radiusAlong = Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble()) * 1.6f;
            float radiusSide = Mathf.Min(Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble()), roadHalfWidth);
            if (to - from < radiusAlong * 2f)
            {
                break;
            }
            float centre = from + radiusAlong + (float)random.NextDouble() * (to - from - radiusAlong * 2f);
            float lateral = ((float)random.NextDouble() * 2f - 1f) * (roadHalfWidth - radiusSide);
            var patch = new SurfacePatch
            {
                Kind = kind,
                Oval = true,
                Start = centre - radiusAlong,
                End = centre + radiusAlong,
                Lateral = lateral,
                HalfWidth = radiusSide,
            };
            if (taken.Any(other => BoxesOverlap(other, patch)))
            {
                continue;
            }
            placed.Add(patch);
            taken.Add(patch);
        }
        return placed;
    }

    static bool Collides(IEnumerable<SurfacePatch> taken, float start, float end, float gap) =>
        taken.Any(patch => start < patch.End + gap && end > patch.Start - gap);

    static bool BoxesOverlap(SurfacePatch first, SurfacePatch second) =>
        first.Start < second.End && second.Start < first.End
        && Mathf.Abs(first.Lateral - second.Lateral) < first.HalfWidth + second.HalfWidth;

    static float PickWeighted(System.Random random, List<(float start, float weight)> candidates)
    {
        float total = candidates.Sum(candidate => candidate.weight);
        if (total <= 0f)
        {
            return candidates[random.Next(candidates.Count)].start;
        }
        float roll = (float)random.NextDouble() * total;
        foreach (var candidate in candidates)
        {
            roll -= candidate.weight;
            if (roll <= 0f)
            {
                return candidate.start;
            }
        }
        return candidates[candidates.Count - 1].start;
    }

    // ---- colors ----
    public static readonly string[] Palette = { "#ff3b30", "#ffd400", "#35e0ff", "#b07cff", "#3ddc84", "#ff8c42", "#ff5fa2", "#7ae7ff", "#c8ff4d", "#ff7a7a" };
    // Stable pick per login so a bot keeps its color between races.
    public static string AutoColorHex(string login)
    {
        uint hash = 2166136261; foreach (char character in (login ?? "").ToLowerInvariant()) hash = unchecked((hash ^ character) * 16777619); // FNV-1a: spreads similar names apart
        return Palette[(int)(hash % (uint)Palette.Length)];
    }

    // ---- respawns ----
    // limit -1 = unlimited (reported as -1); 0 = chat respawns off; otherwise what's left, never below 0
    public static int RespawnsLeft(int limit, int used) => limit < 0 ? -1 : Math.Max(0, limit - used);

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
    public static readonly string[] ShotKeys = { "grid", "high", "side", "sweep", "pack", "front", "chase", "orbit", "overhead", "prop", "finish", "duel", "pileup", "boom", "plane" };

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
