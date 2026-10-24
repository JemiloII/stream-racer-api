// Chaos.Meteor: a meteor shower. Glowing rocks streak in diagonally onto the road just ahead of racing cars (a red
// ring on the ground marks each spot while it falls), boom whoever they hit and leave a crater in the track. Craters
// stay until the race ends; a car that drives into one falls in and booms (TickHoles), ResetHoles clears them.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const int MeteorMin = 6;                    // meteors per shower (inclusive)
    const int MeteorMax = 12;
    const float ShowerSeconds = 12f;            // the shower is spread over this long
    const float FallMinSeconds = 1.5f;          // how long one meteor is visible before it lands
    const float FallMaxSeconds = 2.5f;
    const float MeteorHeight = 90f;             // where it starts above its target
    const float MeteorSlant = 55f;              // and how far off to the side (the diagonal)
    const float LeadExtra = 6f;                 // aim this far past where the car will be when it lands
    const float SideJitter = 3f;                // up to this far left/right of the road line
    const float MeteorBlastRadius = 5f;
    const float HoleRadius = 2.6f;              // a car whose centre gets this close falls in
    const float HoleHeightTolerance = 1.6f;     // ...when it is this near the crater's height (not jumping over it)
    const float HoleCooldown = 10f;             // the same crater can't take the same car again for this long
    const float HoleSmokeSeconds = 8f;          // the crater smokes for this long after the impact
    const int RimRocks = 12;

    static readonly Color RockDark = new(0.16f, 0.13f, 0.12f);
    static readonly Color RockCore = new(1f, 0.45f, 0.08f);
    static readonly Color WarningRed = new(1f, 0.1f, 0.08f);
    static readonly Color CraterDark = new(0.07f, 0.06f, 0.05f);
    static readonly Color CraterScorch = new(0.03f, 0.025f, 0.02f);
    static readonly Color[] RimColors =
    {
        new(0.36f, 0.27f, 0.19f),
        new(0.45f, 0.43f, 0.41f),
        new(0.28f, 0.24f, 0.21f),
    };

    // One crater left in the track and, per car, when it may take that car again.
    class Crater
    {
        public Vector3 Center;
        public Transform Visual;
        public readonly Dictionary<Vehicle, float> ReadyAt = new();
    }

    static readonly List<Crater> _craters = new();

    static IEnumerator Meteors()
    {
        if (RouteLength <= 0f || Racers().Count == 0)
        {
            yield break;
        }
        Announce("METEOR SHOWER", "METEOR");
        int count = Random.Range(MeteorMin, MeteorMax + 1);
        float gap = ShowerSeconds / count;
        for (int index = 0; index < count; index++)
        {
            Spawn(OneMeteor());
            yield return new WaitForSeconds(gap * Random.Range(0.6f, 1.4f));
        }
        yield return new WaitForSeconds(FallMaxSeconds + 0.5f);   // the last one lands before the event ends
    }

    // Picks a spot ahead of a random racer, drops a meteor on it, then leaves a crater there.
    static IEnumerator OneMeteor()
    {
        var racer = RandomRacer();
        if (racer == null)
        {
            yield break;
        }
        float fallSeconds = Random.Range(FallMinSeconds, FallMaxSeconds);
        var target = MeteorTarget(racer, fallSeconds);
        var slant = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * MeteorSlant;
        var start = target + slant + Vector3.up * MeteorHeight;

        var ring = WarningRing(target);
        var meteor = MeteorRock(start);
        for (float time = 0f; time < fallSeconds && meteor != null; time += Time.deltaTime)
        {
            float progress = time / fallSeconds;
            meteor.position = Vector3.Lerp(start, target, progress * progress);   // speeds up as it comes in
            meteor.Rotate(new Vector3(140f, 90f, 60f) * Time.deltaTime, Space.Self);
            PulseWarningRing(ring, time);
            yield return null;
        }
        if (ring != null)
        {
            Object.Destroy(ring.gameObject);
        }
        if (meteor == null)
        {
            yield break;
        }
        LetTrailFade(meteor);
        Object.Destroy(meteor.gameObject);
        Blast(target, MeteorBlastRadius, "meteor");
        MakeHole(target);
    }

    // Where the car will roughly be when the meteor lands (route distance + speed x fall time), on the ground.
    static Vector3 MeteorTarget(Vehicle racer, float fallSeconds)
    {
        var body = racer.Car().GetComponent<Rigidbody>();
        float speed = body != null ? new Vector3(body.velocity.x, 0f, body.velocity.z).magnitude : 15f;
        float distance = racer.Progress() + speed * fallSeconds + LeadExtra;
        var side = Vector3.Cross(Vector3.up, RoadDirectionAt(distance));
        return Ground(RoadAt(distance) + side * Random.Range(-SideJitter, SideJitter));
    }

    // ---- the rock ----

    // A lumpy dark rock around a glowing core, trailing fire and smoke.
    static Transform MeteorRock(Vector3 start)
    {
        var meteor = Holder("meteor");
        meteor.position = start;
        Prim(PrimitiveType.Sphere, meteor, Vector3.zero, Vector3.one * 1.5f, RockCore, glow: true);
        for (int index = 0; index < 6; index++)
        {
            var offset = Random.onUnitSphere * 0.55f;
            var scale = Vector3.one * Random.Range(0.9f, 1.3f);
            Prim(PrimitiveType.Sphere, meteor, offset, scale, RockDark, Random.rotation.eulerAngles);
        }
        var light = meteor.gameObject.AddComponent<Light>();
        light.color = RockCore;
        light.range = 14f;
        light.intensity = 3f;
        Stream(meteor, new Color(1f, 0.6f, 0.15f), new Color(1f, 0.15f, 0f, 0f), 70f, 1.6f, 0.5f, 0.6f);
        Stream(meteor, new Color(0.3f, 0.28f, 0.26f, 0.8f), new Color(0.2f, 0.2f, 0.2f, 0f), 30f, 2.4f, 0.3f, 1.8f);
        return meteor;
    }

    // Unhooks the trail particles so they fade out where they are instead of vanishing with the rock.
    static void LetTrailFade(Transform meteor)
    {
        foreach (var system in meteor.GetComponentsInChildren<ParticleSystem>())
        {
            system.transform.SetParent(Root.transform, true);
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(system.gameObject, 2.5f);
        }
    }

    // ---- the warning ring ----

    // A ring of glowing red segments plus a faint red disc, flat on the ground at the impact spot.
    static Transform WarningRing(Vector3 target)
    {
        var ring = Holder("meteor-warning");
        ring.position = target + Vector3.up * 0.08f;
        ring.rotation = Quaternion.FromToRotation(Vector3.up, MeteorGroundNormal(target));
        var faint = new Color(WarningRed.r, WarningRed.g, WarningRed.b, 0.25f);
        float across = MeteorBlastRadius * 2f;
        Prim(PrimitiveType.Cylinder, ring, Vector3.zero, new Vector3(across, 0.01f, across), faint, glow: true);
        const int segments = 16;
        for (int index = 0; index < segments; index++)
        {
            float angle = index * 360f / segments;
            var position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * MeteorBlastRadius;
            var scale = new Vector3(MeteorBlastRadius * 0.28f, 0.06f, 0.35f);
            Prim(PrimitiveType.Cube, ring, position, scale, WarningRed, new Vector3(0f, angle + 90f, 0f), glow: true);
        }
        return ring;
    }

    static void PulseWarningRing(Transform ring, float time)
    {
        if (ring == null)
        {
            return;
        }
        ring.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(time * 14f));
        ring.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
    }

    // The ground's tilt at `point`, from two nearby ground samples (flat up when there is nothing to sample).
    static Vector3 MeteorGroundNormal(Vector3 point)
    {
        var center = Ground(point);
        var east = Ground(point + Vector3.right);
        var north = Ground(point + Vector3.forward);
        var normal = Vector3.Cross(north - center, east - center);
        return normal.sqrMagnitude > 1e-6f && normal.y > 0.5f * normal.magnitude ? normal.normalized : Vector3.up;
    }

    // ---- craters ----

    // A dark scorched disc just above the ground, a rim of rocks and a little smoke; it stays until the race ends.
    static void MakeHole(Vector3 center)
    {
        var crater = Holder("meteor-crater");
        crater.position = center + Vector3.up * 0.04f;
        crater.rotation = Quaternion.FromToRotation(Vector3.up, MeteorGroundNormal(center));
        float size = HoleRadius * 2f;
        Prim(PrimitiveType.Cylinder, crater, Vector3.zero, new Vector3(size, 0.01f, size), CraterDark);
        Prim(PrimitiveType.Cylinder, crater, Vector3.up * 0.01f, new Vector3(size * 0.65f, 0.01f, size * 0.65f),
            CraterScorch);
        for (int index = 0; index < RimRocks; index++)
        {
            RimRock(crater, index * 360f / RimRocks + Random.Range(-10f, 10f));
        }
        var smokeStart = new Color(0.25f, 0.24f, 0.23f, 0.7f);
        var smoke = Stream(crater, smokeStart, new Color(0.3f, 0.3f, 0.3f, 0f), 5f, 2f, 1f, 3.5f);
        Spawn(StopCraterSmoke(smoke, HoleSmokeSeconds));
        _craters.Add(new Crater
        {
            Center = center,
            Visual = crater,
        });
    }

    static void RimRock(Transform crater, float angle)
    {
        float distance = HoleRadius * Random.Range(0.95f, 1.15f);
        var position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
        var scale = new Vector3(Random.Range(0.35f, 0.75f), Random.Range(0.2f, 0.45f), Random.Range(0.35f, 0.75f));
        var color = RimColors[Random.Range(0, RimColors.Length)];
        var type = Random.value < 0.5f ? PrimitiveType.Cube : PrimitiveType.Sphere;
        Prim(type, crater, position + Vector3.up * scale.y * 0.3f, scale, color, Random.rotation.eulerAngles);
    }

    static IEnumerator StopCraterSmoke(ParticleSystem system, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (system != null)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // Every frame: a racing car that drives into a crater falls in.
    static partial void TickHoles()
    {
        if (_craters.Count == 0)
        {
            return;
        }
        var racers = Racers();
        foreach (var hole in _craters)
        {
            foreach (var vehicle in racers)
            {
                CheckHole(hole, vehicle);
            }
        }
    }

    static void CheckHole(Crater hole, Vehicle vehicle)
    {
        var offset = CarPosition(vehicle) - hole.Center;
        if (Mathf.Abs(offset.y) > HoleHeightTolerance || new Vector2(offset.x, offset.z).magnitude >= HoleRadius)
        {
            return;
        }
        if (hole.ReadyAt.TryGetValue(vehicle, out float readyAt) && Time.unscaledTime < readyAt)
        {
            return;
        }
        hole.ReadyAt[vehicle] = Time.unscaledTime + HoleCooldown;
        Game.StrikeNow(vehicle, "a meteor crater");
    }

    static partial void ResetHoles()
    {
        foreach (var hole in _craters.Where(hole => hole.Visual != null))
        {
            Object.Destroy(hole.Visual.gameObject);
        }
        _craters.Clear();
    }
}
