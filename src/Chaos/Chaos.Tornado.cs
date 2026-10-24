// Chaos.Tornado: a grey twister drops out of a storm cloud onto the road ahead of the pack and wanders back along
// (and across) the route for a while. Cars that come near get dragged in, swirled round its axis and lifted; once one
// is high enough it is flung out and booms ("by tornado"). The wind is applied in FixedUpdate by a small component on
// the funnel (TornadoWind) as velocity changes only, so no car is ever frozen or made kinematic.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const float TornadoSeconds = 20f;           // on the ground this long (plus touch-down and lift-off)
    const float TornadoTouchSeconds = 2f;       // the funnel grows down from the cloud / pulls back up this long
    const float TornadoAhead = 70f;             // touches down this far along the route ahead of the leader
    const float TornadoDrift = 4f;              // m/s it wanders back along the route, toward the pack
    const float TornadoSway = 9f;               // m it swings left/right across the road
    const float TornadoSwayPeriod = 7f;         // s for one full swing
    const float TornadoFollow = 2f;             // how quickly the base catches up with its wandering target (1/s)
    const float TornadoReach = 18f;             // a car whose centre is this close (flat) feels the wind
    const float TornadoCore = 3.5f;             // the radius cars are dragged in to and orbit at
    const float TornadoInward = 9f;             // m/s pull toward that orbit, at the edge of the reach
    const float TornadoSwirl = 16f;             // m/s around the axis near the core
    const float TornadoLift = 7f;               // m/s upward once a car is well inside
    const float TornadoGrip = 2.5f;             // how fast a car's velocity is steered to the wind's (1/s)
    const float TornadoSpin = 5f;               // rad/s the car spins about itself near the core
    const float TornadoFlingHeight = 17f;       // this high above the base it is thrown out and booms
    const float TornadoFlingSpeed = 26f;        // m/s outward when thrown
    const float TornadoFlingUp = 8f;            // m/s upward when thrown
    const float TornadoRecatch = 6f;            // a thrown car is left alone this long
    const float TornadoHeight = 48f;            // funnel height, ground to cloud
    const float TornadoBaseRadius = 2.2f;
    const float TornadoTopRadius = 22f;
    const int TornadoLayers = 30;               // stacked see-through discs that make the funnel
    const int TornadoDebrisChunks = 12;         // boards and rocks circling inside it

    // see-through (unlit, alpha) so the stack reads as swirling dust and wind, not a solid pillar
    static readonly Color[] TornadoGreys =
    {
        new(0.52f, 0.53f, 0.55f, 0.32f),
        new(0.62f, 0.62f, 0.64f, 0.26f),
        new(0.42f, 0.43f, 0.45f, 0.36f),
    };
    static readonly Color TornadoCloudColor = new(0.22f, 0.23f, 0.26f);
    static readonly Color TornadoDust = new(0.55f, 0.48f, 0.38f, 0.7f);
    static readonly Color TornadoDebrisColor = new(0.32f, 0.24f, 0.16f, 1f);
    static readonly Color TornadoMist = new(0.6f, 0.61f, 0.63f, 0.45f);

    static Transform _tornadoBase;              // the funnel's foot on the ground (null when there is no tornado)
    static float _tornadoStrength;              // 0 while it touches down / lifts off, 1 on the ground
    static readonly Dictionary<Vehicle, float> _tornadoFreeAt = new();   // thrown car -> when it may be caught again

    // Applies the wind every physics step; lives on the funnel, so it stops when the funnel (or the root) goes.
    class TornadoWind : MonoBehaviour
    {
        void FixedUpdate() => Guard("tornado", () => TornadoPull(Time.fixedDeltaTime));
    }

    // One circling chunk inside the funnel: its height and radius as fractions, and its angle speed.
    class TornadoChunk
    {
        public Transform Shape;
        public float Height;
        public float Radius;
        public float Speed;
        public float Angle;
    }

    static IEnumerator Tornado()
    {
        var leader = Racers().OrderByDescending(vehicle => vehicle.Progress()).FirstOrDefault();
        if (leader == null || RouteLength <= 0f)
        {
            yield break;
        }
        Announce("TORNADO", "TWISTER");
        float startDistance = leader.Progress() + TornadoAhead;
        var holder = Holder("tornado");
        holder.position = TornadoSpot(startDistance, 0f);
        var funnel = TornadoFunnel(holder);
        var chunks = TornadoDebris(holder);
        _tornadoBase = holder;
        _tornadoStrength = 0f;
        holder.gameObject.AddComponent<TornadoWind>();
        float total = TornadoSeconds + TornadoTouchSeconds * 2f;
        ParticleSystem[] streams = null;
        for (float time = 0f; time < total && holder != null; time += Time.deltaTime)
        {
            float grown = TornadoGrowth(time, total);
            funnel.localScale = new Vector3(1f, Mathf.Max(0.01f, grown), 1f);
            _tornadoStrength = grown >= 0.95f ? 1f : 0f;
            if (streams == null && grown >= 0.95f)
            {
                streams = TornadoGroundEffects(holder);
            }
            float along = Mathf.Max(0f, time - TornadoTouchSeconds);
            var target = TornadoSpot(startDistance - TornadoDrift * along, along);
            holder.position = Vector3.Lerp(holder.position, target, Mathf.Clamp01(TornadoFollow * Time.deltaTime));
            TornadoSpinFunnel(funnel, time);
            TornadoSpinDebris(chunks, grown);
            yield return null;
        }
        _tornadoStrength = 0f;
        _tornadoBase = null;
        if (holder != null)
        {
            TornadoStopStreams(streams);
            Object.Destroy(holder.gameObject, 2.5f);   // the dust settles on its own
        }
    }

    // 0 -> 1 while touching down, 1 on the ground, 1 -> 0 while lifting off.
    static float TornadoGrowth(float time, float total)
    {
        float down = Mathf.Clamp01(time / TornadoTouchSeconds);
        float up = Mathf.Clamp01((total - time) / TornadoTouchSeconds);
        return Mathf.SmoothStep(0f, 1f, Mathf.Min(down, up));
    }

    // The ground under the road at `distance`, swung sideways by the sway.
    static Vector3 TornadoSpot(float distance, float time)
    {
        var side = Vector3.Cross(Vector3.up, RoadDirectionAt(distance));
        float swing = Mathf.Sin(time * 2f * Mathf.PI / TornadoSwayPeriod) * TornadoSway;
        return Ground(RoadAt(distance) + side * swing);
    }

    // ---- the funnel ----

    // A storm cloud up top and stacked grey discs hanging down from it, narrow at the ground, wide at the cloud. The
    // funnel's pivot is at the cloud, so scaling it in y makes it reach down / pull back up.
    static Transform TornadoFunnel(Transform holder)
    {
        var cloud = Holder("tornado-cloud", holder);
        cloud.localPosition = Vector3.up * TornadoHeight;
        Prim(PrimitiveType.Sphere, cloud, Vector3.zero, new Vector3(70f, 8f, 70f), TornadoCloudColor);
        Prim(PrimitiveType.Sphere, cloud, new Vector3(14f, 3f, -9f), new Vector3(46f, 7f, 40f), TornadoCloudColor);
        var funnel = Holder("tornado-funnel", holder);
        funnel.localPosition = Vector3.up * TornadoHeight;
        float thickness = TornadoHeight / TornadoLayers * 1.2f;
        for (int index = 0; index < TornadoLayers; index++)
        {
            float up = index / (float)(TornadoLayers - 1);
            float radius = Mathf.Lerp(TornadoBaseRadius, TornadoTopRadius, Mathf.Pow(up, 2.2f));
            var position = Vector3.down * TornadoHeight * (1f - up);
            var scale = new Vector3(radius * 2f, thickness * 0.5f, radius * 1.7f);   // a bit oval, so the spin shows
            Prim(PrimitiveType.Cylinder, funnel, position, scale, TornadoGreys[index % TornadoGreys.Length], glow: true);
        }
        var mist = Stream(funnel, TornadoMist, new Color(0.6f, 0.61f, 0.63f, 0f), 60f, 7f, 1f, 3f);
        mist.transform.localPosition = Vector3.down * TornadoHeight * 0.5f;
        var mistShape = mist.shape;
        mistShape.radius = 5f;
        return funnel;
    }

    // Each disc turns (faster near the ground) and the whole funnel snakes: the higher a disc, the further it swings.
    static void TornadoSpinFunnel(Transform funnel, float time)
    {
        for (int index = 0; index < funnel.childCount; index++)
        {
            var layer = funnel.GetChild(index);
            if (layer.GetComponent<ParticleSystem>() != null)
            {
                continue;
            }
            float up = Mathf.Clamp01((layer.localPosition.y + TornadoHeight) / TornadoHeight);
            float speed = 90f + 260f * (1f - up);
            layer.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
            float swing = 4f * up;
            float phase = time * 1.7f + up * 3f;
            var position = layer.localPosition;
            layer.localPosition = new Vector3(Mathf.Sin(phase) * swing, position.y, Mathf.Cos(phase * 0.8f) * swing);
        }
    }

    // Boards and rocks that circle the axis inside the funnel.
    static List<TornadoChunk> TornadoDebris(Transform holder)
    {
        var chunks = new List<TornadoChunk>();
        for (int index = 0; index < TornadoDebrisChunks; index++)
        {
            bool board = index % 2 == 0;
            var scale = board ? new Vector3(2.2f, 0.15f, 0.5f) : Vector3.one * Random.Range(0.5f, 1.1f);
            var color = board ? TornadoDebrisColor : TornadoGreys[2];
            var shape = Prim(board ? PrimitiveType.Cube : PrimitiveType.Sphere, holder, Vector3.zero, scale, color);
            chunks.Add(new TornadoChunk
            {
                Shape = shape.transform,
                Height = Random.Range(0.05f, 0.6f),
                Radius = Random.Range(1.3f, 2.4f),
                Speed = Random.Range(160f, 280f),
                Angle = Random.Range(0f, 360f),
            });
        }
        return chunks;
    }

    static void TornadoSpinDebris(List<TornadoChunk> chunks, float grown)
    {
        foreach (var chunk in chunks)
        {
            chunk.Angle += chunk.Speed * Time.deltaTime;
            float height = chunk.Height * TornadoHeight;
            float funnelRadius = Mathf.Lerp(TornadoBaseRadius, TornadoTopRadius, Mathf.Pow(chunk.Height, 1.8f));
            var around = Quaternion.Euler(0f, chunk.Angle, 0f) * Vector3.forward * funnelRadius * chunk.Radius;
            chunk.Shape.localPosition = around + Vector3.up * height;
            chunk.Shape.Rotate(new Vector3(200f, 140f, 90f) * Time.deltaTime, Space.Self);
            chunk.Shape.gameObject.SetActive(grown >= 0.95f);
        }
    }

    // A ring of dust blowing out flat round the foot and dirt thrown up the funnel.
    static ParticleSystem[] TornadoGroundEffects(Transform holder)
    {
        var dust = Stream(holder, TornadoDust, new Color(0.55f, 0.48f, 0.38f, 0f), 50f, 5f, 7f, 1.8f);
        var dustShape = dust.shape;
        dustShape.shapeType = ParticleSystemShapeType.Circle;
        dustShape.radius = 4f;
        dustShape.rotation = new Vector3(90f, 0f, 0f);   // the circle lies flat, so it blows out sideways
        var dirt = Stream(holder, TornadoDebrisColor, new Color(0.32f, 0.24f, 0.16f, 0f), 30f, 0.5f, 14f, 2.5f);
        var dirtShape = dirt.shape;
        dirtShape.shapeType = ParticleSystemShapeType.Cone;
        dirtShape.angle = 35f;
        dirtShape.rotation = new Vector3(-90f, 0f, 0f);  // the cone points up
        var dirtMain = dirt.main;
        dirtMain.gravityModifier = 0.6f;
        return new[] { dust, dirt };
    }

    static void TornadoStopStreams(ParticleSystem[] streams)
    {
        if (streams == null)
        {
            return;
        }
        foreach (var system in streams.Where(system => system != null))
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ---- the wind ----

    // Every racing car inside the reach is steered toward the wind's velocity: in toward the core orbit, round the
    // axis and, once well inside, up. Gravity is cancelled for the lifted share so the lift actually lifts.
    static void TornadoPull(float deltaTime)
    {
        if (_tornadoBase == null || _tornadoStrength <= 0f)
        {
            return;
        }
        var axis = _tornadoBase.position;
        foreach (var vehicle in Racers())
        {
            if (_tornadoFreeAt.TryGetValue(vehicle, out var freeAt) && Time.time < freeAt)
            {
                continue;
            }
            var body = vehicle.Car().GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                TornadoPullCar(vehicle, body, axis, deltaTime);
            }
        }
    }

    static void TornadoPullCar(Vehicle vehicle, Rigidbody body, Vector3 axis, float deltaTime)
    {
        var offset = body.position - axis;
        var flat = new Vector3(offset.x, 0f, offset.z);
        float distance = flat.magnitude;
        if (distance > TornadoReach || offset.y < -3f)
        {
            return;
        }
        var outward = distance > 0.01f ? flat / distance : Vector3.right;
        if (offset.y > TornadoFlingHeight)
        {
            TornadoFling(vehicle, body, outward);
            return;
        }
        float closeness = 1f - distance / TornadoReach;
        var around = Vector3.Cross(Vector3.up, outward);
        float inwardShare = Mathf.Clamp((distance - TornadoCore) / (TornadoReach - TornadoCore), -0.5f, 1f);
        float liftShare = Mathf.Clamp01((TornadoReach * 0.7f - distance) / (TornadoReach * 0.7f - TornadoCore));
        var wind = around * TornadoSwirl * (0.4f + 0.6f * closeness) - outward * TornadoInward * inwardShare
            + Vector3.up * TornadoLift * liftShare;
        float steer = Mathf.Clamp01(TornadoGrip * (0.3f + 0.7f * closeness) * deltaTime);
        var velocity = body.velocity;
        velocity += (wind - velocity) * steer;
        velocity -= Physics.gravity * deltaTime * liftShare;
        body.velocity = velocity;
        var spin = new Vector3(Mathf.Sin(Time.time * 3f), 1f, Mathf.Cos(Time.time * 2f)) * TornadoSpin * liftShare;
        body.angularVelocity = Vector3.Lerp(body.angularVelocity, spin, steer);
    }

    // High enough: thrown out of the funnel, tumbling, and the game's boom by "tornado".
    static void TornadoFling(Vehicle vehicle, Rigidbody body, Vector3 outward)
    {
        _tornadoFreeAt[vehicle] = Time.time + TornadoRecatch;
        body.velocity = outward * TornadoFlingSpeed + Vector3.up * TornadoFlingUp;
        body.angularVelocity = Random.onUnitSphere * TornadoSpin;
        var burst = Holder("tornado-fling");
        burst.position = body.position;
        Puff(burst, TornadoDebrisColor, new Color(0.32f, 0.24f, 0.16f, 0f), 25, 0.6f, 8f, 1.2f);
        Object.Destroy(burst.gameObject, 2f);
        Game.StrikeNow(vehicle, "tornado");
        Plugin.Emit("tornado", new
        {
            login = Game.Login(vehicle),
            displayName = Game.DisplayName(vehicle),
        });
    }

    // ---- per frame / reset ----

    // Forgets thrown cars once their grace time is over, and everything once the tornado is gone.
    static partial void TickTornado()
    {
        if (_tornadoFreeAt.Count == 0)
        {
            return;
        }
        var free = _tornadoFreeAt.Where(entry => Time.time >= entry.Value).Select(entry => entry.Key).ToList();
        foreach (var vehicle in free)
        {
            _tornadoFreeAt.Remove(vehicle);
        }
    }

    static partial void ResetTornado()
    {
        _tornadoBase = null;
        _tornadoStrength = 0f;
        _tornadoFreeAt.Clear();
    }
}
