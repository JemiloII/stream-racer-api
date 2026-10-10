// Game.Surfaces: road surfaces laid for one race (ice, puddles, mud, snow), from POST /surface/* and the weather.
// Placement, hit tests and snow packing are Pure (surfaces section); this file maps them onto the circuit, draws
// each patch as a flat mesh just above the road, and ~15 times a second applies what every racing car is driving
// on: grip through the game's own GripFor, top speed + drag through the AI speed postfix (Patches.AfterAiSurface).
using System.Collections.Specialized;
using UnityEngine.Rendering;
using UnityStandardAssets.Utility;
using UnityStandardAssets.Vehicles.Car;
using SurfaceKind = StreamRacerApi.Pure.SurfaceKind;
using SurfacePatch = StreamRacerApi.Pure.SurfacePatch;
using SurfaceEffect = StreamRacerApi.Pure.SurfaceEffect;

namespace StreamRacerApi;

static partial class Game
{
    // ---- tunables ----
    const float SurfaceTickSeconds = 0.066f;       // ~15 Hz hit test
    const float RoadHalfWidth = 6f;                // assumed road half width around the route line (off road = 18)
    const float StretchHalfWidth = RoadHalfWidth + 1.5f;
    const float SurfaceStartClear = 40f;           // never on the grid
    const float SurfaceFinishClear = 15f;          // nor right on the line
    const float SurfaceAheadOfRear = 30f;          // new patches go ahead of the last car, so someone still drives them
    const float SurfaceGap = 10f;                  // between two patches of the same kind
    const int MaxSurfacePatches = 60;
    const int MaxSurfaceCount = 20;
    const float MinSurfaceLength = 10f, MaxSurfaceLength = 300f;
    const int IceCount = 3, PuddleCount = 8, MudCount = 3, SnowCount = 3;
    const float IceLength = 40f, MudLength = 30f, SnowLength = 50f;
    const float IceGrip = 0.35f;
    const float IceCornerDegrees = 25f;            // ice weight +1 per this many degrees the stretch turns
    const float IceStraightWeight = 0.1f;          // weight of a dead straight stretch (corners win)
    const float PuddleSpeed = 0.55f, PuddleGrip = 0.45f;   // a real splash: lifts off and twitches if it hits mid-corner
    const float PuddleSpinChance = 0.35f;          // chance to spin out on a puddle in a ~60° bend (scales with the bend)
    const float PuddleSpinDegrees = 60f;           // the bend (over PuddleSpinLook units) that gives the full chance
    const float PuddleSpinLook = 30f;
    const float PuddleSpinMinSpeed = 12f;          // m/s: slower cars just splash through
    const float PuddleSpinYaw = 3.5f;              // rad/s kick into the bend: the tail steps out
    const float PuddleSpinGrip = 0.15f, PuddleSpinSeconds = 1.1f;
    const float PuddleLingerSeconds = 1.5f;        // a puddle's slow outlives the splash
    const float PuddleMinRadius = 1.5f, PuddleMaxRadius = 3f;
    const int MudBlobsPerPatch = 2, SnowBlobsPerPatch = 3;         // a "patch" of mud / blob-snow = this many blobs
    const float MudMinRadius = 3.5f, MudMaxRadius = 6f;            // large brown puddles
    const float SnowMinRadius = 2.5f, SnowMaxRadius = 4.5f;
    const float BlobRaggedness = 0.3f;                             // mud / snow blob edges wobble this much (x radius)
    const float MudSpeed = 0.5f, MudDrag = 0.25f, MudBogDrag = 1.6f, BogSpeed = 14f;
    const float SnowSpeed = 0.6f, SnowGrip = 0.8f;
    const float SnowPackPerPass = 0.12f, SnowPackFloor = 0.2f;
    const float EffectLingerSeconds = 0.25f;       // an effect outlives the tick that found it (ticks are ~0.07 s apart)
    const float GripRefreshEvery = 0.25f, GripHoldSeconds = 0.45f;
    const float SurfaceMeshStep = 2f;              // route units between strip rows
    const float SurfaceLift = 0.15f;               // above the road, against z-fighting
    const int PuddleSegments = 20;
    const float GroundProbe = 6f;                  // raycast this far above/below the route line to find the road
    static readonly Color IceColor = Color.white;                   // the colour is in IceTexture (opaque road recolour)
    const float EdgeProbeStep = 0.5f, EdgeProbeMax = 14f;          // finding the road's real edges, sideways
    const float EdgeHeightJump = 0.4f;                             // a kerb / barrier / drop this tall ends the road
    static readonly Color PuddleColor = new(0.08f, 0.14f, 0.3f, 0.6f);
    static readonly Color MudColor = new(0.36f, 0.24f, 0.12f, 0.92f);
    static readonly Color SnowColor = new(0.97f, 0.98f, 1f, 1f);
    static readonly Color SnowPackedColor = new(0.5f, 0.5f, 0.54f, 1f);

