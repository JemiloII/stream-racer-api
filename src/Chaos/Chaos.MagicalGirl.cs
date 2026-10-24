// Chaos.MagicalGirl: a magical girl transformation sequence. A pink-and-white magical girl with twin-tails and a
// star-tipped wand flies in low on a trail of pink and white sparkles and rides along with the field, gliding up
// beside 3-6 random racers in turn and beaming one every ~5 s; every car she hits turns into something cute for a
// while (a cinnamon roll, a rice ball or a big golden star). Then she flies off. Costumes live in Chaos.Costume.cs.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    static readonly Color MagicalPink = new(1f, 0.45f, 0.72f);
    static readonly Color MagicalWhite = new(0.97f, 0.96f, 1f);
    static readonly Color MagicalSkin = new(1f, 0.86f, 0.76f);
    static readonly Color MagicalHair = new(1f, 0.66f, 0.86f);
    static readonly Color MagicalEyes = new(0.25f, 0.5f, 1f);
    static readonly Color MagicalRibbon = new(0.95f, 0.15f, 0.35f);
    static readonly Color MagicalBeam = new(1f, 0.4f, 0.8f);
    static readonly Color StarGold = new(1f, 0.82f, 0.15f);

    static readonly Color DoughTan = new(0.86f, 0.62f, 0.36f);
    static readonly Color CinnamonBrown = new(0.45f, 0.24f, 0.1f);
    static readonly Color IcingWhite = new(1f, 0.98f, 0.93f);
    static readonly Color RiceWhite = new(0.97f, 0.97f, 0.95f);
    static readonly Color NoriGreen = new(0.07f, 0.11f, 0.08f);
    static readonly Color OnigiriFace = new(0.1f, 0.08f, 0.08f);
    static readonly Color OnigiriBlush = new(1f, 0.6f, 0.7f);

    const float StarOuter = 1.7f;               // costume star, in costume units (a CostumeCarLength car)
    const float StarInner = 0.72f;
    const float StarDepth = 0.45f;              // how far its faces bulge out front and back
    const float StarSpin = 60f;                 // degrees a second
    const float CinnamonRadius = 1.7f;
    const int CinnamonTurns = 3;
    const float OnigiriDepth = 0.6f;            // half its thickness
    const float OnigiriBulge = 0.12f;           // its faces puff out this much in the middle
    const float OnigiriCornerRadius = 0.45f;
    const int OnigiriCornerSteps = 6;

    static Material _starGlow;

    static IEnumerator MagicalGirl()
    {
        if (Racers().Count == 0)
        {
            yield break;
        }
        Announce("MAGICAL GIRL", "MAGIC");
        var girl = BuildMagicalGirl(out var wandTip);
        var visit = CasterVisit(girl, wandTip, MagicalBeam, false, MagicalCostumes);
        while (visit.MoveNext())
        {
            yield return visit.Current;
        }
    }

    // ---- the magical girl ----

    // Life-size and facing +z, leaning into her flight; `wandTip` is the star on her wand.
    static Transform BuildMagicalGirl(out Transform wandTip)
    {
        var girl = CasterBody("magical-girl", out var body);
        var pose = Holder("pose", body);
        pose.localRotation = Quaternion.Euler(20f, 0f, 0f);
        BuildMagicalOutfit(pose);
        BuildMagicalHead(pose);
        // left arm out to the side, right arm forward holding the wand up
        BoneShape(pose, new Vector3(-0.18f, 1.3f, 0f), new Vector3(-0.4f, 0.98f, 0.05f), 0.09f, MagicalSkin);
        Prim(PrimitiveType.Sphere, pose, new Vector3(-0.4f, 0.98f, 0.05f), Vector3.one * 0.11f, MagicalWhite);
        BoneShape(pose, new Vector3(0.18f, 1.3f, 0f), new Vector3(0.3f, 1.35f, 0.45f), 0.09f, MagicalSkin);
        Prim(PrimitiveType.Sphere, pose, new Vector3(0.3f, 1.35f, 0.45f), Vector3.one * 0.11f, MagicalWhite);
        BoneShape(pose, new Vector3(0.3f, 1.3f, 0.42f), new Vector3(0.38f, 1.72f, 0.8f), 0.05f, MagicalPink);
        var star = StarShape("wand-star", pose, 0.16f, 0.07f, 0.05f, StarGold, true);
        star.transform.localPosition = new Vector3(0.39f, 1.78f, 0.85f);
        wandTip = star.transform;
        Stream(wandTip, StarGold, new Color(1f, 0.6f, 0.9f, 0f), 25f, 0.2f, 0.4f, 0.6f);
        var trail = Holder("trail", pose);
        trail.localPosition = new Vector3(0f, 0.6f, -0.3f);
        Stream(trail, MagicalPink, new Color(1f, 0.7f, 0.9f, 0f), 70f, 0.4f, 0.4f, 1.4f);
        Stream(trail, MagicalWhite, new Color(1f, 1f, 0.6f, 0f), 40f, 0.25f, 0.7f, 1f);
        return girl;
    }

    static void BuildMagicalOutfit(Transform pose)
    {
        ConeShape(pose, new Vector3(0f, 0.48f, 0f), 0.5f, 0.6f, MagicalWhite, sides: 14);
        ConeShape(pose, new Vector3(0f, 0.55f, 0f), 0.46f, 0.6f, MagicalPink, sides: 14);
        BoneShape(pose, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 1.32f, 0f), 0.32f, MagicalWhite);
        Prim(PrimitiveType.Sphere, pose, new Vector3(-0.08f, 1.25f, 0.14f), Vector3.one * 0.12f, MagicalPink);
        Prim(PrimitiveType.Sphere, pose, new Vector3(0.08f, 1.25f, 0.14f), Vector3.one * 0.12f, MagicalPink);
        Prim(PrimitiveType.Sphere, pose, new Vector3(0f, 1.25f, 0.17f), Vector3.one * 0.07f, MagicalRibbon, glow: true);
        foreach (float side in new[] { -1f, 1f })
        {
            var hip = new Vector3(side * 0.1f, 0.6f, 0f);
            var knee = new Vector3(side * 0.12f, 0.3f, -0.1f);
            var foot = new Vector3(side * 0.12f, 0.02f, -0.2f);
            BoneShape(pose, hip, knee, 0.09f, MagicalSkin);
            BoneShape(pose, knee, foot, 0.12f, MagicalPink);   // boots
        }
    }

    static void BuildMagicalHead(Transform pose)
    {
        Prim(PrimitiveType.Sphere, pose, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.42f, MagicalSkin);
        Prim(PrimitiveType.Sphere, pose, new Vector3(0f, 1.66f, -0.05f), Vector3.one * 0.45f, MagicalHair);
        Prim(PrimitiveType.Sphere, pose, new Vector3(-0.07f, 1.75f, 0.14f), Vector3.one * 0.13f, MagicalHair);
        Prim(PrimitiveType.Sphere, pose, new Vector3(0.07f, 1.75f, 0.14f), Vector3.one * 0.13f, MagicalHair);
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Sphere, pose, new Vector3(side * 0.08f, 1.61f, 0.18f), new Vector3(0.09f, 0.12f, 0.06f),
                MagicalEyes);
            Prim(PrimitiveType.Sphere, pose, new Vector3(side * 0.07f, 1.64f, 0.21f), Vector3.one * 0.03f, Color.white,
                glow: true);
            Prim(PrimitiveType.Sphere, pose, new Vector3(side * 0.13f, 1.54f, 0.16f), new Vector3(0.06f, 0.03f, 0.03f),
                OnigiriBlush, glow: true);
            // a twin-tail streaming back from a red hair tie
            var tie = new Vector3(side * 0.24f, 1.72f, -0.08f);
            Prim(PrimitiveType.Sphere, pose, tie, Vector3.one * 0.09f, MagicalRibbon);
            BoneShape(pose, tie, new Vector3(side * 0.36f, 1.35f, -0.38f), 0.17f, MagicalHair);
            BoneShape(pose, new Vector3(side * 0.36f, 1.35f, -0.38f), new Vector3(side * 0.3f, 1f, -0.5f), 0.12f,
                MagicalHair);
        }
        var tiara = StarShape("tiara", pose, 0.07f, 0.03f, 0.02f, StarGold, true);
        tiara.transform.localPosition = new Vector3(0f, 1.82f, 0.13f);
    }

    // A puffy five-pointed star facing +z in the xy plane, its faces bulging `depth` out front and back.
    static GameObject StarShape(string name, Transform parent, float outer, float inner, float depth, Color color,
        bool glow = false)
    {
        var points = new Vector3[12];
        var triangles = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            float angle = (90f + i * 36f) * Mathf.Deg2Rad;
            float radius = i % 2 == 0 ? outer : inner;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            int next = (i + 1) % 10;
            AddTriangle(triangles, 10, i, next);
            AddTriangle(triangles, 11, next, i);
        }
        points[10] = Vector3.forward * depth;
        points[11] = Vector3.back * depth;
        return Facets(name, parent, points, triangles.ToArray(), color, glow);
    }

    // Lit gold that also glows a little (emission), so the costume star still shades as it turns.
    static Material StarGlow()
    {
        if (_starGlow != null)
        {
            return _starGlow;
        }
        _starGlow = new Material(Mat(StarGold));
        if (_starGlow.HasProperty("_EmissionColor"))
        {
            _starGlow.EnableKeyword("_EMISSION");
            _starGlow.SetColor("_EmissionColor", StarGold * 0.6f);
        }
        return _starGlow;
    }

    // ---- costume: cinnamon roll ----

    // A fat roll with a cinnamon swirl and white icing drizzle on top, spinning (faster when the car is).
    static System.Action<float, float> BuildCinnamonRoll(Transform model)
    {
        var roll = Holder("roll", model);
        Prim(PrimitiveType.Cylinder, roll, Vector3.up * 0.5f,
            new Vector3(CinnamonRadius * 2f, 0.45f, CinnamonRadius * 2f), DoughTan);
        Prim(PrimitiveType.Cylinder, roll, Vector3.up * 0.5f,
            new Vector3(CinnamonRadius * 1.9f, 0.47f, CinnamonRadius * 1.9f), CinnamonBrown);
        Prim(PrimitiveType.Cylinder, roll, Vector3.up * 0.5f,
            new Vector3(CinnamonRadius * 1.86f, 0.48f, CinnamonRadius * 1.86f), DoughTan);
        BuildCinnamonSwirl(roll);
        BuildIcing(roll, false);
        BuildIcing(roll, true);
        Prim(PrimitiveType.Sphere, roll, Vector3.up * 1f, new Vector3(0.5f, 0.12f, 0.5f), IcingWhite);
        float spin = 0f;
        return (time, speed) =>
        {
            spin += (70f + Mathf.Abs(speed) * 4f) * Time.deltaTime;
            roll.localRotation = Quaternion.Euler(Mathf.Sin(time * 2f) * 4f, spin % 360f, Mathf.Cos(time * 2f) * 4f);
        };
    }

    // The cinnamon line spiralling out from the middle of the top.
    static void BuildCinnamonSwirl(Transform roll)
    {
        float turns = CinnamonTurns * Mathf.PI * 2f;
        var previous = Vector3.up * 0.97f;
        for (float angle = 0.3f; angle <= turns; angle += 0.3f)
        {
            float radius = 0.12f + angle / turns * (CinnamonRadius - 0.25f);
            var point = new Vector3(Mathf.Cos(angle) * radius, 0.97f, Mathf.Sin(angle) * radius);
            BoneShape(roll, previous, point, 0.13f, CinnamonBrown);
            previous = point;
        }
    }

    // One zig-zag of icing across the top, along x (or z when `across`).
    static void BuildIcing(Transform roll, bool across)
    {
        float reach = CinnamonRadius * 0.85f;
        var previous = Vector3.zero;
        for (int i = 0; i <= 8; i++)
        {
            float along = -reach + i * reach / 4f;
            float sideways = (i % 2 == 0 ? 1f : -1f) * Mathf.Sqrt(Mathf.Max(0f, reach * reach - along * along)) * 0.8f;
            var point = across ? new Vector3(sideways, 1.03f, along) : new Vector3(along, 1.03f, sideways);
            if (i > 0)
            {
                BoneShape(roll, previous, point, 0.11f, IcingWhite);
            }
            previous = point;
        }
    }

    // ---- costume: rice ball ----

    // An onigiri: a puffy white rounded triangle with a nori strip, sesame seeds and a little face, hopping along.
    static System.Action<float, float> BuildOnigiri(Transform model)
    {
        var hopper = Holder("hopper", model);
        var outline = RoundedTriangle();
        BuildRiceMesh(hopper, outline);
        Prim(PrimitiveType.Cube, hopper, new Vector3(0f, 0.38f, 0f),
            new Vector3(1.1f, 0.8f, (OnigiriDepth + OnigiriBulge * 0.8f) * 2f), NoriGreen);
        foreach (float face in new[] { -1f, 1f })
        {
            BuildOnigiriFace(hopper, face);
        }
        return (time, speed) =>
        {
            float hop = Mathf.Abs(Mathf.Sin(time * 4f));
            float squash = (1f - hop) * 0.08f;
            hopper.localPosition = Vector3.up * (hop * 0.35f);
            hopper.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash);
        };
    }

    // The rice: front and back faces fanned from a puffed-out middle, joined round the rounded-triangle edge.
    static void BuildRiceMesh(Transform hopper, Vector2[] outline)
    {
        int count = outline.Length;
        var points = new Vector3[count * 2 + 2];
        var triangles = new List<int>();
        for (int i = 0; i < count; i++)
        {
            points[i] = new Vector3(outline[i].x, outline[i].y, OnigiriDepth);
            points[count + i] = new Vector3(outline[i].x, outline[i].y, -OnigiriDepth);
            int next = (i + 1) % count;
            AddTriangle(triangles, count * 2, i, next);
            AddTriangle(triangles, count * 2 + 1, count + next, count + i);
            AddTriangle(triangles, i, count + i, next);
            AddTriangle(triangles, next, count + i, count + next);
        }
        points[count * 2] = new Vector3(0f, 0.95f, OnigiriDepth + OnigiriBulge);
        points[count * 2 + 1] = new Vector3(0f, 0.95f, -OnigiriDepth - OnigiriBulge);
        Facets("rice", hopper, points, triangles.ToArray(), RiceWhite);
    }

    // A triangle (2.8 wide, 2.4 tall, bottom on y = 0) with its corners rounded off, counter-clockwise.
    static Vector2[] RoundedTriangle()
    {
        var corners = new[]
        {
            new Vector2(0f, 1.95f),
            new Vector2(-0.95f, 0.45f),
            new Vector2(0.95f, 0.45f),
        };
        var outline = new List<Vector2>();
        for (int i = 0; i < corners.Length; i++)
        {
            var corner = corners[i];
            var into = Outward(corners[(i + 2) % 3], corner);
            var outOf = Outward(corner, corners[(i + 1) % 3]);
            float start = Mathf.Atan2(into.y, into.x);
            float end = Mathf.Atan2(outOf.y, outOf.x);
            while (end < start)
            {
                end += Mathf.PI * 2f;
            }
            for (int step = 0; step <= OnigiriCornerSteps; step++)
            {
                float angle = Mathf.Lerp(start, end, step / (float)OnigiriCornerSteps);
                outline.Add(corner + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OnigiriCornerRadius);
            }
        }
        return outline.ToArray();
    }

    // The outward normal of a counter-clockwise edge.
    static Vector2 Outward(Vector2 from, Vector2 to)
    {
        var edge = to - from;
        return new Vector2(edge.y, -edge.x).normalized;
    }

    // Eyes, blush, a small smile and a few sesame seeds on one face (`face` +1 = front, -1 = back).
    static void BuildOnigiriFace(Transform hopper, float face)
    {
        float surface = face * (OnigiriDepth + OnigiriBulge * 0.75f);
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Sphere, hopper, new Vector3(side * 0.3f, 1.05f, surface),
                new Vector3(0.13f, 0.17f, 0.06f), OnigiriFace);
            Prim(PrimitiveType.Sphere, hopper, new Vector3(side * 0.55f, 0.9f, surface * 0.98f),
                new Vector3(0.2f, 0.1f, 0.04f), OnigiriBlush, glow: true);
        }
        BoneShape(hopper, new Vector3(-0.08f, 0.9f, surface), new Vector3(0.08f, 0.9f, surface), 0.05f, OnigiriFace);
        float sesameDepth = face * (OnigiriDepth + OnigiriBulge * 0.4f);
        Prim(PrimitiveType.Sphere, hopper, new Vector3(-0.35f, 1.55f, sesameDepth), new Vector3(0.05f, 0.09f, 0.04f),
            OnigiriFace, new Vector3(0f, 0f, 30f));
        Prim(PrimitiveType.Sphere, hopper, new Vector3(0.25f, 1.7f, sesameDepth), new Vector3(0.05f, 0.09f, 0.04f),
            OnigiriFace, new Vector3(0f, 0f, -20f));
        Prim(PrimitiveType.Sphere, hopper, new Vector3(0.6f, 1.25f, sesameDepth * 0.95f),
            new Vector3(0.05f, 0.09f, 0.04f), OnigiriFace, new Vector3(0f, 0f, 60f));
    }

    // ---- costume: big star ----

    // A big puffy golden star, glowing and slowly spinning, with sparkles trailing off it.
    static System.Action<float, float> BuildStar(Transform model)
    {
        var spinner = Holder("spinner", model);
        spinner.localPosition = Vector3.up * StarOuter;
        var star = StarShape("star", spinner, StarOuter, StarInner, StarDepth, StarGold);
        star.GetComponent<Renderer>().sharedMaterial = StarGlow();
        var light = spinner.gameObject.AddComponent<Light>();
        light.color = StarGold;
        light.range = 6f;
        light.intensity = 1.5f;
        Stream(spinner, StarGold, new Color(1f, 1f, 0.8f, 0f), 25f, 0.3f, 0.6f, 0.8f);
        return (time, speed) =>
        {
            spinner.localPosition = Vector3.up * (StarOuter + Mathf.Sin(time * 2f) * 0.15f);
            spinner.localRotation = Quaternion.Euler(0f, time * StarSpin, 0f);
        };
    }
}
