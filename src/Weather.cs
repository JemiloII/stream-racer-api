// Weather for the current race: rain, snow or fog, all built from code (no assets). Rain/snow particles follow the
// active camera, fog is Unity's built-in RenderSettings fog (the scene's own values are restored on clear), and
// while it rains or snows new road surfaces appear over time through Game.LaySurfaces. Ends on POST /weather/clear,
// after ?seconds=, at race end or a new lobby; surfaces already laid stay until /surface/clear or race end.
using System.Collections.Specialized;
using UnityEngine.Rendering;
using SurfaceKind = StreamRacerApi.Pure.SurfaceKind;

namespace StreamRacerApi;

enum WeatherKind
{
    Clear,
    Rain,
    Snow,
    Fog,
}

static class Weather
{
    // ---- tunables ----
    const float RainFogDensity = 0.004f, SnowFogDensity = 0.007f, FogOnlyDensity = 0.012f;   // exponential squared
    static readonly Color RainFogColor = new(0.55f, 0.58f, 0.62f);
    static readonly Color SnowFogColor = new(0.85f, 0.87f, 0.9f);
    static readonly Color FogOnlyColor = new(0.7f, 0.72f, 0.74f);
    const float FirstSpawnSeconds = 3f;
    const float LightningMinSeconds = 15f, LightningMaxSeconds = 45f;   // random gap between strikes while it rains
    const float LightningFlashSeconds = 0.25f;                          // how long the bolt and the flash last
    const float LightningHeight = 70f;                                  // the bolt starts this far above the car
    const float RainSpawnEvery = 10f;
    const int PuddlesPerSpawn = 2;
    const float SnowSpawnEvery = 12f, SnowSpawnLength = 35f;
    const float SnowIceChance = 0.25f, SnowIceLength = 30f;
    const float RainRate = 500f, RainLifetime = 1.1f, RainSpeed = 32f, RainSize = 0.05f, RainStretch = 0.06f;
    const int RainMaxParticles = 1500;
    const float SnowRate = 220f, SnowLifetime = 7f, SnowSpeed = 2.5f, SnowSize = 0.22f, SnowDrift = 0.8f;
    const int SnowMaxParticles = 2500;
    const float RainArea = 60f, SnowArea = 70f;          // emitter box side, centred on the camera
    const float EmitterHeight = 18f, EmitterAhead = 12f;  // above the camera, and pushed along its view
    static readonly Color RainDropColor = new(0.75f, 0.8f, 0.9f, 0.45f);
    static readonly Color SnowflakeColor = new(1f, 1f, 1f, 0.9f);

    public static WeatherKind Current { get; private set; } = WeatherKind.Clear;
    public static bool SnowOnGround => Current == WeatherKind.Snow;   // off-road = deep snow (Game.Surfaces)
    static readonly Color SnowGroundColor = new(0.93f, 0.95f, 0.98f);
    static readonly Dictionary<Renderer, Material[]> _groundMaterials = new();   // terrain renderer -> its own materials
    static float _until;            // unscaled time it ends; 0 = until cleared
    static float _nextSpawn;
    static float _nextStrike;
    static ParticleSystem _emitter;
    static Material _particleMaterial;
    static bool _fogSaved, _savedFog;
    static FogMode _savedFogMode;
    static Color _savedFogColor;
    static float _savedFogDensity;
    static bool _warned;

    // ---- API ----

    // POST /weather/rain|snow|fog?seconds=, POST /weather/clear.
    public static object Request(string target, NameValueCollection query, ref int status)
    {
        if (target == "clear")
        {
            Clear();
            return Dto();
        }
        var kind = Parse(target);
        if (kind == null)
        {
            status = 404;
            return new { error = "need /weather/rain|snow|fog|clear" };
        }
        if (!Game.Running && !Game.InLobby)
        {
            status = 409;
            return new { error = "no race or lobby" };
        }
        float seconds = float.TryParse(query["seconds"], out var parsedSeconds) ? Mathf.Max(0f, parsedSeconds) : 0f;
        Start(kind.Value, seconds);
        return Dto();
    }

    public static object Dto() => new
    {
        kind = Name(Current),
        secondsLeft = Current != WeatherKind.Clear && _until > 0f ? Mathf.Max(0f, _until - Time.unscaledTime) : 0f,
        untilCleared = Current != WeatherKind.Clear && _until <= 0f,
    };

    static string Name(WeatherKind kind) => kind.ToString().ToLowerInvariant();

    static WeatherKind? Parse(string name)
    {
        switch ((name ?? "").ToLowerInvariant())
        {
            case "rain": return WeatherKind.Rain;
            case "snow": return WeatherKind.Snow;
            case "fog": return WeatherKind.Fog;
            default: return null;
        }
    }