    // One laid patch and what draws it (Visual is null when drawing failed: the effect still works).
    class LaidSurface
    {
        public SurfacePatch Patch;
        public GameObject Visual;
        public Mesh Mesh;
        public Material Material;
        public float PaintedStrength = 1f;
    }

    static readonly List<LaidSurface> _laid = new();
    static readonly Dictionary<CarController, (SurfaceEffect effect, float until)> SurfaceEffects = new();
    static readonly Dictionary<Vehicle, (float grip, float at)> GripRefreshed = new();
    static readonly System.Random SurfaceRandom = new();
    static float _nextSurfaceTick;
    static bool _surfaceTickWarned, _surfaceDrawWarned;

    // ---- API ----

    // POST /surface/ice|puddles|mud|snow?count=&length=, POST /surface/clear.
    public static object SurfaceRequest(string target, NameValueCollection query, ref int status)
    {
        if (target == "clear")
        {
            return new
            {
                ok = true,
                affected = ClearSurfaces(),
            };
        }
        var kind = ParseSurfaceKind(target);
        if (kind == null)
        {
            status = 404;
            return new { error = "need /surface/ice|puddles|mud|snow|clear" };
        }
        if (!Running && !InLobby)
        {
            status = 409;
            return new { error = "no race or lobby" };
        }
        int count = int.TryParse(query["count"], out var parsedCount) ? parsedCount : DefaultSurfaceCount(kind.Value);
        float length = float.TryParse(query["length"], out var parsedLength)
            ? parsedLength
            : DefaultSurfaceLength(kind.Value);
        var placed = LaySurfaces(kind.Value, count, length);
        if (placed.Count == 0)
        {
            status = 409;
            return new { error = "no room on the road (no circuit, too full, or too close to the finish)" };
        }
        return new
        {
            ok = true,
            kind = SurfaceName(kind.Value),
            patches = placed.Select(SurfacePatchDto).ToList(),
        };
    }

    public static object SurfacesDto() => new
    {
        count = _laid.Count,
        patches = _laid.Select(laid => SurfacePatchDto(laid.Patch)).ToList(),
    };

    static object SurfacePatchDto(SurfacePatch patch) => new
    {
        kind = patch.Kind.ToString().ToLowerInvariant(),
        start = Round1(patch.Start),
        end = Round1(patch.End),
        lateral = Round1(patch.Lateral),
        halfWidth = Round1(patch.HalfWidth),
        strength = Mathf.Round(patch.Strength * 100f) / 100f,
    };

    static float Round1(float value) => Mathf.Round(value * 10f) / 10f;

    static SurfaceKind? ParseSurfaceKind(string name)
    {
        switch ((name ?? "").ToLowerInvariant())
        {
            case "ice": return SurfaceKind.Ice;
            case "puddle":
            case "puddles": return SurfaceKind.Puddle;
            case "mud": return SurfaceKind.Mud;
            case "snow": return SurfaceKind.Snow;
            default: return null;
        }
    }

    // The route name, used as the `surface` event's kind.
    static string SurfaceName(SurfaceKind kind) =>
        kind == SurfaceKind.Puddle ? "puddles" : kind.ToString().ToLowerInvariant();

    static int DefaultSurfaceCount(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Ice => IceCount,
        SurfaceKind.Puddle => PuddleCount,
        SurfaceKind.Mud => MudCount,
        _ => SnowCount,
    };

    static float DefaultSurfaceLength(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Ice => IceLength,
        SurfaceKind.Mud => MudLength,
        _ => SnowLength,
    };

    // ---- laying and clearing ----

