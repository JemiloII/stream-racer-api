// Chaos.UmaMotion: the horse girls move with Umamusume's own race motions and jiggle like they do there. The clips
// (scripts/uma_export.py motions -> StreamRacerApi.uma/motions/type0N_<run|overrun>.json, one per running form) are
// per-bone keyframes played back faster or slower with the car's speed. After them, a small spring simulation swings
// every "Sp_" bone (ears, tail, hair, ribbons, skirt): each one trails its parent with drag and settles back to how
// the clip left it, so they bounce with every stride and flap in the wind.
using System.IO;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

static partial class Chaos
{
    const float ClipSpeed = 16f;                    // m/s the run clip is made for (her real gallop)
    const float ClipMinRate = 0.45f, ClipMaxRate = 2f;
    const float SpringGravity = 3f;                 // m/s² down on the swinging tips
    const float SpringFlutter = 0.25f;              // how much the headwind gusts (fraction), so things flap

    // How one kind of swinging bone moves: pull back toward the pose each step (0..1), velocity lost each step (0..1),
    // headwind per m/s she runs (m/s²), the most it may swing from the pose (degrees), and how much of its sideways
    // (left/right) motion survives each step (0..1: low = it doesn't flop from side to side).
    class SpringProfile
    {
        public float Stiffness;
        public float Drag;
        public float Wind;
        public float MaxSwing;
        public float Sideways;
    }

    // By the bone's name (Sp_He_Ear*, Sp_Hi_Tail*, Sp_He_* hair, Sp_Ch_Bust*, everything else = clothes).
    static SpringProfile ProfileFor(string bone)
    {
        if (bone.StartsWith("Sp_He_Ear"))
        {
            return EarSpring;
        }
        if (bone.StartsWith("Sp_Hi_Tail"))
        {
            return TailSpring;
        }
        if (bone.StartsWith("Sp_He_"))
        {
            return HairSpring;
        }
        return bone.StartsWith("Sp_Ch_Bust") ? BustSpring : ClothSpring;
    }

    static readonly SpringProfile EarSpring = new()
    {
        Stiffness = 0.25f,
        Drag = 0.25f,
        Wind = 0.08f,
        MaxSwing = 15f,
        Sideways = 0.5f,
    };
    static readonly SpringProfile TailSpring = new()
    {
        Stiffness = 0.09f,
        Drag = 0.2f,
        Wind = 0.25f,
        MaxSwing = 35f,
        Sideways = 0.35f,
    };
    static readonly SpringProfile HairSpring = new()
    {
        Stiffness = 0.07f,
        Drag = 0.14f,
        Wind = 0.3f,
        MaxSwing = 40f,
        Sideways = 0.7f,
    };
    static readonly SpringProfile BustSpring = new()
    {
        Stiffness = 0.3f,
        Drag = 0.3f,
        Wind = 0f,
        MaxSwing = 8f,
        Sideways = 1f,
    };
    static readonly SpringProfile ClothSpring = new()
    {
        Stiffness = 0.05f,
        Drag = 0.12f,
        Wind = 0.35f,
        MaxSwing = 60f,
        Sideways = 0.8f,
    };

    // Her body as spheres the swinging bones can't go through: bone, offset toward its child (0..1), radius (m).
    static readonly (string bone, float along, float radius)[] SpringColliders =
    {
        ("Head", 0.5f, 0.11f),
        ("Neck", 0f, 0.06f),
        ("Chest", 0.3f, 0.12f),
        ("Spine", 0.5f, 0.11f),
        ("Waist", 0.5f, 0.11f),
        ("Hip", 0f, 0.14f),
        // the legs, thigh to ankle, so skirts and tails ride on them instead of passing through
        ("Thigh_L", 0.15f, 0.095f),
        ("Thigh_L", 0.5f, 0.09f),
        ("Thigh_L", 0.85f, 0.075f),
        ("Knee_L", 0.3f, 0.065f),
        ("Knee_L", 0.7f, 0.055f),
        ("Thigh_R", 0.15f, 0.095f),
        ("Thigh_R", 0.5f, 0.09f),
        ("Thigh_R", 0.85f, 0.075f),
        ("Knee_R", 0.3f, 0.065f),
        ("Knee_R", 0.7f, 0.055f),
    };