    // ---- lifecycle ----

    public static void Start(WeatherKind kind, float seconds)
    {
        StopParticles();
        Current = kind;
        _until = seconds > 0f ? Time.unscaledTime + seconds : 0f;
        _nextSpawn = Time.unscaledTime + FirstSpawnSeconds;
        _nextStrike = Time.unscaledTime + Random.Range(LightningMinSeconds, LightningMaxSeconds);
        Guarded(() => ApplyFog(kind));
        if (kind == WeatherKind.Rain || kind == WeatherKind.Snow)
        {
            Guarded(() => _emitter = BuildEmitter(kind));
        }
        Guarded(RestoreGround);
        if (kind == WeatherKind.Snow)
        {
            Guarded(PaintGroundSnow);
        }
        Plugin.Emit("weather", new { kind = Name(kind), seconds });
    }

    // Particles off, the scene's fog back. Laid surfaces are not touched.
    public static void Clear()
    {
        if (Current == WeatherKind.Clear)
        {
            return;
        }
        StopParticles();
        Guarded(RestoreFog);
        Guarded(RestoreGround);
        Current = WeatherKind.Clear;
        Plugin.Emit("weather", new { kind = Name(WeatherKind.Clear), seconds = 0f });
    }

    // Race over or a new lobby: weather and every laid surface go.
    public static void EndOfRace()
    {
        Clear();
        Game.ClearSurfaces();
    }

    public static void Tick()
    {
        if (Current == WeatherKind.Clear)
        {
            return;
        }
        if (_until > 0f && Time.unscaledTime >= _until)
        {
            Clear();
            return;
        }
        Guarded(FollowCamera);
        if (Game.Running && Time.unscaledTime >= _nextSpawn)
        {
            Guarded(SpawnSurfaces);
        }
        if (Current == WeatherKind.Rain && Game.Running && Time.unscaledTime >= _nextStrike)
        {
            _nextStrike = Time.unscaledTime + Random.Range(LightningMinSeconds, LightningMaxSeconds);
            Guarded(Strike);
        }
    }

    // Rain leaves puddles, snow leaves snow patches and now and then a sheet of ice on a corner.
    static void SpawnSurfaces()
    {
        if (Current == WeatherKind.Rain)
        {
            _nextSpawn = Time.unscaledTime + RainSpawnEvery;
            Game.LaySurfaces(SurfaceKind.Puddle, PuddlesPerSpawn, 0f);
            return;
        }
        if (Current == WeatherKind.Snow)
        {
            _nextSpawn = Time.unscaledTime + SnowSpawnEvery;
            Game.LaySurfaces(SurfaceKind.Snow, 1, SnowSpawnLength);
            if (Random.value < SnowIceChance)
            {
                Game.LaySurfaces(SurfaceKind.Ice, 1, SnowIceLength);
            }
            return;
        }
        _nextSpawn = float.MaxValue;   // fog lays nothing
    }

