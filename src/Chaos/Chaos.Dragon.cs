// Chaos.Dragon: one or two low-poly dragons (red / green, built from primitives with flapping membrane wings) appear
// high in the sky and make attack runs for ~25 s (Chaos.Swoop): each picks a racer, dives at it and spits a fireball
// from close range, pulls up and climbs back for the next. A hit sets the car on fire (see Chaos.Fire: a random fuse,
// then boom, spreading to cars it touches). Then they climb away.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const float DragonSeconds = 25f;          // how long they circle before leaving
    const float DragonLeaveSeconds = 4f;      // the climb out after that
    const float DragonScale = 1.6f;           // the whole model (about 9 m nose to tail at 1)
    const float DragonHigh = 70f;             // m above its target where it rolls into a dive (it appears this high)
    const float DragonDiveSpeed = 42f;        // m/s
    const float DragonClimbSpeed = 26f;       // m/s, and climbing away at the end x1.5
    const float DragonFireRange = 30f;        // spits its fireball this close to the car...
    const float DragonFireHeight = 14f;       // ...or this little above it
    const float DragonLeadAhead = 15f;        // where they leave from: this far ahead of the leading cars on the route
    const float DragonFlapSpeed = 5f;         // wing beat, radians a second
    const float DragonFlapDegrees = 35f;
    const float FireballSpeed = 45f;          // m/s (cars do ~20-35)
    const string FireBy = "dragon fire";

    static readonly Color FireballColor = new(1f, 0.45f, 0.05f);
    static readonly Color FireballCore = new(1f, 0.9f, 0.4f);
    static readonly Color HornColor = new(0.95f, 0.9f, 0.78f);

    // The colours of one dragon.
    class DragonLook
    {
        public Color Body;
        public Color Belly;
        public Color Wing;
        public Color Eye;
    }

    static readonly DragonLook[] DragonLooks =
    {
        new DragonLook
        {
            Body = new Color(0.72f, 0.1f, 0.08f),
            Belly = new Color(0.95f, 0.7f, 0.25f),
            Wing = new Color(0.42f, 0.05f, 0.05f),
            Eye = new Color(1f, 0.9f, 0.2f),
        },
        new DragonLook
        {
            Body = new Color(0.16f, 0.52f, 0.2f),
            Belly = new Color(0.78f, 0.82f, 0.32f),
            Wing = new Color(0.07f, 0.28f, 0.12f),
            Eye = new Color(1f, 0.25f, 0.1f),
        },
    };

    static readonly SwoopStyle DragonStyle = new()
    {
        High = DragonHigh,
        DiveSpeed = DragonDiveSpeed,
        ClimbSpeed = DragonClimbSpeed,
        Turn = 1.3f,
        DiveFrom = 130f,
        ClimbBack = 90f,
        AttackHeight = DragonFireHeight,
        AttackRange = DragonFireRange,
        Floor = 6f,
    };

    // One flying dragon: its flight (Chaos.Swoop) and its model's moving parts.
    class DragonRig : Swooper
    {
        public Transform Neck;
        public Transform Mouth;
        public Transform LeftWing;
        public Transform RightWing;
        public List<Transform> Tail = new();
        public float Phase;         // so two dragons don't flap in step
    }

    static IEnumerator Dragon()
    {
        Announce("DRAGON ATTACK", "DRAGON");
        var centre = PackCentre();
        int count = Random.Range(1, 3);
        int firstLook = Random.Range(0, DragonLooks.Length);
        var rigs = new List<DragonRig>();
        for (int i = 0; i < count; i++)
        {
            rigs.Add(BuildDragon(DragonLooks[(firstLook + i) % DragonLooks.Length], centre, i));
        }
        for (float time = 0f; time < DragonSeconds; time += Time.deltaTime)
        {
            centre = Vector3.Lerp(centre, PackCentre(), 1f - Mathf.Exp(-Time.deltaTime * 2f));
            foreach (var rig in rigs)
            {
                FlyDragon(rig, centre, time);
            }
            yield return null;
        }
        for (float time = 0f; time < DragonLeaveSeconds; time += Time.deltaTime)
        {
            foreach (var rig in rigs)
            {
                LeaveDragon(rig, centre, time);
            }
            yield return null;
        }
        foreach (var rig in rigs.Where(rig => rig.Body != null))
        {
            Object.Destroy(rig.Body.gameObject);
        }
    }

    // Where the dragons circle: the road a little ahead of the three leading cars (their average position when the
    // route is unknown). Last known centre stays put while nobody is racing.
    static Vector3 PackCentre()
    {
        var leaders = Racers().OrderByDescending(vehicle => vehicle.Progress()).Take(3).ToList();
        if (leaders.Count == 0)
        {
            return RoadAt(0f);
        }
        if (RouteLength > 0f)
        {
            return RoadAt(leaders.Average(vehicle => vehicle.Progress()) + DragonLeadAhead);
        }
        return leaders.Aggregate(Vector3.zero, (sum, vehicle) => sum + CarPosition(vehicle)) / leaders.Count;
    }

    // ---- flight ----

    // One frame of its attack run; on the frame it is close enough it spits a fireball at its target.
    static void FlyDragon(DragonRig rig, Vector3 centre, float time)
    {
        if (rig.Body == null)
        {
            return;
        }
        if (rig.Target == null || rig.Target.Car() == null || rig.Target.HasFinished())
        {
            rig.Target = RandomRacer();
        }
        var target = rig.Target;
        var next = SwoopStep(rig, DragonStyle, out bool attack);
        MoveDragon(rig, next, time);
        if (attack)
        {
            ShootFireball(rig, target);
            rig.Target = RandomRacer();
        }
    }

    // Climbs up and away from the pack, flapping hard.
    static void LeaveDragon(DragonRig rig, Vector3 centre, float time)
    {
        if (rig.Body == null)
        {
            return;
        }
        var away = rig.Body.position - centre;
        away.y = 0f;
        away = away.sqrMagnitude < 1f ? Vector3.forward : away.normalized;
        var heading = (away + Vector3.up * 0.8f).normalized;
        MoveDragon(rig, rig.Body.position + heading * DragonClimbSpeed * 1.5f * Time.deltaTime, DragonSeconds + time * 1.5f);
    }

    // Puts the body at `next`, turned (and banked) toward where it is going, and animates wings, tail and neck.
    static void MoveDragon(DragonRig rig, Vector3 next, float time)
    {
        var moved = next - rig.Body.position;
        rig.Body.position = next;
        if (moved.sqrMagnitude > 1e-4f)
        {
            float bank = Mathf.Clamp(-TurnRate(rig.Body.forward, moved) * 0.4f, -35f, 35f);
            var facing = Quaternion.LookRotation(moved.normalized) * Quaternion.Euler(0f, 0f, bank);
            rig.Body.rotation = Quaternion.Slerp(rig.Body.rotation, facing, 1f - Mathf.Exp(-Time.deltaTime * 3f));
        }
        float flap = Mathf.Sin(time * DragonFlapSpeed + rig.Phase) * DragonFlapDegrees + 8f;
        rig.RightWing.localRotation = Quaternion.Euler(0f, 0f, flap);
        rig.LeftWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
        for (int i = 0; i < rig.Tail.Count; i++)
        {
            rig.Tail[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 2.2f - i * 0.7f + rig.Phase) * 9f, 0f);
        }
        rig.Neck.localRotation = Quaternion.Euler(Mathf.Sin(time * 1.3f + rig.Phase) * 6f, 0f, 0f);
    }

    // Degrees a second the heading swings (+ = turning right), for banking into the turn.
    static float TurnRate(Vector3 forward, Vector3 moved)
    {
        forward.y = 0f;
        moved.y = 0f;
        if (forward.sqrMagnitude < 1e-6f || moved.sqrMagnitude < 1e-6f || Time.deltaTime <= 0f)
        {
            return 0f;
        }
        return Vector3.SignedAngle(forward, moved, Vector3.up) / Time.deltaTime;
    }

    // ---- fireballs ----

    // A glowing orange ball with a fire trail from the mouth, homing onto `target`; a hit sets the car alight.
    static void ShootFireball(DragonRig rig, Vehicle target)
    {
        if (target == null || target.Car() == null || rig.Mouth == null)
        {
            return;
        }
        var breath = Puff(rig.Mouth, FireballColor, FlameEnd, 25, 1.2f, 6f, 0.5f);
        Object.Destroy(breath.gameObject, 1.5f);
        var shot = Holder("fireball");
        shot.position = rig.Mouth.position;
        Prim(PrimitiveType.Sphere, shot, Vector3.zero, Vector3.one * 1.3f, FireballColor, glow: true);
        Prim(PrimitiveType.Sphere, shot, Vector3.zero, Vector3.one * 0.8f, FireballCore, glow: true);
        var light = shot.gameObject.AddComponent<Light>();
        light.color = FireballColor;
        light.range = 12f;
        light.intensity = 3f;
        var trail = Stream(shot, FireballColor, FlameEnd, 70f, 0.9f, 0.6f, 0.5f);
        Spawn(Projectile(shot, target, FireballSpeed,
            vehicle => FireballHit(vehicle, trail),
            point => FireballFizzle(point, trail)));
    }

    static void FireballHit(Vehicle vehicle, ParticleSystem trail)
    {
        Ignite(vehicle, FireBy);
        FireballFizzle(CarPosition(vehicle) + Vector3.up, trail);
    }

    // The ball is gone (hit or target lost): a small burst where it ended, and its trail lingers a moment.
    static void FireballFizzle(Vector3 point, ParticleSystem trail)
    {
        var burst = Holder("fireball-burst");
        burst.position = point;
        Puff(burst, FireballColor, FlameEnd, 30, 1.3f, 5f, 0.6f);
        Object.Destroy(burst.gameObject, 2f);
        if (trail == null)
        {
            return;
        }
        trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        trail.transform.SetParent(Root.transform, true);
        Object.Destroy(trail.gameObject, 1f);
    }

    // ---- the model (local +z = forward) ----

    static DragonRig BuildDragon(DragonLook look, Vector3 centre, int index)
    {
        var body = Holder("dragon");
        body.localScale = Vector3.one * DragonScale;
        var rig = new DragonRig
        {
            Body = body,
            Phase = index * 1.7f,
        };
        // appears high in the sky behind the field, each on its own side, already flying toward the pack
        body.position = SwoopEntry(DragonHigh, index % 2 == 0 ? 1f : -1f);
        var toward = centre - body.position;
        toward.y = 0f;
        body.rotation = Quaternion.LookRotation(toward.sqrMagnitude > 1f ? toward.normalized : Vector3.forward);
        rig.Velocity = body.forward * DragonClimbSpeed;
        Prim(PrimitiveType.Sphere, body, Vector3.zero, new Vector3(1.6f, 1.4f, 3.2f), look.Body);
        Prim(PrimitiveType.Sphere, body, new Vector3(0f, -0.3f, 0.1f), new Vector3(1.3f, 1f, 2.6f), look.Belly);
        BuildSpines(body, look);
        BuildLegs(body, look);
        rig.Neck = BuildNeck(body, look, out rig.Mouth);
        BuildTail(rig, look);
        rig.RightWing = BuildWing(body, look, 1f);
        rig.LeftWing = BuildWing(body, look, -1f);
        return rig;
    }

    // Neck (three segments rising forward) and the head on it; `mouth` is where fireballs leave.
    static Transform BuildNeck(Transform body, DragonLook look, out Transform mouth)
    {
        var neck = Holder("neck", body);
        neck.localPosition = new Vector3(0f, 0.3f, 1.3f);
        for (int i = 0; i < 3; i++)
        {
            var at = new Vector3(0f, 0.35f + i * 0.4f, 0.4f + i * 0.55f);
            Prim(PrimitiveType.Sphere, neck, at, new Vector3(0.8f, 0.8f, 1.1f) * (1f - i * 0.12f), look.Body,
                new Vector3(-35f, 0f, 0f));
        }
        var head = Holder("head", neck);
        head.localPosition = new Vector3(0f, 1.5f, 1.9f);
        Prim(PrimitiveType.Cube, head, Vector3.zero, new Vector3(0.85f, 0.65f, 1.1f), look.Body);
        Prim(PrimitiveType.Cube, head, new Vector3(0f, -0.12f, 0.85f), new Vector3(0.62f, 0.4f, 0.9f), look.Body);
        Prim(PrimitiveType.Cube, head, new Vector3(0f, -0.32f, 0.7f), new Vector3(0.55f, 0.12f, 1f), look.Belly);
        for (int side = -1; side <= 1; side += 2)
        {
            Prim(PrimitiveType.Cube, head, new Vector3(side * 0.28f, 0.45f, -0.45f), new Vector3(0.14f, 0.14f, 0.9f),
                HornColor, new Vector3(-35f, side * 15f, 0f));
            Prim(PrimitiveType.Sphere, head, new Vector3(side * 0.4f, 0.14f, 0.3f), Vector3.one * 0.2f, look.Eye,
                glow: true);
            Prim(PrimitiveType.Sphere, head, new Vector3(side * 0.18f, -0.02f, 1.3f), Vector3.one * 0.1f,
                Color.black);
        }
        mouth = Holder("mouth", head);
        mouth.localPosition = new Vector3(0f, -0.2f, 1.45f);
        return neck;
    }

    // Tail: a chain of shrinking segments, each a child of the one before so the wave adds up to the tip.
    static void BuildTail(DragonRig rig, DragonLook look)
    {
        var parent = rig.Body;
        var at = new Vector3(0f, 0.05f, -1.4f);
        for (int i = 0; i < 6; i++)
        {
            var segment = Holder("tail", parent);
            segment.localPosition = at;
            float size = 1f - i * 0.13f;
            Prim(PrimitiveType.Sphere, segment, new Vector3(0f, 0f, -0.45f), new Vector3(0.9f, 0.8f, 1.3f) * size,
                look.Body);
            rig.Tail.Add(segment);
            parent = segment;
            at = new Vector3(0f, 0f, -0.8f * size);
        }
        Prim(PrimitiveType.Cube, parent, new Vector3(0f, 0f, -0.9f), new Vector3(0.05f, 0.5f, 0.5f), look.Wing,
            new Vector3(45f, 0f, 0f));
    }

    // A row of horn-coloured spines along the back.
    static void BuildSpines(Transform body, DragonLook look)
    {
        for (int i = 0; i < 4; i++)
        {
            Prim(PrimitiveType.Cube, body, new Vector3(0f, 0.72f, 0.9f - i * 0.6f), new Vector3(0.08f, 0.35f, 0.3f),
                look.Wing, new Vector3(-30f, 0f, 0f));
        }
    }

    // Four legs tucked under the belly for flight.
    static void BuildLegs(Transform body, DragonLook look)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Prim(PrimitiveType.Cube, body, new Vector3(side * 0.5f, -0.6f, 0.9f), new Vector3(0.25f, 0.25f, 0.7f),
                look.Body, new Vector3(30f, 0f, 0f));
            Prim(PrimitiveType.Cube, body, new Vector3(side * 0.55f, -0.55f, -0.9f), new Vector3(0.3f, 0.3f, 0.8f),
                look.Body, new Vector3(40f, 0f, 0f));
        }
    }

    // One wing on side `side` (+1 right, -1 left): a pivot at the shoulder (flapped about local z), a two-sided
    // membrane and an arm bone along its leading edge.
    static Transform BuildWing(Transform body, DragonLook look, float side)
    {
        var pivot = Holder(side > 0f ? "wing-right" : "wing-left", body);
        pivot.localPosition = new Vector3(side * 0.6f, 0.5f, 0.4f);
        var edge = new[]
        {
            new Vector3(0f, 0f, 0.5f),        // shoulder front
            new Vector3(2.8f, 0.4f, 0.9f),    // elbow
            new Vector3(6f, 0f, -0.4f),       // tip
            new Vector3(4f, 0f, -1.9f),       // trailing scallop
            new Vector3(2f, 0f, -1.5f),
            new Vector3(0f, 0f, -1.5f),       // shoulder back
        };
        var points = edge.Select(point => new Vector3(point.x * side, point.y, point.z)).ToArray();
        MeshShape("membrane", pivot, TwoSided(points), WingTriangles(points.Length), look.Wing);
        Bone(pivot, points[0], points[1], 0.14f, look.Body);
        Bone(pivot, points[1], points[2], 0.1f, look.Body);
        return pivot;
    }

    // The points twice: the second copy carries the back faces (own normals, so lighting works from below too).
    static Vector3[] TwoSided(Vector3[] points) => points.Concat(points).ToArray();

    // A fan from the shoulder over `count` edge points, wound both ways (front copy, then back copy).
    static int[] WingTriangles(int count)
    {
        var triangles = new List<int>();
        for (int i = 1; i < count - 1; i++)
        {
            AddTriangle(triangles, 0, i, i + 1);
            AddTriangle(triangles, count, count + i + 1, count + i);
        }
        return triangles.ToArray();
    }

    // A cylinder from `from` to `to` in `parent`'s space.
    static void Bone(Transform parent, Vector3 from, Vector3 to, float thickness, Color color)
    {
        var bone = Prim(PrimitiveType.Cylinder, parent, (from + to) / 2f,
            new Vector3(thickness, Vector3.Distance(from, to) / 2f, thickness), color);
        bone.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
    }
}