    // Places up to `count` patches on the road still ahead of the field, draws them and emits `surface`.
    // Puddles ignore `length` (their size is random). Returns what was placed (possibly nothing).
    public static List<SurfacePatch> LaySurfaces(SurfaceKind kind, int count, float length)
    {
        var circuit = Instances.WaypointController?.GetCircuit();
        if (circuit == null)
        {
            return new List<SurfacePatch>();
        }
        count = Mathf.Clamp(count, 1, MaxSurfaceCount);
        count = Mathf.Min(count, MaxSurfacePatches - _laid.Count);
        length = Mathf.Clamp(length, MinSurfaceLength, MaxSurfaceLength);
        var (from, to) = SurfaceRange(circuit);
        var existing = _laid.Select(laid => laid.Patch);
        // Puddles and mud are blobs on the road; snow is either blobs or a stretch of road gone white (coin flip);
        // ice is always a stretch of road recoloured.
        bool snowBlobs = kind == SurfaceKind.Snow && SurfaceRandom.NextDouble() < 0.5;
        var placed = kind == SurfaceKind.Puddle
            ? Pure.PlacePuddles(SurfaceRandom, count, from, to, RoadHalfWidth, PuddleMinRadius, PuddleMaxRadius, existing)
            : kind == SurfaceKind.Mud
            ? Pure.PlacePuddles(SurfaceRandom, count * MudBlobsPerPatch, from, to, RoadHalfWidth, MudMinRadius, MudMaxRadius, existing, SurfaceKind.Mud)
            : snowBlobs
            ? Pure.PlacePuddles(SurfaceRandom, count * SnowBlobsPerPatch, from, to, RoadHalfWidth, SnowMinRadius, SnowMaxRadius, existing, SurfaceKind.Snow)
            : Pure.PlaceStretches(SurfaceRandom, kind, count, length, from, to, StretchHalfWidth,
                kind == SurfaceKind.Ice ? start => IceWeight(circuit, start, length) : null, existing, SurfaceGap);
        foreach (var patch in placed)
        {
            _laid.Add(DrawSurface(circuit, patch));
        }
        if (placed.Count > 0)
        {
            Plugin.Emit("surface", new
            {
                kind = SurfaceName(kind),
                patches = placed.Select(SurfacePatchDto).ToList(),
            });
        }
        return placed;
    }

    // Removes every patch (race end, new lobby, POST /surface/clear). Running grip effects restore themselves.
    public static int ClearSurfaces()
    {
        int removed = _laid.Count;
        foreach (var laid in _laid)
        {
            DestroySurfaceVisual(laid);
        }
        _laid.Clear();
        SurfaceEffects.Clear();
        GripRefreshed.Clear();
        if (removed > 0)
        {
            Plugin.Emit("surface", new
            {
                kind = "clear",
                patches = new List<object>(),
            });
        }
        return removed;
    }

    static void DestroySurfaceVisual(LaidSurface laid)
    {
        if (laid.Visual != null)
        {
            Object.Destroy(laid.Visual);
        }
        if (laid.Mesh != null)
        {
            Object.Destroy(laid.Mesh);
        }
        if (laid.Material != null)
        {
            Object.Destroy(laid.Material);
        }
    }

    // Road still to be driven: past the grid and the last car, short of the finish line.
    static (float from, float to) SurfaceRange(WaypointCircuit circuit)
    {
        float finish = FinishLine(out _, out _, out var lineDistance) && lineDistance > 0f ? lineDistance : 0f;
        if (finish <= 0f)
        {
            finish = Vehicles().Select(FinishAt).DefaultIfEmpty(0f).Max();
        }
        if (finish <= 0f)
        {
            finish = circuit.Length();
        }
        var racing = Vehicles().Where(vehicle => !vehicle.HasFinished() && vehicle.Car() != null).ToList();
        float rear = racing.Count > 0 ? racing.Min(vehicle => vehicle.Progress()) : 0f;
        return (Mathf.Max(SurfaceStartClear, rear + SurfaceAheadOfRear), finish - SurfaceFinishClear);
    }