    // One warning for the whole session, never a crash or a line per frame.
    static void Guarded(System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception error)
        {
            if (_warned)
            {
                return;
            }
            _warned = true;
            Plugin.Log.LogWarning("weather: " + error.Message + " (further errors hidden)");
        }
    }

    // ---- fog ----

    static void ApplyFog(WeatherKind kind)
    {
        if (!_fogSaved)
        {
            _savedFog = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _fogSaved = true;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        bool rain = kind == WeatherKind.Rain, snow = kind == WeatherKind.Snow;
        RenderSettings.fogColor = rain ? RainFogColor : snow ? SnowFogColor : FogOnlyColor;
        RenderSettings.fogDensity = rain ? RainFogDensity : snow ? SnowFogDensity : FogOnlyDensity;
    }

    static void RestoreFog()
    {
        if (!_fogSaved)
        {
            return;
        }
        RenderSettings.fog = _savedFog;
        RenderSettings.fogMode = _savedFogMode;
        RenderSettings.fogColor = _savedFogColor;
        RenderSettings.fogDensity = _savedFogDensity;
        _fogSaved = false;
    }

    // ---- particles ----

    // The map's ground meshes (every renderer under an object named "Terrain", e.g. Grassland/Terrain) swapped to a
    // plain snow-white copy of their own material: a white tint would only multiply the green, so the texture goes.
    static void PaintGroundSnow()
    {
        var snowCopies = new Dictionary<Material, Material>();
        foreach (var root in Object.FindObjectsOfType<Transform>().Where(transform => transform.name == "Terrain"))
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (_groundMaterials.ContainsKey(renderer))
                {
                    continue;
                }
                _groundMaterials[renderer] = renderer.sharedMaterials;
                renderer.sharedMaterials = renderer.sharedMaterials.Select(material => SnowCopy(material, snowCopies)).ToArray();
            }
        }
    }

    static Material SnowCopy(Material original, Dictionary<Material, Material> copies)
    {
        if (original == null)
        {
            return null;
        }
        if (!copies.TryGetValue(original, out var copy))
        {
            copy = new Material(original) { name = original.name + " (snow)", mainTexture = null, color = SnowGroundColor };
            copies[original] = copy;
        }
        return copy;
    }

    static void RestoreGround()
    {
        foreach (var entry in _groundMaterials)
        {
            if (entry.Key != null)
            {
                entry.Key.sharedMaterials = entry.Value;
            }
        }
        _groundMaterials.Clear();
    }

    static void StopParticles()
    {
        if (_emitter != null)
        {
            Object.Destroy(_emitter.gameObject);
        }
        _emitter = null;
    }

    // Lightning: a jagged bolt from the sky onto a random car, a white flash of the scene's sun, and the car booms
    // "by storm" (the game's own boom: the RIP line in the feed says so).
    static void Strike()
    {
        var struck = Game.StrikeRandomCar("storm");
        if (struck == null)
        {
            return;
        }
        var target = struck.Car().transform.position;
        Plugin.Instance.StartCoroutine(Bolt(target));
        Plugin.Emit("lightning", new { login = Game.Login(struck), displayName = struck.Profile().DisplayName() });
    }

    static System.Collections.IEnumerator Bolt(Vector3 target)
    {
        var bolt = new GameObject("sr-lightning").AddComponent<LineRenderer>();
        bolt.material = ParticleMaterial();
        bolt.startColor = bolt.endColor = new Color(0.9f, 0.95f, 1f, 1f);
        bolt.startWidth = 1.2f;
        bolt.endWidth = 0.4f;
        bolt.shadowCastingMode = ShadowCastingMode.Off;
        const int points = 12;
        bolt.positionCount = points;
        for (int i = 0; i < points; i++)
        {
            float along = i / (float)(points - 1);
            var jitter = i == 0 || i == points - 1 ? Vector3.zero : new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            bolt.SetPosition(i, target + Vector3.up * LightningHeight * (1f - along) + jitter);
        }
        var sun = RenderSettings.sun;
        float before = sun != null ? sun.intensity : 0f;
        if (sun != null)
        {
            sun.intensity = before * 3f + 1f;
        }
        yield return new WaitForSeconds(LightningFlashSeconds);
        if (sun != null)
        {
            sun.intensity = before;
        }
        Object.Destroy(bolt.gameObject);
    }

    static void FollowCamera()
    {
        var camera = Camera.main;
        if (_emitter == null || camera == null)
        {
            return;
        }
        var view = camera.transform.forward;
        view.y = 0f;
        var above = Vector3.up * EmitterHeight;
        _emitter.transform.position = camera.transform.position + view.normalized * EmitterAhead + above;
    }

    // A box above the camera emitting straight down, simulated in world space so the camera can move through it.
    static ParticleSystem BuildEmitter(WeatherKind kind)
    {
        bool rain = kind == WeatherKind.Rain;
        var holder = new GameObject("sr-weather-" + Name(kind));
        Object.DontDestroyOnLoad(holder);
        holder.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // the box shape emits along its forward: down
        var system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = rain ? RainLifetime : SnowLifetime;
        main.startSpeed = rain ? RainSpeed : SnowSpeed;
        main.startSize = rain ? RainSize : SnowSize;
        main.startColor = rain ? RainDropColor : SnowflakeColor;
        main.maxParticles = rain ? RainMaxParticles : SnowMaxParticles;

        var emission = system.emission;
        emission.rateOverTime = rain ? RainRate : SnowRate;

        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        float area = rain ? RainArea : SnowArea;
        shape.scale = new Vector3(area, area, 1f);

        if (!rain)
        {
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = SnowDrift;
            noise.frequency = 0.3f;
        }

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = ParticleMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (rain)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = RainStretch;
            renderer.lengthScale = 1f;
        }
        system.Play();
        return system;
    }

    // A built-in blended shader found at runtime, textured with a soft round dot drawn here.
    static Material ParticleMaterial()
    {
        if (_particleMaterial != null)
        {
            return _particleMaterial;
        }
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null)
        {
            throw new System.InvalidOperationException("no particle shader in this build");
        }
        _particleMaterial = new Material(shader) { mainTexture = SoftDot() };
        return _particleMaterial;
    }

    static Texture2D SoftDot()
    {
        const int size = 16;
        var centre = new Vector2(size * 0.5f, size * 0.5f);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fromCentre = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) / (size * 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - fromCentre)));
            }
        }
        texture.Apply();
        return texture;
    }
}
