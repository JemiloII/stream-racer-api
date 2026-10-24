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
            usedForPercent = Vehicles().Select(FinishDistance).DefaultIfEmpty(0f).Max(),
            learnedFromFinisher = LearnedFinish,
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
            map = Minimap.GameMap(),   // the game's own road picture + projection, null until it has captured the map
        };
    }

    // The real finish: the FinishLine trigger object. Position, the route direction through it, and its route distance.
    public const float SampleEvery = 2f;   // how finely the road is mapped, in route units
    static Vector3 _finishPosition, _finishDirection; static float _finishDistance = -1f; static object _finishCircuit;
    static float _raceDistance = -1f;
    /// The distance from the grid to the finish line: 0% to 100%. -1 until the road has been mapped.
    public static float RaceDistance => _raceDistance;
    // The first route distance (past `earliest`) at which the road is inside one of the gate's trigger colliders,
    // refined between samples by halving. Height is probed at a few levels: the route line can run below the deck.
    static float TriggerEntry(WaypointCircuit circuit, GameObject gate, List<Vector3> positions, List<float> along, float earliest)
    {
        var boxes = gate.GetComponentsInChildren<Collider>().Where(collider => collider.isTrigger).ToArray();
        if (boxes.Length == 0)
        {
            return -1f;
        }
        for (int i = 1; i < positions.Count; i++)
        {
            if (along[i] < earliest || !InsideAny(boxes, positions[i]))
            {
                continue;
            }
            float outside = along[i - 1], inside = along[i];
            for (int step = 0; step < 8; step++)
            {
                float middle = (outside + inside) * 0.5f;
                if (InsideAny(boxes, circuit.GetRoutePoint(middle).Position())) inside = middle;
                else outside = middle;
            }
            return inside;
        }
        return -1f;
    }

    static bool InsideAny(Collider[] boxes, Vector3 point)
    {
        foreach (var box in boxes)
        {
            for (float lift = 0f; lift <= 3f; lift += 1f)
            {
                var probe = point + Vector3.up * lift;
                if ((box.ClosestPoint(probe) - probe).sqrMagnitude < 0.0001f) return true;
            }
        }
        return false;
    }

    public static bool FinishLine(out Vector3 position, out Vector3 direction, out float distance)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        // a circuit the game has not built yet (lobby, scene load) throws inside GetRoutePoint
        if (circuit == null || circuit.Waypoints() == null || circuit.Length() <= 0f)
        {
            position = direction = Vector3.zero;
            distance = -1f;
            return false;
        }
        if (!ReferenceEquals(_finishCircuit, circuit) || _finishDistance < 0)
        {
            GameObject[] triggers;
            try { triggers = GameObject.FindGameObjectsWithTag("FinishLine"); } catch { triggers = null; }
            if (triggers == null || triggers.Length == 0) { position = direction = Vector3.zero; distance = -1f; return false; }

            // Map the road once, then read the answer off it. Sample the whole route, then for each object wearing the
            // finish tag find where the road crosses it. A map can tag more than one gate, and a lap crosses its own
            // line leaving the grid, so anything in the first part of the route is the start, not the finish: the
            // answer is the furthest crossing along the road.
            float loop = circuit.Length() > 0 ? circuit.Length() : 5000f;
            var positions = new List<Vector3>((int)(loop / SampleEvery) + 2); var along = new List<float>(positions.Capacity);
            var directions = new List<Vector3>(positions.Capacity);
            for (float at = 0f; at <= loop; at += SampleEvery)
            {
                var point = circuit.GetRoutePoint(at);
                positions.Add(point.Position()); along.Add(at); directions.Add(point.Direction());
            }

            float best = -1f; var bestLine = triggers[0].transform.position; var bestFacing = Vector3.forward;
            float earliest = loop * 0.35f;   // a crossing before this is the start line, whatever it is tagged
            foreach (var trigger in triggers)
            {
                if (trigger == null) continue;
                var line = trigger.transform.position;
                // A finish line is built square across the road, so the road's direction where it meets the line is
                // the line's normal: which side of it a point sits on.
                int nearest = 0; float nearestError = float.MaxValue;
                for (int i = 0; i < positions.Count; i++)
                {
                    float error = Vector3.Distance(positions[i], line);
                    if (error < nearestError) { nearestError = error; nearest = i; }
                }
                if (nearestError > 60f) continue;                       // the road never really comes to this gate
                // Exact: where the road first runs into the gate's trigger box (that touch is what finishes a car).
                // The pivot of the gate object is not the box, so the plane through it is only the fallback.
                float crossing = TriggerEntry(circuit, trigger, positions, along, earliest);
                if (crossing < 0f) crossing = Pure.FinishCrossing(positions, along, line, directions[nearest]);
                if (crossing < earliest) crossing = along[nearest] < earliest ? -1f : along[nearest];
                if (crossing > best) { best = crossing; bestLine = line; bestFacing = directions[nearest]; }
            }

            bestFacing.y = 0f; bestFacing.Normalize();
            _finishPosition = bestLine; _finishDirection = bestFacing;
            _finishDistance = best; _finishCircuit = circuit; _raceDistance = best;
        }
        position = _finishPosition; direction = _finishDirection; distance = _finishDistance; return true;
    }
}
