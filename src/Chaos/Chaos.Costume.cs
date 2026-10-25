// Chaos.Costume: dress-up for cars. A costume hides a car's own body (never its name label) and puts a small model
// built from primitives in its place for CostumeSeconds, then gives the car its body back. Purely looks: physics,
// speed and collisions stay the car's own. The witch and the magical girl hand them out; ResetCostumes undresses all.
// Also the pieces both casters share: cones, bones, two-sided facets, the flight along the pack and the magic zap.
using System.Collections;
using HarmonyLib;

namespace StreamRacerApi;

static partial class Chaos
{
    const float CostumeSeconds = 30f;
    const float CostumeCarLength = 4.5f;        // costume models are built for a car this long, then scaled to fit
    const float CostumeMinScale = 0.5f;
    const float CostumeMaxScale = 2f;

    const float CasterScale = 2.5f;             // the witch / magical girl are built life-size, then shown this big
    const float CasterMinHeight = 5f;           // above the car she is after: she rides along with the field
    const float CasterMaxHeight = 8f;
    const float CasterLead = 25f;               // route distance ahead of the middle of the pack (before a victim)
    const float CasterSideOffset = 4f;          // off to the side of her victim
    const float CasterAhead = 5f;               // and a little ahead of it, so she faces it
    const float CasterEntryOffset = 30f;        // sideways from the road where she comes in from...
    const float CasterEntryRise = 12f;          // ...and this much higher
    const float CasterChase = 2.5f;             // how quickly she catches up with her hover point (1/s)
    const float CasterEnterSeconds = 2.5f;
    const float CasterZapGap = 5f;              // seconds between two zaps: she glides to the next victim meanwhile
    const float CasterLingerSeconds = 1.5f;     // after the last zap, before she leaves
    const float CasterLeaveSeconds = 4f;
    const float CasterLeaveSpeed = 35f;         // m/s up and away
    const int CasterMinZaps = 3;                // cars zapped (inclusive)
    const int CasterMaxZaps = 6;
    const float BoltSpeed = 80f;                // m/s; faster than any car so it always catches up
    const float BoltSize = 0.7f;

    // kind -> builds its model under `model` (built for a CostumeCarLength car, origin on the ground under the car's
    // centre, +z forward) and returns the per-frame animation (seconds since dressed, forward speed in m/s).
    static readonly Dictionary<string, System.Func<Transform, System.Action<float, float>>> CostumeKinds = new()
    {
        ["skeleton"] = BuildSkeleton,
        ["bat"] = BuildBat,
        ["pumpkin"] = BuildPumpkin,
        ["cinnamon"] = BuildCinnamonRoll,
        ["onigiri"] = BuildOnigiri,
        ["star"] = BuildStar,
    };

    static readonly string[] WitchCostumes =
    {
        "skeleton",
        "bat",
        "pumpkin",
    };
    static readonly string[] MagicalCostumes =
    {
        "cinnamon",
        "onigiri",
        "star",
    };

    // The name label is a separate object the CarLabel on the car points at; it must stay visible.
    static readonly System.Reflection.FieldInfo CostumeLabelField =
        AccessTools.Field(typeof(CarLabel), GameNames.CarLabelObject);

    // One dressed car: which car object it was put on, the hidden renderers (with how they were) and the model.
    class Costume
    {
        public Vehicle Vehicle;
        public GameObject Car;
        public Rigidbody Body;
        public string Kind;
        public float Since;
        public float Until;
        public Transform Outfit;
        public System.Action<float, float> Animate;
        public bool Dancing;                     // an uma who finished: off the car, dancing by the line (Chaos.Uma)
        public readonly List<(Renderer renderer, bool enabled)> Hidden = new();
    }

    static readonly Dictionary<Vehicle, Costume> _costumes = new();

    // ---- dressing ----

    // Puts costume `kind` on the car for `seconds`, replacing whatever costume it already wears.
    public static void Dress(Vehicle vehicle, string kind, float seconds = CostumeSeconds) =>
        Guard("costume", () => DressNow(vehicle, kind, seconds));

