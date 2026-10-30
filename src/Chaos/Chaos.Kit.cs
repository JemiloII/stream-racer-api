// Chaos.Kit: what every chaos event builds with. Primitive shapes without colliders, cached materials, the racing
// cars, the road (route points + ground height), explosions that boom whoever is near, particle puffs and a
// projectile that homes onto a car. All of it parents under Chaos.Root so a clear removes it.
using System.Collections;
using UnityEngine.Rendering;

namespace StreamRacerApi;

static partial class Chaos
{
    // Runs `routine` on the chaos root: it stops when the root is cleared (race end, new lobby, Ranked, /chaos/clear).
    public static Coroutine Spawn(IEnumerator routine) => Root.GetComponent<ChaosHost>().StartCoroutine(routine);

    // ---- materials ----

    static readonly Dictionary<(Color, bool), Material> _materials = new();
    static Shader _lit, _unlit;

    // Lit = shaded like the game's own low-poly props; glow = unlit, always full colour (fire, magic, the sky's things).
    public static Material Mat(Color color, bool glow = false)
    {
        if (_materials.TryGetValue((color, glow), out var cached) && cached != null)
        {
            return cached;
        }
        _lit ??= Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
        _unlit ??= Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        var material = new Material(glow || _lit == null ? _unlit : _lit)
        {
            color = color,
        };
        _materials[(color, glow)] = material;
        return material;
    }

    // ---- shapes ----