    // One decoded clip: for each track the bone, what it drives and its values per frame (or one constant).
    class UmaClip
    {
        public int Frames;
        public float Length;
        public List<(string bone, string kind, float[][] values)> Tracks = new();
    }

    static readonly Dictionary<string, UmaClip> _clips = new();

    static UmaClip ClipFor(int runningType, string name)
    {
        string key = runningType + name;
        if (_clips.TryGetValue(key, out var cached))
        {
            return cached;
        }
        string path = Path.Combine(UmaFolder, "motions", "type0" + runningType + "_" + name + ".json");
        UmaClip clip = null;
        if (File.Exists(path))
        {
            clip = ParseClip(JObject.Parse(File.ReadAllText(path)));
        }
        _clips[key] = clip;
        return clip;
    }

    static UmaClip ParseClip(JObject info)
    {
        var clip = new UmaClip
        {
            Frames = (int)info["frames"],
            Length = Mathf.Max(0.05f, (float)info["length"]),
        };
        foreach (JObject track in (JArray)info["tracks"])
        {
            var components = ((JArray)track["values"]).Select(component => component is JArray frames
                ? frames.Select(value => (float)value).ToArray()
                : new[] { (float)component }).ToArray();
            clip.Tracks.Add(((string)track["bone"], (string)track["kind"], components));
        }
        return clip;
    }

    // The clip on her skeleton, looped, sped up / slowed down with the car; springs on top. Null when there is no clip.
    static System.Action<float, float> PlayClip(Transform uma, UmaClip clip)
    {
        if (clip == null)
        {
            return null;
        }
        var bones = uma.GetComponentsInChildren<Transform>(true)
            .GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        var bound = clip.Tracks
            .Where(track => bones.ContainsKey(track.bone) && track.bone != "Position")   // no root motion: the car moves her
            .Select(track => (bone: bones[track.bone], track.kind, track.values))
            .ToList();
        var springs = Springs(uma);
        float time = 0f;
        return (seconds, speed) =>
        {
            if (uma == null)
            {
                return;
            }
            time += Time.deltaTime * Mathf.Clamp(Mathf.Abs(speed) / ClipSpeed, ClipMinRate, ClipMaxRate);
            float at = time / clip.Length % 1f * clip.Frames;
            int frame = (int)at % clip.Frames;
            int next = (frame + 1) % clip.Frames;
            float blend = at - Mathf.Floor(at);
            foreach (var (bone, kind, values) in bound)
            {
                ApplyTrack(bone, kind, values, frame, next, blend);
            }
            StepSprings(springs, speed);
        };
    }

    static void ApplyTrack(Transform bone, string kind, float[][] values, int frame, int next, float blend)
    {
        float Value(int component)
        {
            var curve = values[component];
            return curve.Length == 1 ? curve[0] : Mathf.Lerp(curve[frame], curve[next], blend);
        }
        switch (kind)
        {
            case "r":
                var rotation = new Quaternion(Value(0), Value(1), Value(2), Value(3));
                bone.localRotation = rotation.normalized;
                break;
            case "p":
                bone.localPosition = new Vector3(Value(0), Value(1), Value(2));
                break;
            case "s":
                bone.localScale = new Vector3(Value(0), Value(1), Value(2));
                break;
        }
    }

    // ---- springs ----

    // One swinging bone: its pose before the swing, where its child's tip sits in her own space (and sat last step),
    // and how far the tip is from the bone.
    class Spring
    {
        public Transform Root;
        public Transform Bone;
        public Transform Child;
        public Quaternion Rest;
        public Vector3 Tip;
        public Vector3 PreviousTip;
        public float Length;
        public bool Started;
        public SpringProfile Profile;
        public List<(Transform bone, Transform child, float along, float radius)> Colliders;
    }

