// Game.Track: the circuit. The route as a polyline (mini map / GET /track), the straights worth boosting on
// (GET /track/zones) and where the real finish line is.
namespace StreamRacerApi;

static partial class Game
{
    // The route as a polyline (sampled along the circuit), cached per track.
    static List<Vector3> _track; static object _trackKey;
    public static List<Vector3> TrackPoints()
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null) return new List<Vector3>();
        var waypoints = circuit.Waypoints();
        object key = waypoints == null || waypoints.Length == 0 ? null
            : (object)(waypoints.Length + ":" + (waypoints[0] != null ? waypoints[0].position.ToString() : "") + ":" + (waypoints[waypoints.Length - 1] != null ? waypoints[waypoints.Length - 1].position.ToString() : ""));
        if (_track != null && key != null && key.Equals(_trackKey)) return _track;
        // The waypoint transforms are the road itself, in order. Route-distance sampling can jump across gaps, so avoid it.
        var points = circuit.Waypoints().Where(waypoint => waypoint != null).Select(waypoint => waypoint.position).ToList();
        // The waypoint list ends with a hop back to the start (closing the circuit for the AI). Cut the outline
        // at the first segment that is far longer than the typical spacing so no start-finish line gets drawn.
        if (points.Count > 6)
        {
            var segments = Enumerable.Range(0, points.Count - 1).Select(i => Vector3.Distance(points[i], points[i + 1])).OrderBy(length => length).ToList();
            float median = segments[segments.Count / 2], limit = Mathf.Max(60f, median * 6f);
            // the closing hop back to the start lives at the tail; only cut there (last 20 %), never at the front
            for (int i = (int)(points.Count * 0.8f); i < points.Count - 1; i++)
                if (Vector3.Distance(points[i], points[i + 1]) > limit) { points = points.Take(i + 1).ToList(); break; }
        }
        // densify long straights a little so the line renderer's corners stay smooth
        var dense = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            dense.Add(points[i]);
            if (i + 1 < points.Count)
            {
                float distance = Vector3.Distance(points[i], points[i + 1]);
                for (int k = 1; k < (int)(distance / 12f); k++) dense.Add(Vector3.Lerp(points[i], points[i + 1], k * 12f / distance));
            }
        }
        points = dense;
        _track = points; _trackKey = key;
        return points;
    }

    // ---- boost zones ----
    // Straights worth boosting on: stretches where the route barely turns and stays flat over the next ~40 units.
    // Returned as [start, end] route distances; cars boost when their progress is inside one (early part preferred).
    static List<float[]> _zones; static object _zonesKey;
    public static List<float[]> BoostZones()
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null) return new List<float[]>();
        float length = circuit.Length();
        float finish = Vehicles().Select(FinishAt).DefaultIfEmpty(0f).Max();
        if (finish > 0) length = Mathf.Min(length, finish); // the route keeps going past the finish line; nothing to boost for there
        object key = length + ":" + (circuit.Waypoints()?.Length ?? 0);
        if (_zones != null && key.Equals(_zonesKey)) return _zones;
        var zones = Pure.Zones(distance => { var point = circuit.GetRoutePoint(distance); return (point.Position(), point.Direction()); }, length);
        _zones = zones; _zonesKey = key;
        return zones;
    }
    public static bool InBoostZone(float progress) => Pure.InZone(BoostZones(), progress);
    public static object ZonesDto()
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        var game = Instances.GameController?.CurrentGame();
        return new
        {
            map = game?.MapId(), mapName = game?.MapName(),
            length = circuit?.Length() ?? 0f, finishAt = Vehicles().Select(FinishAt).DefaultIfEmpty(0f).Max(),
            finishLineAt = FinishLine(out _, out _, out var finishLineDistance) ? finishLineDistance : -1f,
            raceDistance = RaceDistance,
            roadFraction = Pure.RoadFraction(circuit?.Waypoints()?.Where(waypoint => waypoint != null).Select(waypoint => waypoint.position).ToList()),
            zones = BoostZones().Select(zone => new { start = zone[0], end = zone[1], length = zone[1] - zone[0] }).ToList(),
        };
    }

    public static object TrackDto(bool raw = false)
    {
        var points = raw
            ? (Instances.WaypointController?.GetCircuit()?.Waypoints()?.Where(waypoint => waypoint != null).Select(waypoint => waypoint.position).ToList() ?? new List<Vector3>())
            : TrackPoints();
        if (points.Count == 0) return new { points = new List<float[]>(), bounds = (object)null };
        return new
        {
            points = points.Select(point => new[] { point.x, point.z }).ToList(),
            bounds = new { minX = points.Min(point => point.x), maxX = points.Max(point => point.x), minZ = points.Min(point => point.z), maxZ = points.Max(point => point.z) },
        };
    }

    // The real finish: the FinishLine trigger object. Position, the route direction through it, and its route distance.
    public const float SampleEvery = 2f;   // how finely the road is mapped, in route units
    static Vector3 _finishPosition, _finishDirection; static float _finishDistance = -1f; static object _finishCircuit;
    static float _raceDistance = -1f;
    /// The distance from the grid to the finish line: 0% to 100%. -1 until the road has been mapped.
    public static float RaceDistance => _raceDistance;
    public static bool FinishLine(out Vector3 position, out Vector3 direction, out float distance)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null) { position = direction = Vector3.zero; distance = -1f; return false; }
        if (!ReferenceEquals(_finishCircuit, circuit) || _finishDistance < 0)
        {
            GameObject trigger = null;
            try { trigger = GameObject.FindGameObjectsWithTag("FinishLine").FirstOrDefault(); } catch { }
            if (trigger == null) { position = direction = Vector3.zero; distance = -1f; return false; }
            // Map the road once, then read the answer off the map. The track never moves, so this is measured, not
            // guessed: sample the route from the grid to where the road ends, then find the exact point where that
            // line of road crosses the finish line. That distance is 100%.
            var line = trigger.transform.position;
            float loop = circuit.Length() > 0 ? circuit.Length() : 5000f;
            var waypoints = circuit.Waypoints()?.Where(waypoint => waypoint != null).Select(waypoint => waypoint.position).ToList() ?? new List<Vector3>();
            float road = Mathf.Min(loop, loop * Pure.RoadFraction(waypoints) + 40f);   // a little past the end so the line sits inside
            var positions = new List<Vector3>((int)(road / SampleEvery) + 2); var along = new List<float>(positions.Capacity);
            var throughLine = Vector3.forward; float nearestError = float.MaxValue, nearestAt = 0f;
            for (float at = 0f; at <= road; at += SampleEvery)
            {
                var point = circuit.GetRoutePoint(at);
                positions.Add(point.Position()); along.Add(at);
                float error = Vector3.Distance(point.Position(), line);
                if (error < nearestError) { nearestError = error; nearestAt = at; throughLine = point.Direction(); }
            }
            // A finish line is built square across the road, so the road's own direction where it meets the line is
            // the line's normal: which side of it a point sits on.
            float crossing = Pure.FinishCrossing(positions, along, line, throughLine);
            var facing = throughLine; facing.y = 0f; facing.Normalize();
            _finishPosition = line; _finishDirection = facing;
            _finishDistance = crossing > 1f ? crossing : nearestAt;   // nothing crossed it: fall back on the closest the road comes
            _finishCircuit = circuit; _raceDistance = _finishDistance;
        }
        position = _finishPosition; direction = _finishDirection; distance = _finishDistance; return true;
    }
}
