// Chaos.BlackHole: night falls and a black hole takes the sun's place in the sky (a black ball, a glowing tilted
// accretion disc that turns, a lensing ring and a halo). If the map is already at night the sky is left alone (moon
// and all) and the hole hangs high on the other side. Any racing car that gets air for a moment is sucked up toward
// it, spinning and shrinking, then put back on the road and handed to the game's own respawn. The sky is captured
// first and put back exactly at the end, or by Chaos.Clear (OnClear) if the event is cut short; ResetBlackHole puts
// back any car that is mid-flight.
using System.Collections;
using Borodar.FarlandSkies.LowPoly;
using UnityEngine.Rendering;

namespace StreamRacerApi;

static partial class Chaos
{
    const float VoidSeconds = 25f;              // the whole event, fades included
    const float VoidFadeSeconds = 2.5f;         // night falls / day comes back this long
    const float VoidSkyDistance = 700f;         // hole distance from the camera (nearer if the camera can't see that far)
    const float VoidAngularRadius = 4.5f;       // degrees: how big the black ball looks
    const float VoidDayElevation = 20f;         // a low sun's spot is raised to this (degrees up)
    const float VoidViewElevation = 16f;        // degrees above the horizon, ahead of the camera
    const float VoidSunInView = 40f;            // by day the sun's spot is used when within this many degrees of "ahead"
    const float VoidAirSeconds = 0.3f;          // all wheels off the ground this long = eaten
    const float VoidAirSpeed = 5f;              // ...while moving at least this fast flat (a respawn drop isn't a jump)
    const float VoidEatSeconds = 6f;            // the flight up into the hole: a slow float off the road, then faster
    const float VoidCamBack = 20f, VoidCamUp = 2.5f;   // the watching camera: this far behind the car (away from the hole)
    const float VoidPullDistance = 160f;        // how far toward the hole the car flies in that time
    const float VoidSwirlRadius = 3f;           // it corkscrews this wide at the start, tightening to nothing
    const float VoidSpinSpeed = 540f;           // deg/s tumble on the way up
    const float VoidSmallest = 0.03f;           // share of its size left when it vanishes
    const float VoidImmunity = 6f;              // a car that was spat back out is left alone this long
    const float VoidDiscSpin = 2f;              // rad/s the disc's bright streaks travel round
    const int VoidDiscSegments = 96;            // around a full ring
    const float VoidSunIntensity = 0.12f;       // the scene's sun, dimmed to moonlight
    const float VoidAmbientShare = 0.18f;       // skybox-lit ambient is scaled down to this
    const float VoidReflectionShare = 0.25f;
    const float VoidSkyboxExposure = 0.15f;     // plain (non-Farland) skyboxes: exposure and tints scaled to this
    const float VoidFogDensity = 0.0035f;       // exponential squared, only used when the map had no fog of its own

    static readonly Color VoidSunColor = new(0.45f, 0.5f, 0.8f);
    static readonly Color VoidAmbientColor = new(0.05f, 0.06f, 0.12f);
    static readonly Color VoidFogColor = new(0.04f, 0.05f, 0.12f);
    static readonly Color VoidSkyTop = new(0.01f, 0.01f, 0.04f);
    static readonly Color VoidSkyMiddle = new(0.04f, 0.05f, 0.12f);
    static readonly Color VoidSkyBottom = new(0.02f, 0.02f, 0.06f);
    static readonly Color VoidCloudsTint = new(0.12f, 0.12f, 0.2f);
    static readonly Color VoidStarsTint = new(1f, 1f, 1f, 1f);
    static readonly Color VoidSpace = new(0.01f, 0.01f, 0.03f);
    static readonly Color[] VoidBandColors =   // the disc from its inner edge out: white hot, orange, faded purple
    {
        new(1f, 0.88f, 0.65f, 1f),
        new(1f, 0.45f, 0.1f, 0.9f),
        new(0.55f, 0.12f, 0.75f, 0f),
    };
    static readonly Color VoidHaloOrange = new(1f, 0.55f, 0.25f, 0.9f);
    static readonly Color VoidHaloPurple = new(0.55f, 0.2f, 0.9f, 0.45f);
    static readonly string[] VoidSkyboxTints =
    {
        "_Tint",
        "_SkyTint",
        "_GroundColor",
        "_Color",
        "_SkyColor",
        "_HorizonColor",
        "_TopColor",
        "_BottomColor",
    };

