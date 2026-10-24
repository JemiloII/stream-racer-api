// Chaos.Witch: the witching hour. A green-faced witch on a broom (pointy hat, purple robe, a purple sparkle trail)
// flies in low and rides along with the field, gliding up beside 3-6 random racers in turn and zapping one every
// ~5 s with crackling green lightning; every car it hits becomes a spooky costume for a while (a surfing skeleton, a
// bat or a rolling jack-o-lantern). Then she flies off. The costumes themselves live in Chaos.Costume.cs.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    static readonly Color WitchRobe = new(0.25f, 0.08f, 0.32f);
    static readonly Color WitchBlack = new(0.07f, 0.06f, 0.08f);
    static readonly Color WitchSkin = new(0.35f, 0.72f, 0.3f);
    static readonly Color WitchEyes = new(1f, 0.9f, 0.2f);
    static readonly Color BroomWood = new(0.42f, 0.26f, 0.12f);
    static readonly Color BroomStraw = new(0.85f, 0.7f, 0.35f);
    static readonly Color WitchSparkle = new(0.7f, 0.3f, 1f);
    static readonly Color WitchBolt = new(0.35f, 1f, 0.3f);

    static readonly Color BoneWhite = new(0.95f, 0.94f, 0.88f);
    static readonly Color BoneHole = new(0.05f, 0.05f, 0.06f);
    static readonly Color SurfboardTeal = new(0.1f, 0.7f, 0.8f);
    static readonly Color BatBlack = new(0.1f, 0.09f, 0.11f);
    static readonly Color BatBelly = new(0.24f, 0.19f, 0.22f);
    static readonly Color BatEyes = new(1f, 0.1f, 0.05f);
    static readonly Color PumpkinOrange = new(1f, 0.5f, 0.05f);
    static readonly Color PumpkinDeep = new(0.88f, 0.38f, 0.03f);
    static readonly Color PumpkinStem = new(0.25f, 0.45f, 0.15f);
    static readonly Color PumpkinGlow = new(1f, 0.85f, 0.25f);

    const float PumpkinRadius = 1.3f;           // in costume units (a CostumeCarLength car)
    const int PumpkinRibs = 5;
    const float BatFlapSpeed = 9f;              // radians a second
    const float BatFlapAngle = 35f;             // degrees up and down

    // Which wing outline points the finger struts run out to.
    static readonly int[] BatFingerTips =
    {
        2,
        3,
        5,
        7,
    };

    static IEnumerator Witch()
    {
        if (Racers().Count == 0)
        {
            yield break;
        }
        Announce("WITCHING HOUR", "WITCH");
        var witch = BuildWitch(out var hand);
        var visit = CasterVisit(witch, hand, WitchBolt, true, WitchCostumes);
        while (visit.MoveNext())
        {
            yield return visit.Current;
        }
    }

    // ---- the witch ----

    // Life-size and facing +z, sitting on the broom at the origin; `hand` is her raised casting hand.
    static Transform BuildWitch(out Transform hand)
    {
        var witch = CasterBody("witch", out var body);
        BuildBroom(body);
        ConeShape(body, new Vector3(0f, -0.3f, 0f), 0.45f, 1.5f, WitchRobe);
        BoneShape(body, new Vector3(-0.15f, -0.15f, 0.1f), new Vector3(-0.15f, -0.3f, 0.5f), 0.14f, WitchBlack);
        BoneShape(body, new Vector3(0.15f, -0.15f, 0.1f), new Vector3(0.15f, -0.3f, 0.5f), 0.14f, WitchBlack);
        // left hand on the broom, right hand raised forward to cast
        BoneShape(body, new Vector3(-0.22f, 0.95f, 0.05f), new Vector3(-0.06f, 0.08f, 0.6f), 0.13f, WitchRobe);
        Prim(PrimitiveType.Sphere, body, new Vector3(-0.06f, 0.08f, 0.6f), Vector3.one * 0.13f, WitchSkin);
        BoneShape(body, new Vector3(0.22f, 0.95f, 0.05f), new Vector3(0.38f, 1.05f, 0.7f), 0.13f, WitchRobe);
        Prim(PrimitiveType.Sphere, body, new Vector3(0.38f, 1.05f, 0.7f), Vector3.one * 0.13f, WitchSkin);
        hand = Holder("hand", body);
        hand.localPosition = new Vector3(0.4f, 1.07f, 0.78f);
        Stream(hand, WitchBolt, new Color(0.3f, 1f, 0.3f, 0f), 20f, 0.25f, 0.3f, 0.5f);
        BuildWitchHead(body);
        var tail = Holder("tail", body);
        tail.localPosition = new Vector3(0f, 0f, -1.8f);
        Stream(tail, WitchSparkle, new Color(0.3f, 0.1f, 0.5f, 0f), 70f, 0.4f, 0.4f, 1.4f);
        Stream(tail, WitchBolt, new Color(0.2f, 0.6f, 0.2f, 0f), 20f, 0.25f, 0.6f, 1f);
        return witch;
    }

    static void BuildBroom(Transform body)
    {
        BoneShape(body, new Vector3(0f, 0f, -1.1f), new Vector3(0f, 0f, 1.3f), 0.09f, BroomWood);
        ConeShape(body, new Vector3(0f, 0f, -1.8f), 0.3f, 0.8f, BroomStraw, new Vector3(90f, 0f, 0f), sides: 8);
        Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 0f, -1.05f), new Vector3(0.14f, 0.05f, 0.14f), WitchRobe,
            new Vector3(90f, 0f, 0f));
    }

    static void BuildWitchHead(Transform body)
    {
        Prim(PrimitiveType.Sphere, body, new Vector3(0f, 1.32f, 0.02f), Vector3.one * 0.42f, WitchSkin);
        ConeShape(body, new Vector3(0f, 1.3f, 0.2f), 0.06f, 0.2f, WitchSkin, new Vector3(100f, 0f, 0f), sides: 6);
        Prim(PrimitiveType.Sphere, body, new Vector3(-0.08f, 1.38f, 0.19f), Vector3.one * 0.07f, WitchEyes, glow: true);
        Prim(PrimitiveType.Sphere, body, new Vector3(0.08f, 1.38f, 0.19f), Vector3.one * 0.07f, WitchEyes, glow: true);
        // stringy black hair down her back
        for (int i = 0; i < 5; i++)
        {
            float across = (i - 2) * 0.08f;
            BoneShape(body, new Vector3(across, 1.42f, -0.12f), new Vector3(across * 1.6f, 0.95f, -0.3f), 0.06f,
                WitchBlack);
        }
        Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 1.5f, 0f), new Vector3(0.85f, 0.015f, 0.85f), WitchBlack);
        Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 1.55f, 0f), new Vector3(0.52f, 0.04f, 0.52f), WitchRobe);
        ConeShape(body, new Vector3(0f, 1.51f, 0f), 0.27f, 0.9f, WitchBlack, new Vector3(-18f, 0f, 0f));
    }

    // ---- costume: surfing skeleton ----

    // A white skeleton riding a surfboard sideways, arms out, swaying with the waves.
    static System.Action<float, float> BuildSkeleton(Transform model)
    {
        var ride = Holder("ride", model);
        Prim(PrimitiveType.Sphere, ride, new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.12f, 4.3f), SurfboardTeal);
        Prim(PrimitiveType.Cube, ride, new Vector3(0f, 0.17f, 0f), new Vector3(0.12f, 0.12f, 3.6f), Color.white);
        var surfer = Holder("surfer", ride);
        surfer.localPosition = Vector3.up * 0.22f;
        BuildSkeletonLegs(surfer);
        BuildSkeletonChest(surfer);
        var arms = new[]
        {
            BuildSkeletonArm(surfer, -1f),
            BuildSkeletonArm(surfer, 1f),
        };
        BuildSkull(surfer);
        return (time, speed) =>
        {
            ride.localPosition = Vector3.up * (0.1f + Mathf.Sin(time * 2f) * 0.08f);
            ride.localRotation = Quaternion.Euler(Mathf.Sin(time * 1.3f) * 3f, 0f, Mathf.Sin(time * 1.7f) * 6f);
            surfer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2.1f + 1f) * 5f);
            arms[0].localRotation = Quaternion.Euler(Mathf.Sin(time * 3f) * 12f, 0f, 0f);
            arms[1].localRotation = Quaternion.Euler(-Mathf.Sin(time * 3f + 0.8f) * 12f, 0f, 0f);
        };
    }

    // The skeleton faces +x (the car's right), so its feet, hips and arms spread along z.
    static void BuildSkeletonLegs(Transform surfer)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var ankle = new Vector3(0f, 0.05f, side * 0.7f);
            var knee = new Vector3(0.15f, 0.5f, side * 0.45f);
            var hip = new Vector3(0f, 0.95f, side * 0.15f);
            Prim(PrimitiveType.Sphere, surfer, ankle + Vector3.right * 0.08f, new Vector3(0.3f, 0.08f, 0.14f),
                BoneWhite);
            BoneShape(surfer, ankle, knee, 0.1f, BoneWhite);
            BoneShape(surfer, knee, hip, 0.11f, BoneWhite);
        }
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0f, 0.98f, 0f), new Vector3(0.25f, 0.18f, 0.45f), BoneWhite);
    }

    static void BuildSkeletonChest(Transform surfer)
    {
        BoneShape(surfer, new Vector3(0f, 0.98f, 0f), new Vector3(0f, 1.88f, 0f), 0.08f, BoneWhite);
        BoneShape(surfer, new Vector3(0.15f, 1.3f, 0f), new Vector3(0.15f, 1.68f, 0f), 0.06f, BoneWhite);
        var widths = new[]
        {
            0.26f,
            0.3f,
            0.3f,
            0.27f,
        };
        for (int i = 0; i < widths.Length; i++)
        {
            float height = 1.3f + i * 0.12f;
            float width = widths[i];
            var frontLeft = new Vector3(0.14f, height, -width * 0.6f);
            var frontRight = new Vector3(0.14f, height, width * 0.6f);
            BoneShape(surfer, frontLeft, frontRight, 0.05f, BoneWhite);
            BoneShape(surfer, frontLeft, new Vector3(-0.08f, height + 0.03f, -width), 0.05f, BoneWhite);
            BoneShape(surfer, frontRight, new Vector3(-0.08f, height + 0.03f, width), 0.05f, BoneWhite);
        }
        BoneShape(surfer, new Vector3(0f, 1.72f, -0.32f), new Vector3(0f, 1.72f, 0.32f), 0.07f, BoneWhite);
    }

    // One arm held out to the side, from a shoulder pivot so it can balance up and down.
    static Transform BuildSkeletonArm(Transform surfer, float side)
    {
        var shoulder = Holder("arm", surfer);
        shoulder.localPosition = new Vector3(0f, 1.72f, side * 0.32f);
        var elbow = new Vector3(0f, 0.03f, side * 0.48f);
        var wrist = new Vector3(0f, 0.13f, side * 0.88f);
        BoneShape(shoulder, Vector3.zero, elbow, 0.08f, BoneWhite);
        BoneShape(shoulder, elbow, wrist, 0.07f, BoneWhite);
        Prim(PrimitiveType.Sphere, shoulder, wrist + new Vector3(0f, 0.02f, side * 0.08f),
            new Vector3(0.08f, 0.14f, 0.16f), BoneWhite);
        return shoulder;
    }

    static void BuildSkull(Transform surfer)
    {
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0.02f, 2.05f, 0f), Vector3.one * 0.42f, BoneWhite);
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0.08f, 1.88f, 0f), new Vector3(0.26f, 0.14f, 0.28f), BoneWhite);
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0.18f, 2.07f, -0.09f), Vector3.one * 0.11f, BoneHole);
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0.18f, 2.07f, 0.09f), Vector3.one * 0.11f, BoneHole);
        Prim(PrimitiveType.Sphere, surfer, new Vector3(0.21f, 1.98f, 0f), new Vector3(0.04f, 0.06f, 0.05f), BoneHole);
        Prim(PrimitiveType.Cube, surfer, new Vector3(0.2f, 1.9f, 0f), new Vector3(0.02f, 0.02f, 0.16f), BoneHole);
    }

    // ---- costume: bat ----

    // A fat black bat with red eyes, flapping membrane wings and bobbing up and down over the road.
    static System.Action<float, float> BuildBat(Transform model)
    {
        var flyer = Holder("flyer", model);
        Prim(PrimitiveType.Sphere, flyer, Vector3.zero, new Vector3(0.9f, 0.85f, 1.3f), BatBlack);
        Prim(PrimitiveType.Sphere, flyer, new Vector3(0f, -0.1f, 0.1f), new Vector3(0.7f, 0.65f, 1f), BatBelly);
        Prim(PrimitiveType.Sphere, flyer, new Vector3(0f, 0.25f, 0.85f), Vector3.one * 0.75f, BatBlack);
        ConeShape(flyer, new Vector3(-0.22f, 0.5f, 0.82f), 0.15f, 0.45f, BatBlack, new Vector3(0f, 0f, 20f), sides: 4);
        ConeShape(flyer, new Vector3(0.22f, 0.5f, 0.82f), 0.15f, 0.45f, BatBlack, new Vector3(0f, 0f, -20f), sides: 4);
        Prim(PrimitiveType.Sphere, flyer, new Vector3(-0.17f, 0.35f, 1.17f), Vector3.one * 0.14f, BatEyes, glow: true);
        Prim(PrimitiveType.Sphere, flyer, new Vector3(0.17f, 0.35f, 1.17f), Vector3.one * 0.14f, BatEyes, glow: true);
        var fangDown = new Vector3(180f, 0f, 0f);
        ConeShape(flyer, new Vector3(-0.08f, 0.08f, 1.16f), 0.04f, 0.12f, Color.white, fangDown, sides: 4);
        ConeShape(flyer, new Vector3(0.08f, 0.08f, 1.16f), 0.04f, 0.12f, Color.white, fangDown, sides: 4);
        var left = BuildBatWing(flyer, -1f);
        var right = BuildBatWing(flyer, 1f);
        return (time, speed) =>
        {
            flyer.localPosition = Vector3.up * (1.4f + Mathf.Sin(time * 3f) * 0.35f);
            float flap = Mathf.Sin(time * BatFlapSpeed) * BatFlapAngle;
            right.localRotation = Quaternion.Euler(0f, 0f, flap);
            left.localRotation = Quaternion.Euler(0f, 0f, -flap);
        };
    }

    // A scalloped membrane fanning out from the shoulder, with dark finger struts; `side` -1 = left, 1 = right.
    static Transform BuildBatWing(Transform flyer, float side)
    {
        var shoulder = Holder("wing", flyer);
        shoulder.localPosition = new Vector3(side * 0.4f, 0.15f, 0f);
        var outline = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.15f, 0f, 0.4f),
            new Vector3(1f, 0.12f, 0.65f),
            new Vector3(1.9f, 0.05f, 0.25f),
            new Vector3(1.5f, 0f, -0.08f),
            new Vector3(1.75f, 0f, -0.55f),
            new Vector3(1f, 0f, -0.32f),
            new Vector3(0.5f, 0f, -0.72f),
            new Vector3(0f, 0f, -0.4f),
        };
        var points = outline.Select(point => new Vector3(point.x * side, point.y, point.z)).ToArray();
        var triangles = new List<int>();
        for (int i = 1; i < points.Length - 1; i++)
        {
            AddTriangle(triangles, 0, i, i + 1);
        }
        Facets("membrane", shoulder, points, triangles.ToArray(), BatBlack);
        foreach (int tip in BatFingerTips)
        {
            BoneShape(shoulder, Vector3.zero, points[tip], 0.05f, BatBelly);
        }
        return shoulder;
    }

    // ---- costume: rolling pumpkin ----

    // A ribbed jack-o-lantern with a glowing face, rolling along with the car (faster car, faster roll).
    static System.Action<float, float> BuildPumpkin(Transform model)
    {
        var roller = Holder("roller", model);
        roller.localPosition = Vector3.up * PumpkinRadius;
        float diameter = PumpkinRadius * 2f;
        Prim(PrimitiveType.Sphere, roller, Vector3.zero, Vector3.one * (diameter * 0.97f), PumpkinDeep);
        for (int i = 0; i < PumpkinRibs; i++)
        {
            Prim(PrimitiveType.Sphere, roller, Vector3.zero, new Vector3(diameter * 0.6f, diameter * 0.95f, diameter),
                i % 2 == 0 ? PumpkinOrange : PumpkinDeep, new Vector3(0f, i * 180f / PumpkinRibs, 0f));
        }
        Prim(PrimitiveType.Cylinder, roller, Vector3.up * (PumpkinRadius * 0.98f), new Vector3(0.25f, 0.22f, 0.25f),
            PumpkinStem, new Vector3(8f, 0f, 12f));
        Prim(PrimitiveType.Sphere, roller, new Vector3(0.22f, PumpkinRadius * 0.98f, 0.1f),
            new Vector3(0.4f, 0.06f, 0.22f), PumpkinStem, new Vector3(0f, 30f, -15f));
        BuildPumpkinFace(roller);
        var light = roller.gameObject.AddComponent<Light>();
        light.color = PumpkinGlow;
        light.range = 5f;
        light.intensity = 1.5f;
        float roll = 0f;
        return (time, speed) =>
        {
            roll += speed / PumpkinRadius * Mathf.Rad2Deg * Time.deltaTime;   // rolling without slipping
            roller.localRotation = Quaternion.Euler(roll % 360f, 0f, 0f);
            light.intensity = 1.3f + Mathf.PerlinNoise(time * 4f, 0f) * 0.6f;   // candle flicker
        };
    }

    // Triangle eyes, a triangle nose and a wide grin, laid on the pumpkin's front (+z) surface.
    static void BuildPumpkinFace(Transform roller)
    {
        var points = new List<Vector3>();
        AddFacePiece(points, new Vector2(-0.6f, 0.25f), new Vector2(-0.2f, 0.25f), new Vector2(-0.4f, 0.6f));
        AddFacePiece(points, new Vector2(0.2f, 0.25f), new Vector2(0.6f, 0.25f), new Vector2(0.4f, 0.6f));
        AddFacePiece(points, new Vector2(-0.12f, 0f), new Vector2(0.12f, 0f), new Vector2(0f, 0.18f));
        for (int i = 0; i < 6; i++)
        {
            float left = -0.75f + i * 0.25f;
            float right = left + 0.25f;
            var topLeft = new Vector2(left, GrinTop(left));
            var topRight = new Vector2(right, GrinTop(right));
            var bottomLeft = new Vector2(left, GrinBottom(left));
            var bottomRight = new Vector2(right, GrinBottom(right));
            AddFacePiece(points, topLeft, topRight, bottomLeft);
            AddFacePiece(points, topRight, bottomRight, bottomLeft);
        }
        Facets("face", roller, points.ToArray(), Enumerable.Range(0, points.Count).ToArray(), PumpkinGlow, glow: true);
    }

    static void AddFacePiece(List<Vector3> points, Vector2 first, Vector2 second, Vector2 third)
    {
        points.Add(OnPumpkin(first));
        points.Add(OnPumpkin(second));
        points.Add(OnPumpkin(third));
    }

    // The grin's edges (a smile: higher at the ends); the top dips at +-0.25 to leave two teeth sticking down.
    static float GrinTop(float across)
    {
        float tooth = Mathf.Abs(Mathf.Abs(across) - 0.25f) < 0.01f ? 0.12f : 0f;
        return -0.2f + across * across * 0.4f - tooth;
    }

    static float GrinBottom(float across) => -0.5f + across * across * 0.5f;

    // A face point (x, y on the front) pushed out onto the sphere's surface, just proud of it.
    static Vector3 OnPumpkin(Vector2 point)
    {
        float radius = PumpkinRadius * 1.02f;
        float depth = Mathf.Sqrt(Mathf.Max(0.01f, radius * radius - point.x * point.x - point.y * point.y));
        return new Vector3(point.x, point.y, depth);
    }
}