    static void DressNow(Vehicle vehicle, string kind, float seconds)
    {
        var car = vehicle?.Car();
        if (car == null || !CostumeBuilder(kind, out var build))
        {
            return;
        }
        Undress(vehicle, false);
        var body = BodyRenderers(car);
        var model = FitOutfit(car.transform, BodyBounds(car.transform, body));
        System.Action<float, float> animate;
        try
        {
            animate = build(model);
        }
        catch
        {
            Object.Destroy(model.parent.gameObject);   // a half-built costume never leaves the car invisible
            throw;
        }
        var costume = new Costume
        {
            Vehicle = vehicle,
            Car = car,
            Body = car.GetComponent<Rigidbody>(),
            Kind = kind,
            Since = Time.time,
            Until = Time.time + seconds,
            Outfit = model.parent,
            Animate = animate,
        };
        foreach (var renderer in body)
        {
            costume.Hidden.Add((renderer, renderer.enabled));
            renderer.enabled = false;
        }
        _costumes[vehicle] = costume;
        if (kind.StartsWith("uma:"))
        {
            OnUmaLayer(costume.Outfit.gameObject);   // the whole outfit, so the per-frame check below stays cheap
        }
        animate(0f, 0f);
        EmitCostume(vehicle, kind, seconds);
    }

    // The builder for `kind`: one of CostumeKinds, or "uma:<export folder>" for a horse girl (Chaos.Uma).
    static bool CostumeBuilder(string kind, out System.Func<Transform, System.Action<float, float>> build)
    {
        if (kind.StartsWith("uma:"))
        {
            string umaKey = kind.Substring(4);
            build = model => BuildUma(model, umaKey);
            return true;
        }
        return CostumeKinds.TryGetValue(kind, out build);
    }

    // Takes the costume off and gives the car its body back.
    static void Undress(Vehicle vehicle, bool emit)
    {
        if (!_costumes.TryGetValue(vehicle, out var costume))
        {
            return;
        }
        _costumes.Remove(vehicle);
        foreach (var (renderer, enabled) in costume.Hidden)
        {
            if (renderer != null)
            {
                renderer.enabled = enabled;
            }
        }
        if (costume.Outfit != null)
        {
            Object.Destroy(costume.Outfit.gameObject);
        }
        if (emit)
        {
            EmitCostume(vehicle, null, 0f);
        }
    }

    static void EmitCostume(Vehicle vehicle, string kind, float seconds) => Plugin.Emit("costume", new
    {
        login = vehicle.Profile()?.Login(),
        costume = kind,
        seconds = Mathf.Round(seconds),
    });

    static partial void TickCostumes()
    {
        if (_costumes.Count == 0)
        {
            return;
        }
        foreach (var costume in _costumes.Values.ToList())
        {
            Guard("costume", () => TickCostume(costume));
        }
    }