    static bool _voidHungry;                    // eating cars right now
    static Vector3 _voidCentre;                 // where the hole is drawn this frame
    static readonly Dictionary<Vehicle, float> _voidAir = new();        // car -> seconds with no wheel on the ground
    static readonly Dictionary<Vehicle, float> _voidSafeUntil = new();  // spat-out car -> left alone until
    static readonly List<VoidMeal> _voidMeals = new();                  // cars on their way up
    static Material _voidBlack, _voidDiscBack, _voidDiscFront, _voidLens, _voidHaloInner, _voidHaloOuter;

    // A car on its way into the hole, and what to put back when it comes out.
    class VoidMeal
    {
        public Vehicle Vehicle;
        public Transform Car;
        public Rigidbody Body;
        public Vector3 From;
        public Vector3 Toward;
        public Vector3 Scale;
        public Vector3 SpinAxis;
        public float Started;
        public RigidbodyInterpolation Interpolation;
    }

    // One ring mesh whose vertex colours are repainted each frame (the streaks travel round).
    class VoidRing
    {
        public Mesh Mesh;
        public float[] Angles;
        public int[] Bands;
        public Color[] Colors;
    }

    // The hole as drawn: its holder (scaled so the black ball has radius 1), its rings and the inner halo.
    class VoidVisual
    {
        public Transform Hole;
        public Transform Halo;
        public readonly List<VoidRing> Rings = new();
    }

    static IEnumerator BlackHole()
    {
        if (Racers().Count == 0)
        {
            yield break;
        }
        Announce("BLACK HOLE", "VOID");
        var sky = VoidSky.Capture();
        bool night = VoidIsNight(sky);
        var direction = VoidDirection(sky, night);
        System.Action undo = sky.Restore;
        if (!night)
        {
            OnClear(undo);
            Guard("blackhole sky", sky.Begin);
        }
        var visual = VoidBuild();
        float hungryUntil = VoidSeconds - VoidFadeSeconds;
        for (float time = 0f; time < hungryUntil; time += Time.deltaTime)
        {
            float dusk = Mathf.SmoothStep(0f, 1f, time / VoidFadeSeconds);
            _voidHungry = dusk >= 1f;
            VoidFrame(visual, sky, direction, dusk, time);
            yield return null;
        }
        _voidHungry = false;
        float waited = 0f;
        for (; _voidMeals.Count > 0 && waited < VoidEatSeconds + 0.5f; waited += Time.deltaTime)
        {
            VoidFrame(visual, sky, direction, 1f, hungryUntil + waited);   // the last cars finish going in
            yield return null;
        }
        for (float time = 0f; time < VoidFadeSeconds; time += Time.deltaTime)
        {
            float dusk = Mathf.SmoothStep(1f, 0f, time / VoidFadeSeconds);
            VoidFrame(visual, sky, direction, dusk, hungryUntil + waited + time);
            yield return null;
        }
        Guard("blackhole sky", sky.Restore);
        ForgetOnClear(undo);
        if (visual.Hole != null)
        {
            Object.Destroy(visual.Hole.gameObject);
        }
    }

    // One frame of the event: the sky `dusk` of the way to night, the hole placed, sized and its disc turned.
    static void VoidFrame(VoidVisual visual, VoidSky sky, Vector3 direction, float dusk, float time)
    {
        Guard("blackhole sky", () => sky.Apply(dusk));
        if (visual.Hole == null)
        {
            return;
        }
        VoidPlace(visual.Hole, direction, dusk);
        foreach (var ring in visual.Rings)
        {
            VoidPaint(ring, time * VoidDiscSpin);
        }
        visual.Halo.localScale = Vector3.one * (5.2f + 0.25f * Mathf.Sin(time * 3f));
    }