    // How much a stretch turns (start -> middle -> end, flat): ice prefers corners.
    static float IceWeight(WaypointCircuit circuit, float start, float length)
    {
        var first = FlatDirection(circuit.GetRoutePoint(start).Direction());
        var middle = FlatDirection(circuit.GetRoutePoint(start + length * 0.5f).Direction());
        var last = FlatDirection(circuit.GetRoutePoint(start + length).Direction());
        float turn = Vector3.Angle(first, middle) + Vector3.Angle(middle, last);
        return IceStraightWeight + turn / IceCornerDegrees;
    }

    static Vector3 FlatDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude < 1e-6f ? Vector3.forward : direction.normalized;
    }

    // ---- per-tick effect ----

    public static void SurfacesTick()
    {
        if ((_laid.Count == 0 && !Weather.SnowOnGround) || !Running || Time.unscaledTime < _nextSurfaceTick)
        {
            return;
        }
        _nextSurfaceTick = Time.unscaledTime + SurfaceTickSeconds;
        try
        {
            var circuit = Instances.WaypointController?.GetCircuit();
            if (circuit == null)
            {
                return;
            }
            foreach (var vehicle in Vehicles())
            {
                ApplySurfacesTo(circuit, vehicle);
            }
            RepaintPackedSnow();
        }
        catch (System.Exception error)
        {
            if (!_surfaceTickWarned)
            {
                _surfaceTickWarned = true;
                Plugin.Log.LogWarning("surfaces: " + error.Message + " (further errors hidden)");
            }
        }
    }

    // Which patches the car is on: its route progress plus how far it sits ahead of / beside that route point.
    static void ApplySurfacesTo(WaypointCircuit circuit, Vehicle vehicle)
    {
        if (vehicle.HasFinished() || vehicle.Car() == null)
        {
            return;
        }
        var controller = vehicle.Car().GetComponent<CarController>();
        if (controller == null)
        {
            return;
        }
        var point = circuit.GetRoutePoint(vehicle.Progress());
        var (along, lateral) = Pure.RouteFrame(vehicle.Car().transform.position, point.Position(), point.Direction());
        float distance = vehicle.Progress() + along;
        var body = vehicle.Car().GetComponent<Rigidbody>();
        float speed = body != null ? body.velocity.magnitude : 0f;
        var effect = SurfaceEffect.None;
        float linger = EffectLingerSeconds;
        foreach (var laid in _laid)
        {
            if (!Pure.Contains(laid.Patch, distance, lateral))
            {
                continue;
            }
            bool firstContact = Pure.NotePass(laid.Patch, Login(vehicle), SnowPackPerPass, SnowPackFloor);
            if (firstContact && laid.Patch.Kind == SurfaceKind.Puddle)
            {
                MaybeSpinOut(vehicle, body, speed);
            }
            effect = Pure.Combine(effect, EffectOf(laid.Patch, speed));
            if (laid.Patch.Kind == SurfaceKind.Puddle)
            {
                linger = Mathf.Max(linger, PuddleLingerSeconds);
            }
        }
        // snowing: the whole map is snow, so leaving the road is driving through deep, unpacked snow
        if (Weather.SnowOnGround && IsOffRoad(vehicle, vehicle.Car().transform))
        {
            effect = Pure.Combine(effect, Effect(SnowSpeed, SnowGrip, Pure.BogDrag(speed, 1f, MudDrag, MudBogDrag, BogSpeed)));
        }
        if (effect.IsNone)
        {
            return;   // the last effect runs out on its own (EffectLingerSeconds)
        }
        SurfaceEffects[controller] = (effect, Time.unscaledTime + linger);
        bool spinning = SpinningUntil.TryGetValue(vehicle, out var until) && Time.unscaledTime < until;
        if (effect.Grip < 1f && !spinning)
        {
            RefreshGrip(vehicle, effect.Grip, linger);
        }
    }

    // Hitting a puddle mid-bend at speed can spin the car: almost no grip for a second and the tail kicked out.
    static readonly Dictionary<Vehicle, float> SpinningUntil = new();
    static void MaybeSpinOut(Vehicle vehicle, Rigidbody body, float speed)
    {
        if (body == null || speed < PuddleSpinMinSpeed)
        {
            return;
        }
        float bend = RouteTurnAhead(vehicle.Progress(), PuddleSpinLook);
        if (Random.value > PuddleSpinChance * Mathf.Clamp01(Mathf.Abs(bend) / PuddleSpinDegrees))
        {
            return;
        }
        SpinningUntil[vehicle] = Time.unscaledTime + PuddleSpinSeconds;
        vehicle.GripFor(PuddleSpinGrip, PuddleSpinSeconds);
        body.angularVelocity += Vector3.up * Mathf.Sign(bend) * PuddleSpinYaw;
        Plugin.Emit("spinout", new { login = Login(vehicle), on = "puddle" });
    }

    static SurfaceEffect EffectOf(SurfacePatch patch, float speed)
    {
        float strength = patch.Strength;
        float bog = Pure.BogDrag(speed, strength, MudDrag, MudBogDrag, BogSpeed);
        switch (patch.Kind)
        {
            case SurfaceKind.Ice:
                return Effect(1f, Mathf.Lerp(1f, IceGrip, strength), 0f);
            case SurfaceKind.Puddle:
                return Effect(Mathf.Lerp(1f, PuddleSpeed, strength), Mathf.Lerp(1f, PuddleGrip, strength), 0f);
            case SurfaceKind.Mud:
                return Effect(Mathf.Lerp(1f, MudSpeed, strength), 1f, bog);
            default:
                return Effect(Mathf.Lerp(1f, SnowSpeed, strength), Mathf.Lerp(1f, SnowGrip, strength), bog);
        }
    }

    static SurfaceEffect Effect(float speed, float grip, float drag) => new SurfaceEffect
    {
        Speed = speed,
        Grip = grip,
        Drag = drag,
    };

    // The game's grip effect replaces itself on every call, so refresh it a little ahead of running out, or at
    // once when the multiplier changes (puddle -> ice). It restores the car's own friction when it ends.
    static void RefreshGrip(Vehicle vehicle, float grip, float linger)
    {
        bool known = GripRefreshed.TryGetValue(vehicle, out var last);
        if (known && Mathf.Approximately(last.grip, grip) && Time.unscaledTime - last.at < GripRefreshEvery)
        {
            return;
        }
        GripRefreshed[vehicle] = (grip, Time.unscaledTime);
        vehicle.GripFor(grip, Mathf.Max(GripHoldSeconds, linger));
    }

    // Called after the AI's own speed logic every frame (Patches.AfterAiSurface): top speed and drag.
    public static void ApplySurface(CarController controller)
    {
        if (controller == null || !SurfaceEffects.TryGetValue(controller, out var active))
        {
            return;
        }
        if (Time.unscaledTime >= active.until)
        {
            SurfaceEffects.Remove(controller);
            return;
        }
        controller.SetTopSpeedMultiplier(controller.TopSpeedMultiplier() * active.effect.Speed);
        if (active.effect.Drag <= 0f)
        {
            return;
        }
        var body = controller.GetComponent<Rigidbody>();
        if (body == null)
        {
            return;
        }
        var velocity = body.velocity;
        float keep = Mathf.Max(0f, 1f - active.effect.Drag * Time.deltaTime);
        body.velocity = new Vector3(velocity.x * keep, velocity.y, velocity.z * keep);   // ground speed only: gravity stays
    }

    // ---- visuals ----

    static LaidSurface DrawSurface(WaypointCircuit circuit, SurfacePatch patch)
    {
        var laid = new LaidSurface { Patch = patch };
        try
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new System.InvalidOperationException("no Sprites/Default or Unlit/Color shader");
            }
            laid.Material = new Material(shader) { color = SurfaceColor(patch) };
            if (patch.Kind == SurfaceKind.Ice)
            {
                laid.Material.mainTexture = IceTexture();
            }
            else if (patch.Kind == SurfaceKind.Snow && !patch.Oval)
            {
                laid.Material.mainTexture = SnowTexture();
            }
            laid.Mesh = patch.Oval ? OvalMesh(circuit, patch) : StripMesh(circuit, patch);
            laid.Visual = new GameObject("sr-surface-" + patch.Kind.ToString().ToLowerInvariant());
            laid.Visual.AddComponent<MeshFilter>().sharedMesh = laid.Mesh;
            var renderer = laid.Visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = laid.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        catch (System.Exception error)
        {
            if (!_surfaceDrawWarned)
            {
                _surfaceDrawWarned = true;
                Plugin.Log.LogWarning("surfaces: can't draw (" + error.Message + "); effects still apply");
            }
        }
        return laid;
    }

    static Color SurfaceColor(SurfacePatch patch) => patch.Kind switch
    {
        SurfaceKind.Ice => IceColor,
        SurfaceKind.Puddle => PuddleColor,
        SurfaceKind.Mud => MudColor,
        _ => Color.Lerp(SnowPackedColor, SnowColor, Mathf.InverseLerp(SnowPackFloor, 1f, patch.Strength)),
    };

    // Snow darkens as it packs: repaint the patches whose strength moved since they were last painted.
    static void RepaintPackedSnow()
    {
        foreach (var laid in _laid)
        {
            if (laid.Material == null || Mathf.Approximately(laid.PaintedStrength, laid.Patch.Strength))
            {
                continue;
            }
            laid.PaintedStrength = laid.Patch.Strength;
            laid.Material.color = SurfaceColor(laid.Patch);
        }
    }

    // Rows across the road every SurfaceMeshStep, edge to edge of the road's own width (RoadEdge), so the patch never
    // spills onto the grass: an opaque recolour of that stretch of road. UVs are in world units / TextureTile, so the
    // pixel textures keep their size however wide the road is.
    static Mesh StripMesh(WaypointCircuit circuit, SurfacePatch patch)
    {
        int rows = Mathf.Max(2, Mathf.CeilToInt((patch.End - patch.Start) / SurfaceMeshStep) + 1);
        var vertices = new Vector3[rows * 4];
        var colors = new Color[rows * 4];
        var uvs = new Vector2[rows * 4];
        var triangles = new int[(rows - 1) * 18];
        for (int row = 0; row < rows; row++)
        {
            float distance = Mathf.Lerp(patch.Start, patch.End, row / (float)(rows - 1));
            float left = -RoadEdge(circuit, distance, -1f), right = RoadEdge(circuit, distance, 1f);
            float[] laterals = { left, Mathf.Lerp(left, right, 1f / 3f), Mathf.Lerp(left, right, 2f / 3f), right };
            for (int column = 0; column < 4; column++)
            {
                int index = row * 4 + column;
                vertices[index] = RoadPoint(circuit, distance, laterals[column]);
                colors[index] = Color.white;
                uvs[index] = new Vector2(laterals[column] / TextureTile, (distance - patch.Start) / TextureTile);
            }
        }
        for (int row = 0; row + 1 < rows; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int corner = row * 4 + column, at = (row * 3 + column) * 6;
                triangles[at] = corner;
                triangles[at + 1] = corner + 4;
                triangles[at + 2] = corner + 1;
                triangles[at + 3] = corner + 1;
                triangles[at + 4] = corner + 4;
                triangles[at + 5] = corner + 5;
            }
        }
        var mesh = BuildMesh(vertices, triangles);
        mesh.colors = colors;
        mesh.uv = uvs;
        return mesh;
    }

    // How far the road reaches to one side of the route line (side = -1 left, +1 right): step outward while the top
    // surface is the same collider as the centre's and stays level; a kerb, barrier, drop or grass ends it.
    static float RoadEdge(WaypointCircuit circuit, float distance, float side)
    {
        var centre = TopHit(circuit, distance, 0f);
        if (centre.collider == null)
        {
            return RoadHalfWidth;
        }
        float reached = 0f;
        for (float lateral = EdgeProbeStep; lateral <= EdgeProbeMax; lateral += EdgeProbeStep)
        {
            var hit = TopHit(circuit, distance, lateral * side);
            if (hit.collider != centre.collider || Mathf.Abs(hit.point.y - centre.point.y) > EdgeHeightJump)
            {
                break;
            }
            reached = lateral;
        }
        return reached > 0f ? reached : RoadHalfWidth;
    }

    // The highest static surface under (distance, lateral): the same rule RoadPoint uses.
    static RaycastHit TopHit(WaypointCircuit circuit, float distance, float lateral)
    {
        var point = circuit.GetRoutePoint(distance);
        var right = Vector3.Cross(Vector3.up, FlatDirection(point.Direction()));
        var from = point.Position() + right * lateral + Vector3.up * GroundProbe;
        RaycastHit best = default;
        float highest = float.MinValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, GroundProbe * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == null && hit.point.y > highest)
            {
                highest = hit.point.y;
                best = hit;
            }
        }
        return best;
    }

    const float TextureTile = 2f;   // world units per texture repeat (16 px): chunky, Minecraft-sized pixels

    // Minecraft-style ice: light blue with short pale diagonal streaks, opaque, pixel-sharp.
    static Texture2D _iceTexture;
    static Texture2D IceTexture()
    {
        if (_iceTexture != null)
        {
            return _iceTexture;
        }
        var baseColor = new Color(0.58f, 0.78f, 0.98f);
        var streak = new Color(0.82f, 0.92f, 1f);
        var deep = new Color(0.5f, 0.7f, 0.94f);
        _iceTexture = PixelTexture((x, y) =>
        {
            int diagonal = (x + y) % 16;
            bool line = (diagonal == 3 && x % 8 < 5) || (diagonal == 11 && (x + 3) % 8 < 4);
            bool speck = (x * 7 + y * 13) % 29 == 0;
            return line ? streak : speck ? deep : baseColor;
        });
        return _iceTexture;
    }

    // Snowed-over road: white with a few faint grey flecks.
    static Texture2D _snowTexture;
    static Texture2D SnowTexture()
    {
        if (_snowTexture != null)
        {
            return _snowTexture;
        }
        var fleck = new Color(0.88f, 0.9f, 0.94f);
        var white = new Color(0.97f, 0.98f, 1f);
        _snowTexture = PixelTexture((x, y) => (x * 5 + y * 11) % 23 == 0 ? fleck : white);
        return _snowTexture;
    }

    static Texture2D PixelTexture(System.Func<int, int, Color> pixel)
    {
        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
        };
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                texture.SetPixel(x, y, pixel(x, y));
            }
        }
        texture.Apply();
        return texture;
    }

    // A fan: the centre plus a ring, each ring point mapped through the route so the oval follows the curve.
    static Mesh OvalMesh(WaypointCircuit circuit, SurfacePatch patch)
    {
        float radiusAlong = (patch.End - patch.Start) * 0.5f;
        var vertices = new Vector3[PuddleSegments + 1];
        var triangles = new int[PuddleSegments * 3];
        vertices[0] = RoadPoint(circuit, patch.Centre, patch.Lateral);
        for (int segment = 0; segment < PuddleSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / PuddleSegments;
            float ragged = patch.Kind == SurfaceKind.Puddle
                ? 1f
                : 1f - BlobRaggedness + BlobRaggedness * 2f * Mathf.PerlinNoise(patch.Centre * 0.37f, segment * 0.45f);
            float distance = patch.Centre + Mathf.Cos(angle) * radiusAlong * ragged;
            vertices[segment + 1] = RoadPoint(circuit, distance, patch.Lateral + Mathf.Sin(angle) * patch.HalfWidth * ragged);
            triangles[segment * 3] = 0;
            triangles[segment * 3 + 1] = segment + 1;
            triangles[segment * 3 + 2] = (segment + 1) % PuddleSegments + 1;
        }
        return BuildMesh(vertices, triangles);
    }

    static Mesh BuildMesh(Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = "sr-surface" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The road surface at (route distance, lateral offset): the route line moved sideways, then the HIGHEST static
    // collider within GroundProbe of it (the road deck sits on top; the route line can run below the deck, and
    // "nearest" then picked the terrain under a raised road). Cars have rigidbodies and are skipped.
    static Vector3 RoadPoint(WaypointCircuit circuit, float distance, float lateral)
    {
        var point = circuit.GetRoutePoint(distance);
        var right = Vector3.Cross(Vector3.up, FlatDirection(point.Direction()));
        var around = point.Position() + right * lateral;
        var from = around + Vector3.up * GroundProbe;
        var hits = Physics.RaycastAll(from, Vector3.down, GroundProbe * 2f, ~0, QueryTriggerInteraction.Ignore);
        var ground = around;
        float highest = float.MinValue;
        foreach (var hit in hits)
        {
            if (hit.collider.attachedRigidbody != null || hit.point.y <= highest)
            {
                continue;
            }
            highest = hit.point.y;
            ground = hit.point;
        }
        return ground + Vector3.up * SurfaceLift;
    }
}
