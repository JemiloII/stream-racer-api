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
        public float MaxTurn;                  // degrees/s its heading may turn at most (0 = no cap): planes fly arcs
        public float DiveTurn;                 // degrees/s cap while diving, when it lines up on the car (0 = MaxTurn)
        public float MaxClimb;                 // degrees its path may point up at most (0 = no cap)
        public float CoastSeconds;             // after an attack it flies on level this long before climbing out
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
        public float CoastUntil;
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
        if (Time.time < flyer.CoastUntil && flyer.Velocity.sqrMagnitude > 1e-4f)
        {
            desired = LevelOff(flyer.Velocity);   // just dropped: carry on, nose coming level, before turning back
        }
        if (style.MaxClimb > 0f)
        {
            desired = CapClimb(desired, style.MaxClimb);
        }
        float speed = flyer.Diving ? style.DiveSpeed : style.ClimbSpeed;
        var heading = flyer.Velocity.sqrMagnitude > 1e-4f ? flyer.Velocity.normalized : desired;
        var steered = Vector3.Slerp(heading, desired, 1f - Mathf.Exp(-style.Turn * Time.deltaTime));
        float maxTurn = flyer.Diving && style.DiveTurn > 0f ? style.DiveTurn : style.MaxTurn;
        if (maxTurn > 0f)
        {
            steered = Vector3.RotateTowards(heading, steered, maxTurn * Mathf.Deg2Rad * Time.deltaTime, 0f);
        }
        flyer.Velocity = steered * Mathf.MoveTowards(flyer.Velocity.magnitude, speed, 20f * Time.deltaTime);
        var next = position + flyer.Velocity * Time.deltaTime;
        float floor = Ground(next).y + style.Floor;
        if (next.y < floor)
        {
            next.y = floor;
            flyer.Velocity.y = Mathf.Max(flyer.Velocity.y, 0f);
        }
        if (flyer.Diving && (CloseEnough(next, car, style) || PassedOver(flyer.Velocity, next, car)))
        {
            attack = true;
            flyer.Diving = false;
            flyer.CoastUntil = Time.time + style.CoastSeconds;
        }
        return next;
    }

    // Straight on along the current heading, flattened to the horizon.
    static Vector3 LevelOff(Vector3 velocity)
    {
        var flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        return flat.sqrMagnitude > 1e-4f ? flat.normalized : velocity.normalized;
    }

    // `direction` tipped down so it points at most `degrees` above the horizon (dives are not limited).
    static Vector3 CapClimb(Vector3 direction, float degrees)
    {
        var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.y <= 0f || flat.sqrMagnitude < 1e-6f || Vector3.Angle(flat, direction) <= degrees)
        {
            return direction;
        }
        float climb = degrees * Mathf.Deg2Rad;
        return (flat.normalized * Mathf.Cos(climb) + Vector3.up * Mathf.Sin(climb)).normalized;
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

    // Already past the car (it is behind the flyer, seen from above): attack now rather than loop round.
    static bool PassedOver(Vector3 velocity, Vector3 position, Vector3 car)
    {
        var ahead = Vector3.ProjectOnPlane(velocity, Vector3.up);
        var toCar = Vector3.ProjectOnPlane(car - position, Vector3.up);
        return ahead.sqrMagnitude > 1e-4f && toCar.magnitude < 60f && Vector3.Dot(ahead, toCar) < 0f;
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