    static void TickCostume(Costume costume)
    {
        if (Time.time >= costume.Until)
        {
            Undress(costume.Vehicle, true);
            return;
        }
        if (costume.Dancing)
        {
            costume.Animate(Time.time - costume.Since, 0f);
            return;
        }
        var car = costume.Vehicle.Car();
        if (car != costume.Car)
        {
            Redress(costume, car);
            return;
        }
        if (car == null || costume.Outfit == null)
        {
            return;
        }
        foreach (var (renderer, _) in costume.Hidden)
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;   // the game switched it back on (respawn blink): keep the body hidden
            }
        }
        if (costume.Kind.StartsWith("uma:") && costume.Outfit.gameObject.layer != UmaLayer)
        {
            OnUmaLayer(costume.Outfit.gameObject);   // the game re-layers everything under a car now and then
        }
        float speed = costume.Body != null ? Vector3.Dot(costume.Body.velocity, car.transform.forward) : 0f;
        costume.Animate(Time.time - costume.Since, speed);
    }

    // The game swapped the car object (respawn): same costume on the new car for the time that is left.
    static void Redress(Costume costume, GameObject car)
    {
        if (car == null)
        {
            return;   // between cars: wait for the new one
        }
        DressNow(costume.Vehicle, costume.Kind, costume.Until - Time.time);
    }

    static partial void ResetCostumes()
    {
        foreach (var vehicle in _costumes.Keys.ToList())
        {
            Undress(vehicle, true);
        }
    }

    // ---- the car's body ----

    // The car's own meshes: no name label, no UI or text, nothing a chaos event hung on it ("sr-..." objects).
    static List<Renderer> BodyRenderers(GameObject car)
    {
        var label = car.GetComponent<CarLabel>();
        var labelObject = label != null ? CostumeLabelField?.GetValue(label) as GameObject : null;
        return car.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            .Where(renderer => !IsDecoration(renderer, labelObject))
            .ToList();
    }

    static bool IsDecoration(Renderer renderer, GameObject labelObject)
    {
        if (labelObject != null && renderer.transform.IsChildOf(labelObject.transform))
        {
            return true;
        }
        if (renderer.GetComponent<TMPro.TMP_Text>() != null || renderer.GetComponentInParent<Canvas>() != null)
        {
            return true;
        }
        for (var step = renderer.transform; step != null; step = step.parent)
        {
            if (step.name.StartsWith("sr-"))
            {
                return true;
            }
        }
        return false;
    }

    // The body's box in the car's own axes (world-sized, centred on the car's pivot); a typical car when unknown.
    static Bounds BodyBounds(Transform car, List<Renderer> body)
    {
        var bounds = new Bounds(Vector3.up * 0.7f, new Vector3(2f, 1.4f, CostumeCarLength));
        var inverse = Quaternion.Inverse(car.rotation);
        bool any = false;
        foreach (var renderer in body.Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy))
        {
            foreach (var corner in Corners(renderer))
            {
                var aligned = inverse * (corner - car.position);
                bounds = any ? Grow(bounds, aligned) : new Bounds(aligned, Vector3.zero);
                any = true;
            }
        }
        return bounds;
    }

    static Bounds Grow(Bounds bounds, Vector3 point)
    {
        bounds.Encapsulate(point);
        return bounds;
    }

    // A renderer's eight box corners in world space: the mesh's own box when there is one (tight even when turned).
    static IEnumerable<Vector3> Corners(Renderer renderer)
    {
        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        var box = mesh != null ? mesh.bounds : renderer.bounds;
        for (int i = 0; i < 8; i++)
        {
            var corner = box.center + Vector3.Scale(box.extents, new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f));
            yield return mesh != null ? renderer.transform.TransformPoint(corner) : corner;
        }
    }

    // "sr-costume" on the car (unscaled, on the ground under the body's centre) holding the scaled "model".
    static Transform FitOutfit(Transform car, Bounds bounds)
    {
        var outfit = new GameObject("sr-costume").transform;
        outfit.SetParent(car, false);
        var lossy = car.lossyScale;
        outfit.localScale = new Vector3(Inverse(lossy.x), Inverse(lossy.y), Inverse(lossy.z));
        outfit.position = car.position + car.rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        outfit.rotation = car.rotation;
        var model = new GameObject("model").transform;
        model.SetParent(outfit, false);
        float length = Mathf.Max(bounds.size.x, bounds.size.z);
        model.localScale = Vector3.one * Mathf.Clamp(length / CostumeCarLength, CostumeMinScale, CostumeMaxScale);
        return model;
    }

    static float Inverse(float value) => Mathf.Abs(value) > 1e-4f ? 1f / value : 1f;

    // ---- shapes the costumes and casters share ----

    // Like MeshShape, but every triangle gets its own corners and a back face: flat facets seen from both sides
    // (wings, ears, stars, cones) without shared-vertex normals cancelling each other out.
    static GameObject Facets(string name, Transform parent, Vector3[] points, int[] triangles, Color color,
        bool glow = false)
    {
        int count = triangles.Length;
        var corners = new Vector3[count * 2];
        var order = new int[count * 2];
        for (int i = 0; i < count; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                corners[i + k] = points[triangles[i + k]];
                corners[count + i + k] = points[triangles[i + 2 - k]];
            }
        }
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        return MeshShape(name, parent, corners, order, color, glow);
    }

    // A cone pointing up its local +y (`height` tall, `radius` wide at the base), placed in the parent's space.
    static GameObject ConeShape(Transform parent, Vector3 position, float radius, float height, Color color,
        Vector3 euler = default, bool glow = false, int sides = 12)
    {
        var points = new Vector3[sides + 2];
        var triangles = new List<int>();
        points[sides] = Vector3.up * height;
        points[sides + 1] = Vector3.zero;
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            int next = (i + 1) % sides;
            AddTriangle(triangles, sides, next, i);
            AddTriangle(triangles, sides + 1, i, next);
        }
        var cone = Facets("cone", parent, points, triangles.ToArray(), color, glow);
        cone.transform.localPosition = position;
        cone.transform.localRotation = Quaternion.Euler(euler);
        return cone;
    }

    // A capsule from `from` to `to` (parent space), `thickness` wide: limbs, bones, sticks, icing lines.
    static GameObject BoneShape(Transform parent, Vector3 from, Vector3 to, float thickness, Color color,
        bool glow = false)
    {
        var span = to - from;
        float half = Mathf.Max(span.magnitude * 0.5f, thickness * 0.5f);
        var bone = Prim(PrimitiveType.Capsule, parent, (from + to) * 0.5f, new Vector3(thickness, half, thickness),
            color, glow: glow);
        if (span.sqrMagnitude > 1e-6f)
        {
            bone.transform.localRotation = Quaternion.FromToRotation(Vector3.up, span);
        }
        return bone;
    }

    // ---- casters (the witch, the magical girl) ----

    // Where `lead` metres ahead of the middle of the pack the road is, and which way it runs there.
    static bool PackPoint(float lead, out Vector3 point, out Vector3 direction)
    {
        var racers = Racers().OrderBy(vehicle => vehicle.Progress()).ToList();
        point = Vector3.zero;
        direction = Vector3.forward;
        if (racers.Count == 0)
        {
            return false;
        }
        var middle = racers[racers.Count / 2];
        if (RouteLength > 0f)
        {
            point = RoadAt(middle.Progress() + lead);
            direction = RoadDirectionAt(middle.Progress() + lead);
            return true;
        }
        var forward = middle.Car().transform.forward;
        forward.y = 0f;
        direction = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        point = CarPosition(middle) + direction * lead;
        return true;
    }

    // A caster's path: comes in from the side, hovers over the pack (bobbing, turning where she goes), leaves.
    class Flight
    {
        readonly Transform _caster;
        readonly float _height;
        readonly float _side;
        Vector3 _anchor;
        Vector3 _heading = Vector3.forward;
        float _time;

        public Flight(Transform caster, float height, float side)
        {
            _caster = caster;
            _height = height;
            _side = side;
            if (PackPoint(CasterLead, out var point, out var direction))
            {
                _heading = direction;
                var across = Vector3.Cross(Vector3.up, direction) * side;
                _anchor = point + across * CasterEntryOffset + Vector3.up * (height + CasterEntryRise);
            }
            caster.position = _anchor;
            caster.rotation = Quaternion.LookRotation(-Vector3.Cross(Vector3.up, _heading) * side);
        }

        // Hovers beside and just ahead of `victim` (the pack when there is none), keeping pace with it.
        public void Follow(Vehicle victim = null)
        {
            if (victim != null && victim.Car() != null && !victim.HasFinished())
            {
                _heading = RoadDirectionAt(victim.Progress());
                var target = CarPosition(victim) + _heading * CasterAhead
                    + Vector3.Cross(Vector3.up, _heading) * (_side * CasterSideOffset) + Vector3.up * _height;
                _anchor = Vector3.Lerp(_anchor, target, 1f - Mathf.Exp(-CasterChase * Time.deltaTime));
            }
            else if (PackPoint(CasterLead, out var point, out var direction))
            {
                _heading = direction;
                var target = point + Vector3.Cross(Vector3.up, direction) * (_side * CasterSideOffset)
                    + Vector3.up * _height;
                _anchor = Vector3.Lerp(_anchor, target, 1f - Mathf.Exp(-CasterChase * Time.deltaTime));
            }
            Place();
        }

        public void Leave()
        {
            var away = Vector3.up * 0.6f + _heading + Vector3.Cross(Vector3.up, _heading) * _side;
            _anchor += away.normalized * (CasterLeaveSpeed * Time.deltaTime);
            Place();
        }

        void Place()
        {
            _time += Time.deltaTime;
            var previous = _caster.position;
            _caster.position = _anchor + Vector3.up * (Mathf.Sin(_time * 2f) * 0.6f);
            var move = _caster.position - previous;
            move.y = 0f;
            var face = move.sqrMagnitude > 1e-4f ? move : _heading;
            _caster.rotation = Quaternion.Slerp(_caster.rotation, Quaternion.LookRotation(face),
                1f - Mathf.Exp(-4f * Time.deltaTime));
        }
    }

    // The whole visit: fly in, then ride along with the field gliding up to 3-6 random racers in turn, zapping one every
    // CasterZapGap seconds (each hit gets one of `kinds`), then fly away.
    // `tip` is where the bolts leave from (hand or wand); `jagged` = crackling lightning instead of a straight beam.
    static IEnumerator CasterVisit(Transform caster, Transform tip, Color bolt, bool jagged, string[] kinds)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
        var flight = new Flight(caster, Random.Range(CasterMinHeight, CasterMaxHeight), side);
        for (float time = 0f; time < CasterEnterSeconds && caster != null; time += Time.deltaTime)
        {
            flight.Follow();
            yield return null;
        }
        var victims = Racers()
            .OrderBy(vehicle => Random.value)
            .Take(Random.Range(CasterMinZaps, CasterMaxZaps + 1))
            .ToList();
        foreach (var victim in victims)
        {
            for (float time = 0f; time < CasterZapGap && caster != null; time += Time.deltaTime)
            {
                flight.Follow(victim);
                yield return null;
            }
            if (victim.Car() != null && !victim.HasFinished() && tip != null)
            {
                ZapBolt(tip, victim, bolt, jagged, kinds);
            }
        }
        for (float time = 0f; time < CasterLingerSeconds && caster != null; time += Time.deltaTime)
        {
            flight.Follow();
            yield return null;
        }
        for (float time = 0f; time < CasterLeaveSeconds && caster != null; time += Time.deltaTime)
        {
            flight.Leave();
            yield return null;
        }
        if (caster != null)
        {
            Object.Destroy(caster.gameObject);
        }
    }

    // A glowing orb that homes onto the car, joined to the caster by a beam; the car it reaches gets a costume.
    static void ZapBolt(Transform tip, Vehicle target, Color color, bool jagged, string[] kinds)
    {
        var orb = Holder("sr-bolt");
        orb.position = tip.position;
        Prim(PrimitiveType.Sphere, orb, Vector3.zero, Vector3.one * BoltSize, color, glow: true);
        Prim(PrimitiveType.Sphere, orb, Vector3.zero, Vector3.one * (BoltSize * 0.5f), Color.white, glow: true);
        var light = orb.gameObject.AddComponent<Light>();
        light.color = color;
        light.range = 10f;
        light.intensity = 4f;
        var fade = new Color(color.r, color.g, color.b, 0f);
        Stream(orb, color, fade, 90f, 0.35f, 0.6f, 0.45f);
        var beam = BeamLine(color, jagged);
        Spawn(Projectile(orb, target, BoltSpeed, vehicle => CostumeHit(vehicle, color, kinds)));
        Spawn(DrawBeam(beam, tip, orb, jagged));
    }

    static void CostumeHit(Vehicle vehicle, Color color, string[] kinds)
    {
        Dress(vehicle, kinds[Random.Range(0, kinds.Length)]);
        var burst = Holder("sr-costume-burst");
        burst.position = CarPosition(vehicle) + Vector3.up;
        Puff(burst, color, new Color(1f, 1f, 1f, 0f), 50, 0.6f, 6f, 0.9f);
        Puff(burst, Color.white, new Color(color.r, color.g, color.b, 0f), 25, 0.4f, 3f, 0.7f);
        Object.Destroy(burst.gameObject, 2f);
    }

    static LineRenderer BeamLine(Color color, bool jagged)
    {
        var beam = Holder("sr-beam").gameObject.AddComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.sharedMaterial = Mat(color, true);
        beam.widthMultiplier = jagged ? 0.25f : 0.45f;
        beam.positionCount = jagged ? 10 : 2;
        beam.numCapVertices = 4;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return beam;
    }

    // Keeps the beam between the caster and the orb (a fresh crackle every frame when jagged) until the orb is gone.
    static IEnumerator DrawBeam(LineRenderer beam, Transform tip, Transform orb, bool jagged)
    {
        for (float time = 0f; beam != null && orb != null && tip != null; time += Time.deltaTime)
        {
            int count = beam.positionCount;
            for (int i = 0; i < count; i++)
            {
                float along = i / (float)(count - 1);
                var point = Vector3.Lerp(tip.position, orb.position, along);
                bool end = i == 0 || i == count - 1;
                beam.SetPosition(i, jagged && !end ? point + Random.insideUnitSphere * 0.9f : point);
            }
            beam.widthMultiplier = jagged ? 0.25f : 0.45f + Mathf.Sin(time * 30f) * 0.12f;
            yield return null;
        }
        if (beam != null)
        {
            Object.Destroy(beam.gameObject);
        }
    }

    // A caster's root: a holder under the chaos root with her model built at life size inside a scaled "body".
    static Transform CasterBody(string name, out Transform body)
    {
        var caster = Holder("sr-" + name);
        body = Holder("body", caster);
        body.localScale = Vector3.one * CasterScale;
        return caster;
    }
}