    // ---- where the hole goes ----

    // Night = no sun to speak of: missing, off, dim, at/below the horizon, or a Farland sky whose sun disc is hidden.
    static bool VoidIsNight(VoidSky sky)
    {
        var sun = sky.Sun;
        if (sun == null || !sun.isActiveAndEnabled || sun.intensity < 0.25f || sun.transform.forward.y > -0.05f)
        {
            return true;
        }
        return sky.Farland != null && sky.Farland.SunEnabled && sky.Farland.SunTint.a < 0.15f;
    }

    // Low in the sky ahead of where the camera looks when the event starts (race cameras rarely look up or back), or
    // the sun's spot by day when that is in view anyway.
    static Vector3 VoidDirection(VoidSky sky, bool night)
    {
        var camera = Camera.main;
        var ahead = VoidElevate(camera != null ? camera.transform.forward : Vector3.forward, VoidViewElevation);
        if (night || sky.Sun == null)
        {
            return ahead;
        }
        var toSun = -sky.Sun.transform.forward;
        var sunSpot = toSun.y >= Mathf.Sin(VoidDayElevation * Mathf.Deg2Rad) ? toSun : VoidElevate(toSun, VoidDayElevation);
        return Vector3.Angle(sunSpot, ahead) <= VoidSunInView ? sunSpot : ahead;
    }

    // `heading` flattened to the horizon, then tipped up `degrees`.
    static Vector3 VoidElevate(Vector3 heading, float degrees)
    {
        heading.y = 0f;
        heading = heading.sqrMagnitude < 1e-6f ? Vector3.forward : heading.normalized;
        float angle = degrees * Mathf.Deg2Rad;
        return heading * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
    }

