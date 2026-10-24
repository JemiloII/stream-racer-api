// Chaos.Fire: burning cars. Ignite puts flames and a flickering light on a car and starts a random fuse; when it runs
// out the car gets the game's boom and the fire goes out. A burning car sets any racer it touches alight too (each
// with its own fuse). Used by the dragon's fireballs; any other event can call Ignite. Cleared with the chaos root.
namespace StreamRacerApi;

static partial class Chaos
{
    const float FuseMaxSeconds = 10f;       // a fuse is Random.Range(0, this)
    const float SpreadRadius = 3.5f;        // car centres closer than this pass the fire on
    const float BoomRetrySeconds = 3f;      // a car mid-boom when its fuse ends gets this long to become hittable
    const float FlameRate = 45f;            // flame particles a second
    const float FlameRise = 0.8f;           // how hard flames float up (negative gravity)
    const float FireLightRange = 8f;
    const float FireLightIntensity = 2.5f;

    static readonly Color FlameColor = new(1f, 0.5f, 0.08f, 1f);
    static readonly Color FlameEnd = new(0.6f, 0.08f, 0.02f, 0f);
    static readonly Color SmokeColor = new(0.25f, 0.22f, 0.2f, 0.7f);
    static readonly Color SmokeEnd = new(0.15f, 0.15f, 0.15f, 0f);

    // One burning car: the car object it was lit on (a respawn swaps it), its flames and when the fuse ends.
    class Burning
    {
        public GameObject Car;
        public Transform Flames;
        public Light Glow;
        public float BoomAt;
        public float Seconds;
        public string By;
        public float Seed;
    }

    static readonly Dictionary<Vehicle, Burning> _burning = new();

    public static bool IsBurning(Vehicle vehicle) => vehicle != null && _burning.ContainsKey(vehicle);

    // Sets `vehicle` on fire with a 0..10 s fuse, credited to `by` when it booms. False when it can't burn (no race,
    // no car, finished, already burning).
    public static bool Ignite(Vehicle vehicle, string by)
    {
        if (!Game.Running || vehicle?.Car() == null || vehicle.HasFinished() || IsBurning(vehicle))
        {
            return false;
        }
        var car = vehicle.Car();
        float seconds = Random.Range(0f, FuseMaxSeconds);
        var flames = Holder("sr-fire", car.transform);
        flames.localPosition = Vector3.up * 0.7f;
        Rise(Stream(flames, FlameColor, FlameEnd, FlameRate, 0.9f, 1.2f, 0.7f));
        Rise(Stream(flames, SmokeColor, SmokeEnd, FlameRate * 0.3f, 1.4f, 1.5f, 1.6f));
        var glow = flames.gameObject.AddComponent<Light>();
        glow.color = FlameColor;
        glow.range = FireLightRange;
        glow.intensity = FireLightIntensity;
        _burning[vehicle] = new Burning
        {
            Car = car,
            Flames = flames,
            Glow = glow,
            BoomAt = Time.unscaledTime + seconds,
            Seconds = seconds,
            By = by,
            Seed = Random.value * 100f,
        };
        EmitFire(vehicle, "lit", seconds);
        return true;
    }

    // Flames and smoke float upward instead of spraying evenly.
    static void Rise(ParticleSystem system)
    {
        var main = system.main;
        main.gravityModifier = -FlameRise;
    }

    static void EmitFire(Vehicle vehicle, string phase, float seconds) => Plugin.Emit("fire", new
    {
        login = Game.Login(vehicle),
        phase,
        seconds = Mathf.Round(seconds * 10f) / 10f,
    });

    // ---- per frame ----

    static partial void TickFire()
    {
        if (_burning.Count == 0)
        {
            return;
        }
        foreach (var vehicle in _burning.Keys.ToList())
        {
            TickBurning(vehicle, _burning[vehicle]);
        }
        if (_burning.Count == 0)
        {
            return;
        }
        var racers = Racers();
        foreach (var vehicle in _burning.Keys.ToList())
        {
            SpreadFrom(vehicle, racers);
        }
    }

    // Gone or finished: just goes out. Fuse over: boom (retrying briefly if the car is mid-boom), then out.
    static void TickBurning(Vehicle vehicle, Burning fire)
    {
        var car = vehicle.Car();
        if (car == null || car != fire.Car || vehicle.HasFinished() || fire.Flames == null)
        {
            Extinguish(vehicle);
            return;
        }
        float now = Time.unscaledTime;
        if (now < fire.BoomAt)
        {
            fire.Glow.intensity = FireLightIntensity * (0.6f + 0.8f * Mathf.PerlinNoise(now * 9f, fire.Seed));
            return;
        }
        bool struck = Game.StrikeNow(vehicle, fire.By);
        if (!struck && now < fire.BoomAt + BoomRetrySeconds)
        {
            return;
        }
        Extinguish(vehicle);
        if (struck)
        {
            EmitFire(vehicle, "boom", fire.Seconds);
        }
    }

    // A burning car touching a racer that isn't burning lights it (with its own fuse).
    static void SpreadFrom(Vehicle source, List<Vehicle> racers)
    {
        if (!_burning.TryGetValue(source, out var fire) || source.Car() == null)
        {
            return;
        }
        var from = CarPosition(source);
        foreach (var other in racers)
        {
            if (other == source || IsBurning(other) || other.Car() == null)
            {
                continue;
            }
            if (Vector3.Distance(from, CarPosition(other)) < SpreadRadius)
            {
                Ignite(other, fire.By);
            }
        }
    }

    // Puts the fire out: the flames stop and their last particles drift off from where the car was.
    static void Extinguish(Vehicle vehicle)
    {
        if (!_burning.TryGetValue(vehicle, out var fire))
        {
            return;
        }
        _burning.Remove(vehicle);
        if (fire.Flames == null)
        {
            return;
        }
        foreach (var system in fire.Flames.GetComponentsInChildren<ParticleSystem>())
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        Object.Destroy(fire.Glow);
        fire.Flames.SetParent(Root.transform, true);
        Object.Destroy(fire.Flames.gameObject, 2f);
    }

    // Clear: every fire out now (the flames sit on the cars, not on the chaos root).
    static partial void ResetFire()
    {
        foreach (var fire in _burning.Values)
        {
            if (fire.Flames != null)
            {
                Object.Destroy(fire.Flames.gameObject);
            }
        }
        _burning.Clear();
    }
}