    // Every Sp_ bone with a child, parents before children (so a chain swings as a chain), sharing her colliders.
    static List<Spring> Springs(Transform uma)
    {
        var all = uma.GetComponentsInChildren<Transform>(true);
        var byName = all.GroupBy(bone => bone.name).ToDictionary(group => group.Key, group => group.First());
        var colliders = SpringColliders
            .Where(collider => byName.ContainsKey(collider.bone))
            .Select(collider =>
            {
                var bone = byName[collider.bone];
                return (bone, bone.childCount > 0 ? bone.GetChild(0) : bone, collider.along, collider.radius);
            })
            .ToList();
        var springs = new List<Spring>();
        foreach (var bone in all)
        {
            if (!bone.name.StartsWith("Sp_") || bone.childCount == 0)
            {
                continue;
            }
            springs.Add(new Spring
            {
                Root = uma,
                Bone = bone,
                Child = bone.GetChild(0),
                Rest = bone.localRotation,
                Profile = ProfileFor(bone.name),
                Colliders = colliders,
            });
        }
        return springs;
    }

    // In her own space (so running at 25 m/s doesn't drag every tip a metre behind): each step the bone goes back to
    // its posed rotation; the tip keeps its momentum minus drag, sags, gets a gusting headwind from her speed, is
    // pulled gently toward the posed tip, pushed out of her body, kept within SpringMaxSwing of the pose and at its
    // length; the bone then turns to point at it. Clips never move these bones, so nothing piles up.
    const float SpringTunedFps = 60f;

    static void StepSprings(List<Spring> springs, float speed)
    {
        float step = Mathf.Min(Time.deltaTime, 0.05f);
        float gust = 1f + SpringFlutter * Mathf.Sin(Time.time * 9.3f) * Mathf.Sin(Time.time * 3.7f);
        var wind = Vector3.back * (Mathf.Abs(speed) * gust);   // her own -z: straight behind her, x each profile's Wind
        foreach (var spring in springs)
        {
            if (spring.Bone == null || spring.Child == null || spring.Root == null)
            {
                continue;
            }
            StepSpring(spring, step, wind);
        }
    }

    static void StepSpring(Spring spring, float step, Vector3 wind)
    {
        spring.Bone.localRotation = spring.Rest;
        var root = spring.Root;
        var origin = root.InverseTransformPoint(spring.Bone.position);
        var posed = root.InverseTransformPoint(spring.Child.position);
        if (!spring.Started)
        {
            spring.Started = true;
            spring.Tip = spring.PreviousTip = posed;
            spring.Length = Vector3.Distance(origin, posed);
        }
        if (spring.Length < 1e-5f)
        {
            return;
        }
        var profile = spring.Profile;
        float frames = step * SpringTunedFps;   // Drag / Stiffness were tuned per 60 fps frame: the same per second at any fps
        var velocity = (spring.Tip - spring.PreviousTip) * Mathf.Pow(1f - profile.Drag, frames);
        velocity.x *= profile.Sideways;   // her own x = left/right: a tail that doesn't whip from side to side
        spring.PreviousTip = spring.Tip;
        var down = root.InverseTransformDirection(Vector3.down);
        spring.Tip += velocity + (down * SpringGravity + wind * profile.Wind) * (step * step);
        spring.Tip = Vector3.Lerp(spring.Tip, posed, 1f - Mathf.Pow(1f - profile.Stiffness, frames));
        spring.Tip = OutOfBody(spring, spring.Tip);
        var swing = Vector3.RotateTowards(posed - origin, spring.Tip - origin, profile.MaxSwing * Mathf.Deg2Rad, 0f);
        spring.Tip = origin + swing.normalized * spring.Length;
        var from = spring.Child.position - spring.Bone.position;
        var to = root.TransformPoint(spring.Tip) - spring.Bone.position;
        spring.Bone.rotation = Quaternion.FromToRotation(from, to) * spring.Bone.rotation;
    }

    // A tip inside one of her body spheres is pushed out to its surface.
    static Vector3 OutOfBody(Spring spring, Vector3 tip)
    {
        var root = spring.Root;
        foreach (var (bone, child, along, radius) in spring.Colliders)
        {
            if (bone == spring.Bone || bone == null)
            {
                continue;
            }
            var centre = root.InverseTransformPoint(Vector3.Lerp(bone.position, child.position, along));
            var away = tip - centre;
            if (away.sqrMagnitude < radius * radius && away.sqrMagnitude > 1e-10f)
            {
                tip = centre + away.normalized * radius;
            }
        }
        return tip;
    }
}