    // A primitive with no collider (so it never bumps a car), under `parent`, placed in the parent's local space.
    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color,
        Vector3 euler = default, bool glow = false)
    {
        var shape = GameObject.CreatePrimitive(type);
        Object.Destroy(shape.GetComponent<Collider>());
        shape.transform.SetParent(parent != null ? parent : Root.transform, false);
        shape.transform.localPosition = position;
        shape.transform.localRotation = Quaternion.Euler(euler);
        shape.transform.localScale = scale;
        var renderer = shape.GetComponent<Renderer>();
        renderer.sharedMaterial = Mat(color, glow);
        renderer.shadowCastingMode = glow ? ShadowCastingMode.Off : ShadowCastingMode.On;
        return shape;
    }

    // An empty holder under the chaos root (or `parent`).
    public static Transform Holder(string name, Transform parent = null)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(parent != null ? parent : Root.transform, false);
        return holder;
    }

    // A mesh with flat-shaded triangles from raw points (for stars, wings, fins).
    public static GameObject MeshShape(string name, Transform parent, Vector3[] points, int[] triangles, Color color,
        bool glow = false)
    {
        var mesh = new Mesh
        {
            vertices = points,
            triangles = triangles,
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var shape = new GameObject(name);
        shape.transform.SetParent(parent != null ? parent : Root.transform, false);
        shape.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = shape.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Mat(color, glow);
        return shape;
    }

    // One triangle (three corner indices) onto a MeshShape index list.
    public static void AddTriangle(List<int> triangles, int first, int second, int third)
    {
        triangles.Add(first);
        triangles.Add(second);
        triangles.Add(third);
    }

    // ---- cars and road ----

    // Cars still racing (spawned, not finished).
    public static List<Vehicle> Racers() => Game.Running
        ? Game.Vehicles().Where(vehicle => vehicle.Car() != null && !vehicle.HasFinished()).ToList()
        : new List<Vehicle>();

    public static Vehicle RandomRacer()
    {
        var racers = Racers();
        return racers.Count > 0 ? racers[Random.Range(0, racers.Count)] : null;
    }

    public static Vector3 CarPosition(Vehicle vehicle) => vehicle.Car().transform.position;

    // Cars whose centre is within `radius` of `point` (flat distance, height ignored up to 6 m).
    public static List<Vehicle> CarsNear(Vector3 point, float radius) => Racers()
        .Where(vehicle =>
        {
            var offset = CarPosition(vehicle) - point;
            return Mathf.Abs(offset.y) < 6f && new Vector2(offset.x, offset.z).magnitude <= radius;
        })
        .ToList();

    static WaypointCircuit Circuit => Instances.WaypointController?.GetCircuit();

    public static float RouteLength => Circuit != null ? Circuit.Length() : 0f;

    // The road at `distance` along the route (wraps), and its direction.
    public static Vector3 RoadAt(float distance) =>
        Circuit != null ? Circuit.GetRoutePoint(distance).Position() : Vector3.zero;
    public static Vector3 RoadDirectionAt(float distance)
    {
        var direction = Circuit != null ? Circuit.GetRoutePoint(distance).Direction() : Vector3.forward;
        direction.y = 0f;
        return direction.sqrMagnitude < 1e-6f ? Vector3.forward : direction.normalized;
    }

    // The ground (road or grass) straight below/above `point`; the point itself when nothing is there.
    public static Vector3 Ground(Vector3 point)
    {
        var from = point + Vector3.up * 60f;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 200f).OrderBy(hit => hit.distance))
        {
            if (hit.collider.attachedRigidbody == null && !hit.collider.isTrigger)
            {
                return hit.point;
            }
        }
        return point;
    }

    // ---- effects ----

    // A boom: flash ball + fireball puff + light at `point`; every car within `radius` gets the game's boom by `by`.
    public static void Blast(Vector3 point, float radius, string by, Color? color = null)
    {
        var tint = color ?? new Color(1f, 0.55f, 0.1f);
        var holder = Holder("blast");
        holder.position = point;
        var ball = Prim(PrimitiveType.Sphere, holder, Vector3.zero, Vector3.one, tint, glow: true);
        var light = holder.gameObject.AddComponent<Light>();
        light.color = tint;
        light.range = radius * 4f;
        Puff(holder, tint, new Color(0.2f, 0.2f, 0.2f, 0f), 60, 1.5f, radius * 1.5f, 1.2f);
        Spawn(GrowAndFade(holder, ball.transform, light, radius * 2f, 0.6f));
        foreach (var vehicle in CarsNear(point, radius))
        {
            Game.StrikeNow(vehicle, by);
        }
    }

    static IEnumerator GrowAndFade(Transform holder, Transform ball, Light light, float size, float seconds)
    {
        for (float time = 0f; time < seconds && holder != null; time += Time.deltaTime)
        {
            float progress = time / seconds;
            ball.localScale = Vector3.one * Mathf.Lerp(0.5f, size, progress);
            light.intensity = Mathf.Lerp(8f, 0f, progress);
            yield return null;
        }
        if (holder != null)
        {
            ball.gameObject.SetActive(false);
            Object.Destroy(holder.gameObject, 2f);   // the smoke puff finishes on its own
        }
    }

    // One burst of `count` soft particles from `parent` (start colour fading to `end`), world-simulated.
    public static ParticleSystem Puff(Transform parent, Color start, Color end, int count, float size, float speed,
        float lifetime)
    {
        var system = Particles(parent, start, end, size, speed, lifetime);
        var emission = system.emission;
        emission.rateOverTime = 0f;
        system.Play();
        system.Emit(count);
        return system;
    }

    // A continuous stream (fire on a car, a dragon's breath, a broom's sparkle trail): `rate` particles a second.
    public static ParticleSystem Stream(Transform parent, Color start, Color end, float rate, float size, float speed,
        float lifetime)
    {
        var system = Particles(parent, start, end, size, speed, lifetime);
        var emission = system.emission;
        emission.rateOverTime = rate;
        system.Play();
        return system;
    }

    static ParticleSystem Particles(Transform parent, Color start, Color end, float size, float speed, float lifetime)
    {
        var holder = new GameObject("particles");
        holder.transform.SetParent(parent, false);
        var system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = start;
        main.maxParticles = 600;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(start, end);
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Weather.ParticleMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return system;
    }

    // Moves `shot` toward the car (re-aiming every frame) at `speed` m/s; `onHit` runs when it arrives, `onMiss` if
    // the car is gone first. The shot object is destroyed either way.
    public static IEnumerator Projectile(Transform shot, Vehicle target, float speed, System.Action<Vehicle> onHit,
        System.Action<Vector3> onMiss = null, float timeout = 8f)
    {
        for (float time = 0f; time < timeout && shot != null; time += Time.deltaTime)
        {
            if (target.Car() == null || target.HasFinished())
            {
                onMiss?.Invoke(shot.position);
                break;
            }
            var aim = CarPosition(target) + Vector3.up * 0.8f;
            shot.position = Vector3.MoveTowards(shot.position, aim, speed * Time.deltaTime);
            shot.LookAt(aim);
            if (Vector3.Distance(shot.position, aim) < 0.8f)
            {
                onHit(target);
                break;
            }
            yield return null;
        }
        if (shot != null)
        {
            Object.Destroy(shot.gameObject);
        }
    }

    // A point high above the road somewhere ahead of / behind a random car, for flyers to enter from.
    public static Vector3 SkyAbove(Vector3 point, float height) => point + Vector3.up * height;

    // A feed line for the event ("METEOR SHOWER", "DRAGON"...), with a short word in the icon spot.
    public static void Announce(string text, string word, Vehicle follow = null) => Game.FeedLine(text, word, follow);

    // ---- siren ----

    // Fallback air-raid siren, made here: two wails, each winding up to its top note, holding and
    // winding down. A rotor siren is a buzzy tone (odd harmonics) with a fast flutter from the chopping rotor.
    const int SirenRate = 22050;
    const float SirenSeconds = 14f, SirenVolume = 0.35f;
    const float SirenLow = 140f, SirenHigh = 620f;      // Hz: wound down / full speed
    static AudioClip _siren;

    // The real thing: a recorded pneumatic air-raid siren ("Pneumatic Air Raid Siren.wav" by Glosome, Wikimedia
    // Commons, CC BY-SA 4.0), fetched once into the BepInEx cache; the made-up one above plays until it is there.
    const string SirenUrl = "https://upload.wikimedia.org/wikipedia/commons/a/a6/Pneumatic_Air_Raid_Siren.wav";
    static string SirenFile => System.IO.Path.Combine(BepInEx.Paths.CachePath, "shibiko.streamracer.siren.wav");
    static AudioClip _realSiren;
    static Wav.Samples _sirenSamples;
    static bool _sirenFetching;

    static AudioClip RealSiren()
    {
        if (_realSiren != null)
        {
            return _realSiren;
        }
        if (_sirenSamples != null)
        {
            return _realSiren = Wav.Clip(_sirenSamples, "sr-siren-real");
        }
        if (!_sirenFetching)
        {
            _sirenFetching = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => LoadRealSiren());
        }
        return null;
    }

    // Off the main thread: download (first time only), decode, keep SirenSeconds of it, fade out, peak at 0.9.
    static void LoadRealSiren()
    {
        try
        {
            if (!System.IO.File.Exists(SirenFile))
            {
                var web = new System.Net.WebClient();
                web.Headers["User-Agent"] = "StreamRacerApi (https://github.com/JemiloII/stream-racer-api)";
                web.DownloadFile(SirenUrl, SirenFile + ".part");
                System.IO.File.Move(SirenFile + ".part", SirenFile);
            }
            var samples = Wav.Decode(System.IO.File.ReadAllBytes(SirenFile));
            if (samples == null)
            {
                System.IO.File.Delete(SirenFile);
                return;
            }
            int keep = System.Math.Min(samples.Data.Length, (int)(SirenSeconds * samples.Rate) * samples.Channels);
            var data = new float[keep];
            float peak = 0.0001f;
            for (int index = 0; index < keep; index++)
            {
                peak = System.Math.Max(peak, System.Math.Abs(samples.Data[index]));
            }
            int fade = (int)(1.5f * samples.Rate) * samples.Channels;
            for (int index = 0; index < keep; index++)
            {
                float tail = System.Math.Min(1f, (keep - index) / (float)fade);
                data[index] = samples.Data[index] / peak * 0.9f * tail;
            }
            samples.Data = data;
            _sirenSamples = samples;
        }
        catch (System.Exception error)
        {
            Plugin.Log.LogWarning("siren download failed, using the made-up one: " + error.Message);
        }
    }

    public static void Siren()
    {
        _siren = RealSiren() ?? (_siren != null && _siren.name == "sr-siren" ? _siren : MakeSiren());
        var source = Root.AddComponent<AudioSource>();   // goes with the chaos root on a clear
        source.clip = _siren;
        source.volume = SirenVolume;
        source.spatialBlend = 0f;
        source.Play();
        Object.Destroy(source, SirenSeconds + 0.5f);
    }

    static AudioClip MakeSiren()
    {
        var samples = new float[(int)(SirenRate * SirenSeconds)];
        double phase = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            float time = index / (float)SirenRate;
            float pitch = Mathf.Lerp(SirenLow, SirenHigh, SirenWind(time % (SirenSeconds / 2f)));
            phase += 2 * System.Math.PI * pitch / SirenRate;
            float tone = Mathf.Sin((float)phase) + Mathf.Sin((float)phase * 3f) / 3f + Mathf.Sin((float)phase * 5f) / 6f;
            float flutter = 0.85f + 0.15f * Mathf.Sin(time * 2f * Mathf.PI * 18f);
            float fade = Mathf.Clamp01(time / 0.3f) * Mathf.Clamp01((SirenSeconds - time) / 0.8f);
            samples[index] = tone * flutter * fade * 0.55f;
        }
        var clip = AudioClip.Create("sr-siren", samples.Length, 1, SirenRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // One 7 s wail: winds up for 2.5 s, holds 1.5 s, winds down for 3 s (0 = low note, 1 = top).
    static float SirenWind(float time) => time < 2.5f
        ? Mathf.SmoothStep(0f, 1f, time / 2.5f)
        : time < 4f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (time - 4f) / 3f);
}