    // Keeps the hole at a fixed spot in the sky (a fixed direction from the camera, far off), facing the camera.
    static void VoidPlace(Transform hole, Vector3 direction, float grown)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        float distance = Mathf.Min(VoidSkyDistance, camera.farClipPlane * 0.8f);
        hole.position = camera.transform.position + direction * distance;
        hole.rotation = Quaternion.LookRotation(direction);   // +z points away from the camera
        float radius = distance * Mathf.Tan(VoidAngularRadius * Mathf.Deg2Rad);
        hole.localScale = Vector3.one * radius * Mathf.Max(0.001f, grown);
        _voidCentre = hole.position;
    }

    // ---- the hole itself ----

    // Built at unit size (the black ball has radius 1). All of it is unlit, unfogged transparent stuff drawn in a fixed
    // order: halos, then the back half of the disc and the lensing ring, then the ball, then the disc's front half.
    static VoidVisual VoidBuild()
    {
        VoidMaterials();
        var visual = new VoidVisual
        {
            Hole = Holder("blackhole"),
        };
        var outer = Prim(PrimitiveType.Quad, visual.Hole, Vector3.zero, Vector3.one * 9f, Color.white, glow: true);
        outer.GetComponent<Renderer>().sharedMaterial = _voidHaloOuter;
        var inner = Prim(PrimitiveType.Quad, visual.Hole, Vector3.zero, Vector3.one * 5.2f, Color.white, glow: true);
        inner.GetComponent<Renderer>().sharedMaterial = _voidHaloInner;
        visual.Halo = inner.transform;
        var lens = Holder("blackhole-lens", visual.Hole);
        lens.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the ring faces the camera
        visual.Rings.Add(VoidRingShape(lens, 1f, 1.5f, 0f, 2f * Mathf.PI, _voidLens));
        var disc = Holder("blackhole-disc", visual.Hole);
        disc.localRotation = Quaternion.Euler(-14f, 0f, 10f);   // tipped so its far half shows above the ball
        visual.Rings.Add(VoidRingShape(disc, 1.3f, 3.4f, 0f, Mathf.PI, _voidDiscBack));
        visual.Rings.Add(VoidRingShape(disc, 1.3f, 3.4f, Mathf.PI, 2f * Mathf.PI, _voidDiscFront));
        var ball = Prim(PrimitiveType.Sphere, visual.Hole, Vector3.zero, Vector3.one * 2f, Color.black, glow: true);
        ball.GetComponent<Renderer>().sharedMaterial = _voidBlack;
        return visual;
    }

    static void VoidMaterials()
    {
        if (_voidBlack != null)
        {
            return;
        }
        var glowTexture = VoidGlowTexture();
        _voidHaloOuter = VoidMaterial(VoidHaloPurple, 2999, glowTexture);
        _voidHaloInner = VoidMaterial(VoidHaloOrange, 3000, glowTexture);
        _voidDiscBack = VoidMaterial(Color.white, 3001, null);
        _voidLens = VoidMaterial(Color.white, 3001, null);
        _voidBlack = VoidMaterial(Color.black, 3002, null);
        _voidDiscFront = VoidMaterial(Color.white, 3003, null);
    }

    // The kit's unlit shader (vertex colours x tint, no fog, no lighting) in its own copy with a fixed draw order.
    static Material VoidMaterial(Color color, int queue, Texture texture)
    {
        var material = new Material(Mat(Color.white, glow: true))
        {
            color = color,
            renderQueue = queue,
        };
        if (texture != null)
        {
            material.mainTexture = texture;
        }
        return material;
    }

    // A soft glowing ring with a faint fill, transparent at the edge (the halo round the ball).
    static Texture2D VoidGlowTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var centre = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fromCentre = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) / (size * 0.5f);
                float ring = Mathf.Exp(-Mathf.Pow((fromCentre - 0.42f) / 0.16f, 2f));
                float alpha = (ring + 0.35f * Mathf.Clamp01(1f - fromCentre)) * Mathf.Clamp01((1f - fromCentre) * 4f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
        }
        texture.Apply();
        return texture;
    }

    // A flat ring (in its parent's XZ plane) from angle `from` to `to` (radians), three colour bands across.
    static VoidRing VoidRingShape(Transform parent, float inner, float outer, float from, float to, Material material)
    {
        const int bands = 3;
        float[] radii = { inner, inner + (outer - inner) * 0.35f, outer };
        int steps = Mathf.Max(2, Mathf.RoundToInt(VoidDiscSegments * (to - from) / (2f * Mathf.PI)));
        int count = (steps + 1) * bands;
        var points = new Vector3[count];
        var ring = new VoidRing
        {
            Angles = new float[count],
            Bands = new int[count],
            Colors = new Color[count],
        };
        for (int step = 0; step <= steps; step++)
        {
            float angle = Mathf.Lerp(from, to, step / (float)steps);
            for (int band = 0; band < bands; band++)
            {
                int index = step * bands + band;
                points[index] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radii[band];
                ring.Angles[index] = angle;
                ring.Bands[index] = band;
            }
        }
        ring.Mesh = new Mesh
        {
            vertices = points,
            triangles = VoidRingTriangles(steps, bands),
        };
        ring.Mesh.MarkDynamic();
        ring.Mesh.RecalculateBounds();
        VoidPaint(ring, 0f);
        var shape = new GameObject("blackhole-ring");
        shape.transform.SetParent(parent, false);
        shape.AddComponent<MeshFilter>().sharedMesh = ring.Mesh;
        var renderer = shape.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ring;
    }

    // Two triangles per cell between neighbouring steps and bands (the unlit shader draws both sides).
    static int[] VoidRingTriangles(int steps, int bands)
    {
        var triangles = new List<int>();
        for (int step = 0; step < steps; step++)
        {
            for (int band = 0; band < bands - 1; band++)
            {
                int corner = step * bands + band;
                triangles.AddRange(new[] { corner, corner + bands, corner + 1 });
                triangles.AddRange(new[] { corner + 1, corner + bands, corner + bands + 1 });
            }
        }
        return triangles.ToArray();
    }

    // Band colour x travelling streaks x a brighter side (the half that comes toward us glows more).
    static void VoidPaint(VoidRing ring, float phase)
    {
        for (int index = 0; index < ring.Colors.Length; index++)
        {
            var tint = VoidBandColors[ring.Bands[index]];
            float angle = ring.Angles[index];
            float streak = 0.72f + 0.28f * Mathf.Sin(angle * 5f - phase + ring.Bands[index] * 1.3f);
            float light = streak * (1f + 0.4f * Mathf.Cos(angle));
            ring.Colors[index] = new Color(
                Mathf.Clamp01(tint.r * light),
                Mathf.Clamp01(tint.g * light),
                Mathf.Clamp01(tint.b * light),
                tint.a);
        }
        ring.Mesh.colors = ring.Colors;
    }

    // ---- eating cars ----

    static partial void TickBlackHole()
    {
        VoidDigest();
        if (!_voidHungry)
        {
            _voidAir.Clear();
            return;
        }
        foreach (var vehicle in Racers())
        {
            VoidWatch(vehicle);
        }
    }

    // Counts how long a car has had no wheel on the ground; long enough and it is eaten.
    static void VoidWatch(Vehicle vehicle)
    {
        bool safe = _voidSafeUntil.TryGetValue(vehicle, out var until) && Time.time < until;
        bool eaten = _voidMeals.Any(meal => meal.Vehicle == vehicle);
        if (safe || eaten || !Game.Boomable(vehicle) || !VoidAirborne(vehicle))
        {
            _voidAir.Remove(vehicle);
            return;
        }
        float air = (_voidAir.TryGetValue(vehicle, out var sofar) ? sofar : 0f) + Time.deltaTime;
        _voidAir[vehicle] = air;
        if (air >= VoidAirSeconds)
        {
            _voidAir.Remove(vehicle);
            VoidEat(vehicle);
        }
    }

    // Every wheel off the ground while moving along (not a car dropping straight down or lying on its roof).
    static bool VoidAirborne(Vehicle vehicle)
    {
        var body = vehicle.Car().GetComponent<Rigidbody>();
        var wheels = vehicle.Car().GetComponent<Car>()?.Wheels();
        if (body == null || body.isKinematic || wheels == null || wheels.Length == 0)
        {
            return false;
        }
        var flat = new Vector3(body.velocity.x, 0f, body.velocity.z);
        return flat.magnitude >= VoidAirSpeed && wheels.All(wheel => wheel != null && !wheel.isGrounded);
    }

    // Takes the car off physics (kinematic, no interpolation) so it can be flown up by hand.
    static void VoidEat(Vehicle vehicle)
    {
        var car = vehicle.Car().transform;
        var body = vehicle.Car().GetComponent<Rigidbody>();
        var meal = new VoidMeal
        {
            Vehicle = vehicle,
            Car = car,
            Body = body,
            From = car.position,
            Toward = (_voidCentre - car.position).normalized,
            Scale = car.localScale,
            SpinAxis = Random.onUnitSphere,
            Started = Time.time,
            Interpolation = body.interpolation,
        };
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
        _voidMeals.Add(meal);
        VoidWatchCam(meal);
        Announce(Game.DisplayName(vehicle) + " fell into the black hole", "VOID", vehicle);
        Plugin.Emit("blackhole", new
        {
            login = Game.Login(vehicle),
            displayName = Game.DisplayName(vehicle),
        });
    }

    // Cuts to a camera standing behind the car on the ground, looking up past it at the hole (Cam.PlaneCam: any key
    // in the game or "Plane cam off" leaves it). It stays on the newest swallowed car and goes back when none are left.
    const string VoidCamLabel = "black hole";

    static void VoidWatchCam(VoidMeal meal)
    {
        var flat = new Vector3(meal.Toward.x, 0f, meal.Toward.z);
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : meal.Car.forward;
        var from = meal.From - flat * VoidCamBack + Vector3.up * VoidCamUp;
        if (Cam.PlaneCam(meal.Car, VoidCamLabel, from) && !_voidCamOnClear)
        {
            _voidCamOnClear = true;
            OnClear(EndVoidCam);
        }
    }

    static bool _voidCamOnClear;

    static void EndVoidCam()
    {
        _voidCamOnClear = false;
        ForgetOnClear(EndVoidCam);
        if (Cam.OnEventCam(VoidCamLabel))
        {
            Cam.EndPlaneCam();
        }
    }

    // Flies each eaten car up toward the hole (speeding up, corkscrewing, tumbling, shrinking); done ones come back.
    static void VoidDigest()
    {
        for (int index = _voidMeals.Count - 1; index >= 0; index--)
        {
            var meal = _voidMeals[index];
            float progress = (Time.time - meal.Started) / VoidEatSeconds;
            if (meal.Car == null || progress >= 1f)
            {
                _voidMeals.RemoveAt(index);
                VoidSpit(meal);
                if (_voidMeals.Count == 0)
                {
                    EndVoidCam();
                }
                else if (Cam.OnEventCam(VoidCamLabel))
                {
                    VoidWatchCam(_voidMeals[_voidMeals.Count - 1]);
                }
                continue;
            }
            VoidFly(meal, progress);
        }
    }

    static void VoidFly(VoidMeal meal, float progress)
    {
        var across = Vector3.Cross(meal.Toward, Vector3.up);
        across = across.sqrMagnitude < 1e-6f ? Vector3.right : across.normalized;
        var swirl = Quaternion.AngleAxis(progress * 720f, meal.Toward) * across * VoidSwirlRadius * (1f - progress);
        meal.Car.position = meal.From + meal.Toward * VoidPullDistance * progress * progress * progress + swirl;
        meal.Car.Rotate(meal.SpinAxis, VoidSpinSpeed * Time.deltaTime, Space.World);
        meal.Car.localScale = meal.Scale * Mathf.Lerp(1f, VoidSmallest, progress);
    }

    // Back to normal: full size, physics on, still, on the road where it was, then the game's own respawn.
    static void VoidSpit(VoidMeal meal)
    {
        if (meal.Car == null || meal.Body == null)
        {
            return;
        }
        meal.Car.localScale = meal.Scale;
        meal.Body.isKinematic = false;
        meal.Body.interpolation = meal.Interpolation;
        float progress = meal.Vehicle.Progress();
        var spot = RouteLength > 0f ? RoadAt(progress) + Vector3.up * 1.5f : meal.From;
        var facing = Quaternion.LookRotation(RoadDirectionAt(progress));
        meal.Car.SetPositionAndRotation(spot, facing);
        meal.Body.position = spot;
        meal.Body.rotation = facing;
        meal.Body.velocity = Vector3.zero;
        meal.Body.angularVelocity = Vector3.zero;
        _voidSafeUntil[meal.Vehicle] = Time.time + VoidImmunity;
        if (Game.Running)
        {
            Game.Respawn(meal.Vehicle);
        }
    }

    static partial void ResetBlackHole()
    {
        foreach (var meal in _voidMeals.ToList())
        {
            Guard("blackhole reset", () => VoidSpit(meal));
        }
        _voidMeals.Clear();
        _voidAir.Clear();
        _voidSafeUntil.Clear();
        _voidHungry = false;
    }

    // ---- the sky ----

    // Everything night changes, captured before the change: the sun light, ambient, reflections, fog, the skybox
    // (a Farland Skies controller's colours with its day-night cycle paused, or a dimmed copy of a plain skybox
    // material) and solid-colour camera backgrounds. Apply(0) gives back exactly what was captured.
    class VoidSky
    {
        public Light Sun;
        public SkyboxController Farland;
        float _sunIntensity;
        Color _sunColor;
        AmbientMode _ambientMode;
        Color _ambientLight, _ambientSky, _ambientEquator, _ambientGround;
        SphericalHarmonicsL2 _ambientProbe;
        float _reflection;
        bool _fog;
        FogMode _fogMode;
        Color _fogColor;
        float _fogDensity;
        SkyboxDayNightCycle _cycle;
        bool _cycleEnabled;
        Color _top, _middle, _bottom, _clouds, _stars, _sunTint;
        bool _sunFlare;
        Material _skybox, _skyboxCopy;
        readonly Dictionary<string, float> _skyFloats = new();
        readonly Dictionary<string, Color> _skyColors = new();
        readonly List<(Camera camera, Color color)> _backgrounds = new();
        bool _begun;

        public static VoidSky Capture()
        {
            var sky = new VoidSky
            {
                Farland = Object.FindObjectOfType<SkyboxController>(),
            };
            if (sky.Farland != null && sky.Farland.SkyboxMaterial == null)
            {
                sky.Farland = null;
            }
            var farlandSun = sky.Farland != null ? sky.Farland.SunLight : null;
            sky.Sun = farlandSun != null ? farlandSun : RenderSettings.sun;
            if (sky.Sun != null)
            {
                sky._sunIntensity = sky.Sun.intensity;
                sky._sunColor = sky.Sun.color;
            }
            sky.CaptureLighting();
            if (sky.Farland != null)
            {
                sky.CaptureFarland();
            }
            else
            {
                sky.CaptureSkybox();
            }
            foreach (var camera in Camera.allCameras.Where(camera => camera.clearFlags == CameraClearFlags.SolidColor))
            {
                sky._backgrounds.Add((camera, camera.backgroundColor));
            }
            return sky;
        }

        void CaptureLighting()
        {
            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            _ambientSky = RenderSettings.ambientSkyColor;
            _ambientEquator = RenderSettings.ambientEquatorColor;
            _ambientGround = RenderSettings.ambientGroundColor;
            _ambientProbe = RenderSettings.ambientProbe;
            _reflection = RenderSettings.reflectionIntensity;
            _fog = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
        }

        void CaptureFarland()
        {
            _cycle = Object.FindObjectOfType<SkyboxDayNightCycle>();
            _cycleEnabled = _cycle != null && _cycle.enabled;
            _top = Farland.TopColor;
            _middle = Farland.MiddleColor;
            _bottom = Farland.BottomColor;
            _clouds = Farland.CloudsTint;
            _stars = Farland.StarsTint;
            _sunTint = Farland.SunTint;
            _sunFlare = Farland.SunFlare;
        }

        void CaptureSkybox()
        {
            _skybox = RenderSettings.skybox;
            if (_skybox == null)
            {
                return;
            }
            foreach (var name in new[] { "_Exposure", "_SunSize" }.Where(_skybox.HasProperty))
            {
                _skyFloats[name] = _skybox.GetFloat(name);
            }
            foreach (var name in VoidSkyboxTints.Where(_skybox.HasProperty))
            {
                _skyColors[name] = _skybox.GetColor(name);
            }
        }

        // Pauses whatever would overwrite the sky every frame (the Farland cycle) and swaps in the skybox copy.
        public void Begin()
        {
            _begun = true;
            if (_cycle != null)
            {
                _cycle.enabled = false;
            }
            if (_skybox != null)
            {
                _skyboxCopy = new Material(_skybox);
                RenderSettings.skybox = _skyboxCopy;
            }
        }

        // `night` 0 = as captured, 1 = full black-hole night.
        public void Apply(float night)
        {
            if (!_begun)
            {
                return;
            }
            if (Sun != null)
            {
                Sun.intensity = Mathf.Lerp(_sunIntensity, VoidSunIntensity, night);
                Sun.color = Color.Lerp(_sunColor, VoidSunColor, night);
            }
            ApplyAmbient(night);
            ApplyFog(night);
            RenderSettings.reflectionIntensity = Mathf.Lerp(_reflection, _reflection * VoidReflectionShare, night);
            if (Farland != null)
            {
                ApplyFarland(night);
            }
            ApplySkybox(night);
            foreach (var (camera, color) in _backgrounds.Where(entry => entry.camera != null))
            {
                camera.backgroundColor = Color.Lerp(color, VoidSpace, night);
            }
        }

        void ApplyAmbient(float night)
        {
            if (_ambientMode == AmbientMode.Flat)
            {
                RenderSettings.ambientLight = Color.Lerp(_ambientLight, VoidAmbientColor, night);
                return;
            }
            if (_ambientMode == AmbientMode.Trilight)
            {
                RenderSettings.ambientSkyColor = Color.Lerp(_ambientSky, VoidAmbientColor * 1.3f, night);
                RenderSettings.ambientEquatorColor = Color.Lerp(_ambientEquator, VoidAmbientColor, night);
                RenderSettings.ambientGroundColor = Color.Lerp(_ambientGround, VoidAmbientColor * 0.5f, night);
                return;
            }
            RenderSettings.ambientProbe = _ambientProbe * Mathf.Lerp(1f, VoidAmbientShare, night);
        }

        // A map without fog gets a thin blue one; a map with fog keeps its own, tinted night blue.
        void ApplyFog(float night)
        {
            RenderSettings.fog = _fog || night > 0f;
            RenderSettings.fogColor = Color.Lerp(_fogColor, VoidFogColor, night);
            if (!_fog)
            {
                RenderSettings.fogMode = night > 0f ? FogMode.ExponentialSquared : _fogMode;
                RenderSettings.fogDensity = Mathf.Lerp(_fogDensity, VoidFogDensity, night);
            }
        }

        // Dark gradient, dim clouds, stars out, and the sun's disc and flare hidden (the hole stands in for it).
        void ApplyFarland(float night)
        {
            Farland.TopColor = Color.Lerp(_top, Opaque(VoidSkyTop, _top.a), night);
            Farland.MiddleColor = Color.Lerp(_middle, Opaque(VoidSkyMiddle, _middle.a), night);
            Farland.BottomColor = Color.Lerp(_bottom, Opaque(VoidSkyBottom, _bottom.a), night);
            Farland.CloudsTint = Color.Lerp(_clouds, Opaque(VoidCloudsTint, _clouds.a), night);
            if (Farland.StarsEnabled)
            {
                Farland.StarsTint = Color.Lerp(_stars, VoidStarsTint, night);
            }
            if (Farland.SunEnabled)
            {
                Farland.SunTint = Color.Lerp(_sunTint, Opaque(_sunTint, 0f), night);
                Farland.SunFlare = night > 0.05f ? false : _sunFlare;
            }
        }

        // The plain skybox copy: exposure and tints dimmed, the sun disc shrunk away.
        void ApplySkybox(float night)
        {
            if (_skyboxCopy == null)
            {
                return;
            }
            foreach (var entry in _skyFloats)
            {
                float dark = entry.Key == "_SunSize" ? 0f : entry.Value * VoidSkyboxExposure;
                _skyboxCopy.SetFloat(entry.Key, Mathf.Lerp(entry.Value, dark, night));
            }
            foreach (var entry in _skyColors)
            {
                var dark = Opaque(entry.Value * VoidSkyboxExposure, entry.Value.a);
                _skyboxCopy.SetColor(entry.Key, Color.Lerp(entry.Value, dark, night));
            }
        }

        static Color Opaque(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        // Exactly what was captured, the Farland cycle running again and the map's own skybox material back.
        public void Restore()
        {
            if (!_begun)
            {
                return;
            }
            Apply(0f);
            _begun = false;
            if (_cycle != null)
            {
                _cycle.enabled = _cycleEnabled;
            }
            if (_skyboxCopy != null)
            {
                RenderSettings.skybox = _skybox;
                Object.Destroy(_skyboxCopy);
                _skyboxCopy = null;
            }
        }
    }
}
