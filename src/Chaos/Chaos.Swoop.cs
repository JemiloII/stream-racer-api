// Chaos.Swoop: the attack-run flight the flyers share (bombers, dragons, the witch, the magical girl). A flyer climbs
// to a point high up behind its target car, rolls into a dive straight at where that car will be when it gets there,
// attacks when close (the caller drops a bomb, breathes fire or zaps), pulls up and climbs out for the next one. It
// never flies lower than its floor over the ground. This only moves the velocity and says where to be next frame;
// the caller places, turns and animates its own model.

namespace StreamRacerApi;

static partial class Chaos
{
    // How one kind of flyer flies its attack runs.
    class SwoopStyle
    {
        public float High;                     // m above the target where it rolls into the dive
        public float DiveSpeed;                // m/s
        public float ClimbSpeed;               // m/s
        public float Turn = 1.6f;              // how fast its heading swings toward where it wants to go (1/s)
        public float DiveFrom = 150f;          // flat distance to the target at which a climbing flyer rolls in
        public float ClimbBack = 110f;         // climbs out to this far behind its target
        public float AttackHeight;             // attacks once this little above the target...
        public float AttackRange;              // ...or this close to it
        public float Floor = 8f;               // never lower than this above the ground under it
    }

    // A flyer's flight state. Bombers, dragons and casters keep one each.
    class Swooper
    {
        public Transform Body;
        public Vector3 Velocity;
        public Vehicle Target;
        public bool Diving;
    }

    // One frame of an attack run. Returns where the flyer should be next frame; `attack` is true on the frame it gets
    // close enough to hit `Target` (it then pulls up on its own and needs a new Target).
    static Vector3 SwoopStep(Swooper flyer, SwoopStyle style, out bool attack)
    {
        attack = false;
        var position = flyer.Body.position;
        if (flyer.Target == null || flyer.Target.Car() == null || flyer.Target.HasFinished())
        {
            flyer.Diving = false;
            return position + flyer.Velocity * Time.deltaTime;
        }
        var car = CarPosition(flyer.Target);
        var desired = flyer.Diving ? DiveAt(flyer, car, style) : ClimbBehind(flyer, car, style);
        float speed = flyer.Diving ? style.DiveSpeed : style.ClimbSpeed;
        var heading = flyer.Velocity.sqrMagnitude > 1e-4f ? flyer.Velocity.normalized : desired;
        var steered = Vector3.Slerp(heading, desired, 1f - Mathf.Exp(-style.Turn * Time.deltaTime));
        flyer.Velocity = steered * Mathf.MoveTowards(flyer.Velocity.magnitude, speed, 20f * Time.deltaTime);
        var next = position + flyer.Velocity * Time.deltaTime;
        float floor = Ground(next).y + style.Floor;
        if (next.y < floor)
        {
            next.y = floor;
            flyer.Velocity.y = Mathf.Max(flyer.Velocity.y, 0f);
        }
        if (flyer.Diving && CloseEnough(next, car, style))
        {
            attack = true;
            flyer.Diving = false;
        }
        return next;
    }

    // Climbing: toward a point high up behind the car; rolls into the dive once high and close enough.
    static Vector3 ClimbBehind(Swooper flyer, Vector3 car, SwoopStyle style)
    {
        var behind = -RoadDirectionAt(flyer.Target.Progress());
        var holdPoint = car + behind * style.ClimbBack + Vector3.up * style.High;
        var offset = car - flyer.Body.position;
        float flat = new Vector2(offset.x, offset.z).magnitude;
        if (flyer.Body.position.y - car.y > style.High * 0.75f && flat < style.DiveFrom)
        {
            flyer.Diving = true;
        }
        return (holdPoint - flyer.Body.position).normalized;
    }

    // Diving: straight at where the car will be by the time the flyer gets there.
    static Vector3 DiveAt(Swooper flyer, Vector3 car, SwoopStyle style)
    {
        var aim = LeadPoint(flyer.Target, car, flyer.Body.position, style.DiveSpeed) + Vector3.up * 2f;
        return (aim - flyer.Body.position).normalized;
    }

    static bool CloseEnough(Vector3 position, Vector3 car, SwoopStyle style)
    {
        var offset = position - car;
        return offset.y < style.AttackHeight || offset.magnitude < style.AttackRange;
    }

    // The car's position `distance / speed` seconds from now, at its current velocity.
    static Vector3 LeadPoint(Vehicle target, Vector3 car, Vector3 from, float speed)
    {
        var body = target.Car().GetComponent<Rigidbody>();
        var velocity = body != null ? body.velocity : Vector3.zero;
        float seconds = Vector3.Distance(from, car) / Mathf.Max(1f, speed);
        return car + new Vector3(velocity.x, 0f, velocity.z) * seconds;
    }

    // Where a flyer appears: high in the sky behind the slowest car, off to one side.
    static Vector3 SwoopEntry(float high, float side)
    {
        var racers = Racers();
        float distance = racers.Count > 0 ? racers.Min(vehicle => vehicle.Progress()) - 140f : 0f;
        var across = Vector3.Cross(Vector3.up, RoadDirectionAt(distance)) * side * 40f;
        return RoadAt(distance) + across + Vector3.up * high;
    }
}
