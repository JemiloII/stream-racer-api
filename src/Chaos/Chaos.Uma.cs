// Chaos.Uma: the Uma race. On some races (settings.chaos.umaRaceChance at the start, or POST /chaos/uma) every car
// becomes a random horse girl for the rest of the race and the game's music gives way to an Umamusume race track.
// The models come from scripts/uma_export.py, which reads the Umamusume game installed on this PC and writes
// BepInEx/plugins/StreamRacerApi.uma/<chara id>/ (model.json = skeleton + meshes, model.bin = vertex data, PNG
// textures) and the music (race_*.wav: the game's race tracks; theme.wav: its title theme, the fallback).
// Nothing of Cygames' ships with the mod: no folder = no Uma race (one warning).
// Each uma is a costume ("uma:<folder>", e.g. uma:1001 or uma:1001_gym; Chaos.Costume hides the car) running with
// the game's own race motion (Chaos.UmaMotion), or a procedural gallop when the motions aren't exported.
// POST /chaos/umagym = the same race with everyone in the gym uniform (the <id>_gym exports).
using System.IO;
using Newtonsoft.Json.Linq;
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const float UmaHeight = 2.6f;             // m: a 160 cm horse girl stands this tall next to a ~4.5 m car
    const float UmaPaceEvery = 0.5f;          // s between strategy pace updates
    const float UmaStride = 3.2f;             // m covered per gallop cycle at her size
    const float UmaMinCycles = 0.9f, UmaMaxCycles = 3.4f;   // gallop cycles a second (standing still -> top speed)
    const float UmaElbowBend = 75f, UmaBob = 0.05f;
    const float UmaArmsDown = 72f;             // her rest pose is a T-pose: arms come down this far to run
    const float UmaMusicVolume = 0.6f;
    const float DanceSide = 9f;               // m from the road's centre line, beside the track
    const float DanceBefore = 10f;            // m before the finish line where the first finisher stands
    const float DanceSpacing = 2.2f;          // m between two dancers in the line (at her size), back up the road
    const float DanceBeat = 2.1f;             // beats a second: everyone on the same beat
    const float UmaRaceSeconds = 100000f;     // a costume for the whole race

    static string UmaFolder => Path.Combine(BepInEx.Paths.PluginPath, "StreamRacerApi.uma");
    static readonly Dictionary<string, UmaTemplate> _umaTemplates = new();
    static bool _umaWarned, _umaRolled;
    public static string UmaOnly;   // dev aid (/chaos/uma?only=1001): every car this girl

    // One exported uma, built once (hidden) and copied for every car that becomes her.
    class UmaTemplate
    {
        public string Key;
        public string Name;
        public GameObject Model;
        public float HeightCm;
        public int RunningType;               // chara_data.race_running_type: her running form (1-3)
        public bool Real;                     // in the game's own materials (Chaos.UmaLook), not the baked look
        public string GymSkin;                // gym uniform: her skin texture stem (tex_bdy0001_00_00_<skin>_<bust>_0<socks>)
        public int Style;                     // 1 front runner, 2 pace chaser, 3 late surger, 4 end closer
    }

    // A running form's gallop: thigh swing, knee fold, arm swing, forward lean (degrees).
    class GallopForm
    {
        public float Thigh;
        public float Knee;
        public float Arms;
        public float Lean;
    }

    static readonly Dictionary<int, GallopForm> GallopForms = new()
    {
        [1] = new GallopForm
        {
            Thigh = 38f,
            Knee = 75f,
            Arms = 34f,
            Lean = 12f,
        },
        [2] = new GallopForm      // low and driving
        {
            Thigh = 42f,
            Knee = 85f,
            Arms = 40f,
            Lean = 18f,
        },
        [3] = new GallopForm      // tall and loose, big arm swing
        {
            Thigh = 36f,
            Knee = 70f,
            Arms = 50f,
            Lean = 7f,
        },
    };

    // Top-speed multiplier by how far through the race she is, per strategy: front runners go early and fade, pace
    // chasers sit just behind and hold, late surgers save it for the second half, end closers wait and then fly.
    static float StylePace(int style, float done) => style switch
    {
        1 => done < 0.4f ? 1.10f : 0.97f,
        2 => done < 0.6f ? 1.05f : 1.0f,
        3 => done < 0.5f ? 0.97f : 1.07f,
        4 => done < 0.65f ? 0.95f : 1.14f,
        _ => 1f,
    };

    // The exports on this PC for an outfit: "<id>" folders (their racewear) or "<id>_gym" folders (gym uniform).
    static List<string> UmaKeys(bool gym = false)
    {
        if (!Directory.Exists(UmaFolder))
        {
            return new List<string>();
        }
        return Directory.GetDirectories(UmaFolder)
            .Where(folder => File.Exists(Path.Combine(folder, "model.json")))
            .Select(Path.GetFileName)
            .Where(key => key.EndsWith("_gym") == gym)
            .ToList();
    }

    // ---- the race ----

    static IEnumerator UmaRace() => UmaRaceIn(false);
    static IEnumerator UmaGymRace() => UmaRaceIn(true);

    static IEnumerator UmaRaceIn(bool gym)
    {
        var ids = UmaKeys(gym);
        string only = UmaOnly == null ? null : UmaOnly + (gym ? "_gym" : "");
        if (only != null && ids.Contains(only))
        {
            ids = new List<string> { only };
        }
        if (ids.Count == 0)
        {
            WarnUma("no exported umas in " + UmaFolder + " (run scripts/uma_export.py" + (gym ? " gym" : "") + ")");
            yield break;
        }
        Announce(gym ? "UMA RACE · GYM CLOTHES" : "UMA RACE", "UMA");
        _umaDeck = ids.OrderBy(_ => Random.value).ToList();
        _umaDealt = 0;
        foreach (var vehicle in Racers())
        {
            DressAsUma(vehicle);
            yield return null;   // one build a frame: no hitch on a full grid
        }
        Spawn(PlayUmaMusic());
        _umaMuddy = false;
        while (Game.Running)
        {
            Guard("uma pace", PaceUmas);
            Guard("uma rain", MuddyUmasWhenRaining);
            Guard("uma light", BalanceUmaLight);
            yield return new WaitForSeconds(UmaPaceEvery);
        }
    }

    // Every horse girl runs her strategy: a top-speed multiplier for the next stretch (Game.Speed, refreshed).
    static List<string> _umaDeck = new();
    static int _umaDealt;

    // Her own pick (!race uma) when that girl is exported in this outfit, else the next girl off the shuffled deck
    // (the deck repeats once every car has one).
    static void DressAsUma(Vehicle vehicle)
    {
        if (_umaDeck.Count == 0)
        {
            return;
        }
        string gym = _umaDeck[0].EndsWith("_gym") ? "_gym" : "";
        string picked = Settings.Current.umas.TryGetValue(Game.Login(vehicle) ?? "", out var id) ? id + gym : null;
        if (picked != null && File.Exists(Path.Combine(UmaFolder, picked, "model.json")))
        {
            Dress(vehicle, "uma:" + picked, UmaRaceSeconds);
            return;
        }
        Dress(vehicle, "uma:" + _umaDeck[_umaDealt % _umaDeck.Count], UmaRaceSeconds);
        _umaDealt++;
    }

    // ---- !race uma <name> ----

    static Dictionary<string, string> _umaNames;   // chara id -> name, read once from the exports

    // The names of the exported girls: each racewear model.json starts with {"id": .., "name": ..}, so only its head is read.
    static Dictionary<string, string> UmaNames()
    {
        if (_umaNames != null && _umaNames.Count > 0)
        {
            return _umaNames;
        }
        _umaNames = new Dictionary<string, string>();
        foreach (var key in UmaKeys())
        {
            using var reader = new StreamReader(Path.Combine(UmaFolder, key, "model.json"));
            var head = new char[512];
            int read = reader.Read(head, 0, head.Length);
            var name = System.Text.RegularExpressions.Regex.Match(new string(head, 0, read), @"""name""\s*:\s*""([^""]+)""");
            if (name.Success)
            {
                _umaNames[key] = name.Groups[1].Value;
            }
        }
        return _umaNames;
    }

    // A viewer picks the girl their car becomes in Uma races; saved like their colour (settings.umas).
    public static void PickUma(string login, string displayName, string query) => Guard("uma pick", () =>
    {
        var names = UmaNames();
        if (query.Length == 0)
        {
            string current = Settings.Current.umas.TryGetValue(login, out var mine) && names.TryGetValue(mine, out var name)
                ? "you're " + name + ". "
                : "";
            Game.ReplyInChat("@" + displayName + " " + current + "Pick your Umamusume: !race uma <name>, e.g. !race uma Gold Ship");
            return;
        }
        string id = Pure.MatchUma(query, names, out var close);
        if (id == null)
        {
            Game.ReplyInChat("@" + displayName + (close.Count > 0
                ? " which one? " + string.Join(", ", close)
                : " no Umamusume called \"" + query + "\" here"));
            return;
        }
        Settings.Current.umas[login] = id;
        Settings.Persist();
        Plugin.Emit("uma", new
        {
            login,
            displayName,
            uma = id,
            name = names[id],
        });
        Game.ReplyInChat("@" + displayName + " you'll race as " + names[id] + " in Uma races");
    });

    // A car that wasn't on the road when the race turned Uma (spawned late) gets her girl now.
    static void DressLateCars()
    {
        foreach (var vehicle in Racers().Where(vehicle => !_costumes.ContainsKey(vehicle)))
        {
            DressAsUma(vehicle);
        }
    }

    static void PaceUmas()
    {
        DressLateCars();
        foreach (var costume in _costumes.Values.ToList())
        {
            if (!costume.Kind.StartsWith("uma:"))
            {
                continue;
            }
            var template = UmaTemplateFor(costume.Kind.Substring(4));
            var vehicle = costume.Vehicle;
            if (template == null || costume.Dancing)
            {
                continue;
            }
            if (vehicle.HasFinished())
            {
                // only a car that really crossed the line (the game's finisher list): the game also marks the rest
                // finished when it wraps the race up, and they must not all walk off to dance
                if (Game.FinishOrder.Contains(Game.Login(vehicle)))
                {
                    StartDance(costume);
                }
                continue;
            }
            if (Game.Login(vehicle) == Game.StreamerLogin)
            {
                continue;   // the streamer's car keeps its own pace: a strategy surge looked like a boost they never used
            }
            float done = Mathf.Clamp01(vehicle.Progress() / Mathf.Max(1f, Game.FinishDistance(vehicle)));
            Game.Speed(vehicle, StylePace(template.Style, done), UmaPaceEvery * 2f);
        }
    }

    // ---- the finish ----

    // She crossed the line: step off the car to her spot in the line beside the finish (by place) and dance with
    // everyone who finished before her. The car stays hidden where it stopped.
    static void StartDance(Costume costume)
    {
        if (costume.Outfit == null || !Game.FinishLine(out var line, out var along, out _))
        {
            return;
        }
        var uma = costume.Outfit.Find("model/uma");
        if (uma == null)
        {
            return;
        }
        int place = Mathf.Max(0, Game.FinishOrder.ToList().IndexOf(Game.Login(costume.Vehicle)));
        along.y = 0f;
        along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward;
        var side = Vector3.Cross(Vector3.up, along);
        float scale = uma.lossyScale.y;
        var spot = Ground(line + side * DanceSide - along * (DanceBefore + place * DanceSpacing * scale / 1.6f));
        StandingPose(uma, costume.Kind.Substring(4));
        MoveCarTo(costume, spot, Quaternion.LookRotation(-side));   // facing the road and the cars still coming
        costume.Dancing = true;
        costume.Until = float.MaxValue;
        var springs = Springs(uma);
        var dance = Dance(uma);
        var face = FaceFeeder(uma.gameObject);
        costume.Animate = (seconds, speed) =>
        {
            dance(seconds, speed);
            StepSprings(springs, 0f);
            face();
        };
    }

    // The (hidden) car goes with her, held still, so the game's follow camera on her shows her dancing; she stays on
    // it. Without a car (gone already) she is moved on her own.
    static void MoveCarTo(Costume costume, Vector3 spot, Quaternion facing)
    {
        var car = costume.Vehicle.Car();
        if (car == null || costume.Outfit.parent != car.transform)
        {
            costume.Outfit.SetParent(Root.transform, true);
            costume.Outfit.SetPositionAndRotation(spot, facing);
            return;
        }
        var body = car.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
        var outfitFromCar = Quaternion.Inverse(car.transform.rotation) * costume.Outfit.rotation;
        car.transform.rotation = facing * Quaternion.Inverse(outfitFromCar);
        car.transform.position += spot - costume.Outfit.position;   // her feet (the outfit) land on the spot
    }

    // Every bone back to how her model stands (the template's pose), so she doesn't dance frozen mid-stride.
    static void StandingPose(Transform uma, string key)
    {
        var template = UmaTemplateFor(key);
        if (template == null)
        {
            return;
        }
        var standing = template.Model.GetComponentsInChildren<Transform>(true)
            .GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var bone in uma.GetComponentsInChildren<Transform>(true))
        {
            if (bone == uma || !standing.TryGetValue(bone.name, out var rest))
            {
                continue;
            }
            bone.localPosition = rest.localPosition;
            bone.localRotation = rest.localRotation;
            bone.localScale = rest.localScale;
        }
    }

    // Everyone on the same beat (the clock, not her own timer): step side to side with a little hop, sway, arms
    // pumping up over her head and back down on alternate beats.
    static System.Action<float, float> Dance(Transform uma)
    {
        var all = uma.GetComponentsInChildren<Transform>(true);
        Transform Find(string name) => all.FirstOrDefault(bone => bone.name == name);
        var parts = new List<(Transform bone, Quaternion rest, System.Func<float, float> side,
            System.Func<float, float> forward)>();
        void Add(string name, System.Func<float, float> side, System.Func<float, float> forward)
        {
            var bone = Find(name);
            if (bone != null)
            {
                parts.Add((bone, bone.localRotation, side, forward));
            }
        }
        float Up(float beat) => Mathf.Abs(Mathf.Sin(beat * Mathf.PI));
        Add("Spine", _ => 0f, beat => 8f * Mathf.Sin(beat * Mathf.PI));
        Add("Arm_L", beat => -20f * Up(beat), beat => Mathf.Lerp(UmaArmsDown, -60f, Up(beat * 0.5f)));
        Add("Arm_R", beat => -20f * Up(beat), beat => -Mathf.Lerp(UmaArmsDown, -60f, Up(beat * 0.5f + 0.5f)));
        Add("Elbow_L", _ => -30f, _ => 0f);
        Add("Elbow_R", _ => -30f, _ => 0f);
        Add("Knee_L", beat => 25f * Up(beat), _ => 0f);
        Add("Knee_R", beat => 25f * Up(beat + 0.5f), _ => 0f);
        var hip = Find("Hip");
        var hipRest = hip != null ? hip.localPosition : Vector3.zero;
        return (seconds, speed) =>
        {
            if (uma == null)
            {
                return;
            }
            float beat = Time.time * DanceBeat;
            var right = uma.right;
            var forward = uma.forward;
            foreach (var (bone, rest, side, front) in parts)
            {
                bone.localRotation = rest;
                bone.rotation = Quaternion.AngleAxis(side(beat), right) * Quaternion.AngleAxis(front(beat), forward)
                    * bone.rotation;
            }
            if (hip != null)
            {
                hip.localPosition = hipRest + Vector3.up * (0.06f * Up(beat))
                    + Vector3.right * (0.08f * Mathf.Sin(beat * Mathf.PI * 0.5f));
            }
        };
    }

    // First chaos tick of a race: maybe this one is an Uma race.
    static void MaybeUmaRace()
    {
        if (_umaRolled)
        {
            return;
        }
        _umaRolled = true;
        var options = Settings.Current.chaos;
        if (!options.enabled || Random.value >= options.umaRaceChance)
        {
            return;
        }
        var kinds = new List<string>();
        if (options.On("uma") && UmaKeys().Count > 0)
        {
            kinds.Add("uma");
        }
        if (options.On("umagym") && UmaKeys(true).Count > 0)
        {
            kinds.Add("umagym");
        }
        if (kinds.Count > 0)
        {
            Start(kinds[Random.Range(0, kinds.Count)]);
        }
    }

    // ---- the costume ----

    // Costume builder for "uma:<folder>": her model under `model`, facing +z, standing on the ground; returns her run.
    static System.Action<float, float> BuildUma(Transform model, string key)
    {
        var template = UmaTemplateFor(key);
        if (template == null)
        {
            throw new System.InvalidOperationException("uma " + key + " could not be loaded");
        }
        var uma = Object.Instantiate(template.Model, model, false);
        uma.name = "uma";
        uma.SetActive(true);
        NeutralAmbient(uma);
        OnUmaLayer(uma);
        System.Action face = null;
        if (template.Real)
        {
            face = RealLookFor(uma, template.GymSkin);
        }
        else if (key.EndsWith("_gym"))
        {
            PaintGymColours(uma);
        }
        if (_umaMuddy)
        {
            Muddy(uma);   // dressed after the rain started (a respawned car): muddy from the start
        }
        uma.transform.localPosition = Vector3.zero;
        uma.transform.localRotation = Quaternion.identity;
        float height = UmaHeight * (template.HeightCm > 0f ? template.HeightCm / 160f : 1f);
        uma.transform.localScale = Vector3.one * (height / 1.6f / Mathf.Max(0.01f, model.lossyScale.y));
        var run = PlayClip(uma.transform, ClipFor(template.RunningType, "run"));
        if (run == null)
        {
            var form = GallopForms.TryGetValue(template.RunningType, out var known) ? known : GallopForms[1];
            run = Gallop(uma.transform, form);
        }
        if (face == null)
        {
            return run;
        }
        return (seconds, speed) =>
        {
            run(seconds, speed);
            face();
        };
    }

    static UmaTemplate UmaTemplateFor(string key)
    {
        if (_umaTemplates.TryGetValue(key, out var cached) && cached.Model != null)
        {
            return cached;
        }
        var template = LoadUma(key);
        if (template != null)
        {
            _umaTemplates[key] = template;
        }
        return template;
    }

    // ---- loading an export ----

    static UmaTemplate LoadUma(string key)
    {
        string folder = Path.Combine(UmaFolder, key);
        if (!File.Exists(Path.Combine(folder, "model.json")))
        {
            return null;
        }
        var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "model.json")));
        var blob = File.ReadAllBytes(Path.Combine(folder, "model.bin"));
        var root = new GameObject("sr-uma-" + key);
        root.SetActive(false);
        Object.DontDestroyOnLoad(root);   // templates outlive races; copies live on the cars
        var bones = BuildBones(root.transform, (JArray)info["bones"]);
        var textures = new Dictionary<string, Texture2D>();
        bool real = LoadRealLook(info, key.EndsWith("_gym"));
        foreach (JObject mesh in (JArray)info["meshes"])
        {
            BuildUmaMesh(mesh, blob, bones, folder, textures, real);
        }
        return new UmaTemplate
        {
            Key = key,
            Name = (string)info["name"],
            Model = root,
            HeightCm = (float?)info["heightCm"] ?? 160f,
            RunningType = (int?)info["runningType"] ?? 1,
            Style = (int?)info["style"] ?? 2,
            Real = real,
            GymSkin = key.EndsWith("_gym") ? GymSkinOf(info) : null,
        };
    }

    // The body mesh's skin slot (1) names her skin texture: its stem without _diff.
    static string GymSkinOf(JObject info)
    {
        var body = ((JArray)info["meshes"]).FirstOrDefault() as JObject;
        var submeshes = body?["submeshes"] as JArray;
        string texture = submeshes != null && submeshes.Count > 1 ? (string)submeshes[1]["texture"] : null;
        return texture != null && texture.EndsWith("_diff") ? texture.Substring(0, texture.Length - "_diff".Length) : null;
    }

    static Transform[] BuildBones(Transform root, JArray list)
    {
        var bones = new Transform[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var bone = (JObject)list[i];
            bones[i] = new GameObject((string)bone["name"]).transform;
        }
        for (int i = 0; i < list.Count; i++)
        {
            var bone = (JObject)list[i];
            int parent = (int)bone["parent"];
            bones[i].SetParent(parent >= 0 ? bones[parent] : root, false);
            bones[i].localPosition = ReadVector((JArray)bone["p"]);
            var rotation = (JArray)bone["r"];
            bones[i].localRotation = new Quaternion((float)rotation[0], (float)rotation[1], (float)rotation[2],
                (float)rotation[3]);
            bones[i].localScale = ReadVector((JArray)bone["s"]);
        }
        return bones;
    }

    static Vector3 ReadVector(JArray values) => new((float)values[0], (float)values[1], (float)values[2]);

    static void BuildUmaMesh(JObject info, byte[] blob, Transform[] bones, string folder,
        Dictionary<string, Texture2D> textures, bool real)
    {
        int count = (int)info["vertexCount"];
        var mesh = new Mesh
        {
            name = (string)info["name"],
            indexFormat = count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16,
        };
        mesh.vertices = Vectors3(blob, (int)info["positions"], count);
        if ((int)info["normals"] >= 0)
        {
            mesh.normals = Vectors3(blob, (int)info["normals"], count);
        }
        if ((int)info["uvs"] >= 0)
        {
            mesh.uv = Vectors2(blob, (int)info["uvs"], count);
        }
        var submeshes = (JArray)info["submeshes"];
        mesh.subMeshCount = submeshes.Count;
        var materials = new Material[submeshes.Count];
        for (int i = 0; i < submeshes.Count; i++)
        {
            var submesh = (JObject)submeshes[i];
            mesh.SetTriangles(Ints(blob, (int)submesh["indices"], (int)submesh["indexCount"]), i);
            var own = real ? RealMaterial((string)submesh["material"]) : null;
            if (own != null && own.name.StartsWith("mtl_tail"))
            {
                own = TailMaterial(own, (string)submesh["texture"]);
            }
            materials[i] = own != null ? own
                : (string)info["blend"] == "multiply"
                    ? MultiplyMaterial(UmaTexture(folder, (string)submesh["texture"], textures))
                    : UmaMaterial(folder, (string)submesh["texture"], textures);
        }
        bool face = IsFacePart(mesh.name) && !real;   // the real face shader lights the face itself
        if (face && mesh.vertexCount > 0)
        {
            FaceLighting(mesh);
        }
        var boneList = ((JArray)info["bones"]).Select(token => bones[(int)token]).ToArray();
        GameObject holder;
        if (boneList.Length > 0 && info["boneWeights"] != null)
        {
            mesh.boneWeights = Weights(blob, (int)info["boneIndices"], (int)info["boneWeights"], count);
            mesh.bindposes = ((JArray)info["bindposes"]).Select(pose => ReadMatrix((JArray)pose)).ToArray();
            holder = new GameObject(mesh.name);
            holder.transform.SetParent(bones[0].root, false);
            var skinned = holder.AddComponent<SkinnedMeshRenderer>();
            skinned.bones = boneList;
            skinned.rootBone = boneList[0];
            skinned.sharedMesh = mesh;
            skinned.sharedMaterials = materials;
            skinned.updateWhenOffscreen = true;
            if (!real)
            {
                AddOutline(skinned, mesh);   // the real toon shaders draw their own outline
            }
        }
        else
        {
            holder = new GameObject(mesh.name);
            holder.transform.SetParent(bones[(int)info["attach"]], false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }
        mesh.RecalculateBounds();
        if (face)
        {
            holder.GetComponent<Renderer>().receiveShadows = false;
        }
    }

    // Her face, brows, face lines and blush: lit like the game's face shader (even, no cast shadows from hair or nose).
    static bool IsFacePart(string mesh) => mesh.StartsWith("M_Face") || mesh.StartsWith("M_Mayu")
        || mesh.StartsWith("M_Line") || mesh.StartsWith("M_Cheek");

    // Every normal turned mostly toward where her face looks (+z, a little up): the whole face takes the light the
    // same way, like an anime face, instead of a nose and brow shadow.
    static void FaceLighting(Mesh mesh)
    {
        var front = new Vector3(0f, 0.25f, 1f).normalized;
        var normals = mesh.normals.Length == mesh.vertexCount ? mesh.normals : new Vector3[mesh.vertexCount];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Slerp(normals[i].sqrMagnitude > 0f ? normals[i] : front, front, 0.85f);
        }
        mesh.normals = normals;
    }

    // ---- the binary ----

    static Vector3[] Vectors3(byte[] blob, int offset, int count)
    {
        var values = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int at = offset + i * 12;
            values[i] = new Vector3(System.BitConverter.ToSingle(blob, at), System.BitConverter.ToSingle(blob, at + 4),
                System.BitConverter.ToSingle(blob, at + 8));
        }
        return values;
    }

    static Vector2[] Vectors2(byte[] blob, int offset, int count)
    {
        var values = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int at = offset + i * 8;
            values[i] = new Vector2(System.BitConverter.ToSingle(blob, at), System.BitConverter.ToSingle(blob, at + 4));
        }
        return values;
    }

    static int[] Ints(byte[] blob, int offset, int count)
    {
        var values = new int[count];
        System.Buffer.BlockCopy(blob, offset, values, 0, count * 4);
        return values;
    }

    static BoneWeight[] Weights(byte[] blob, int indexOffset, int weightOffset, int count)
    {
        var weights = new BoneWeight[count];
        for (int i = 0; i < count; i++)
        {
            int index = indexOffset + i * 16, weight = weightOffset + i * 16;
            weights[i] = new BoneWeight
            {
                boneIndex0 = System.BitConverter.ToInt32(blob, index),
                boneIndex1 = System.BitConverter.ToInt32(blob, index + 4),
                boneIndex2 = System.BitConverter.ToInt32(blob, index + 8),
                boneIndex3 = System.BitConverter.ToInt32(blob, index + 12),
                weight0 = System.BitConverter.ToSingle(blob, weight),
                weight1 = System.BitConverter.ToSingle(blob, weight + 4),
                weight2 = System.BitConverter.ToSingle(blob, weight + 8),
                weight3 = System.BitConverter.ToSingle(blob, weight + 12),
            };
        }
        return weights;
    }

    // 16 floats, row by row (e00 e01 e02 e03, e10 ...).
    static Matrix4x4 ReadMatrix(JArray values)
    {
        var matrix = new Matrix4x4();
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                matrix[row, column] = (float)values[row * 4 + column];
            }
        }
        return matrix;
    }

    // ---- look ----

    // Her look with only the game's own shaders (a player can't compile new ones): her colour texture at full albedo,
    // matte, lit by the scene's sun with real shadows. The emission / cutout settings are asked for too, but this build
    // stripped those Standard variants (tested: she came out dim grey with albedo turned down), so albedo does the work.
    // UmaLook "vertexlit" = the legacy VertexLit shader instead (texture x lights + emission), to compare.
    public static string UmaLook = "toon";
    const float UmaAlbedo = 1f, UmaGlow = 0.35f;      // how much she is lit by the scene vs. glows her own colour
    const float OutlineWidth = 0.0035f;                 // m (at her real size) the dark shell sits outside her
    static readonly Color OutlineColor = new(0.12f, 0.08f, 0.1f);
    static Material _outline;

    static Material UmaMaterial(string folder, string texture, Dictionary<string, Texture2D> textures)
    {
        var colour = UmaTexture(folder, texture, textures);
        if (UmaLook == "vertexlit" && Shader.Find("Legacy Shaders/VertexLit") is Shader legacy)
        {
            var lit = new Material(legacy)
            {
                mainTexture = colour,
                color = Color.white * 0.75f,
            };
            lit.SetColor("_Emission", Color.white * 0.55f);
            return lit;
        }
        var material = new Material(Shader.Find("Standard"))
        {
            mainTexture = colour,
            color = Color.white * UmaAlbedo,
        };
        material.SetFloat("_Glossiness", 0f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Mode", 1f);                       // cutout
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        material.EnableKeyword("_EMISSION");
        material.SetTexture("_EmissionMap", colour);
        material.SetColor("_EmissionColor", Color.white * UmaGlow);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return material;
    }

    // The blush: multiplied onto her face like the game's MultiplyCheek (white = no change), never a solid patch.
    static Material MultiplyMaterial(Texture2D texture)
    {
        var shader = Shader.Find("Legacy Shaders/Particles/Multiply");
        if (shader == null)   // no multiply shader in this scene: leave the blush out rather than paint a patch
        {
            return new Material(Shader.Find("Sprites/Default"))
            {
                color = Color.clear,
            };
        }
        return new Material(shader)
        {
            mainTexture = texture,
        };
    }

    static Texture2D UmaTexture(string folder, string texture, Dictionary<string, Texture2D> textures)
    {
        if (texture == null)
        {
            return null;
        }
        if (!textures.TryGetValue(texture, out var loaded))
        {
            loaded = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            string file = GymTextureName(texture, texture.StartsWith("gym:zekken") ? WhiteBib : 0);
            loaded.name = file;
            string path = file != texture
                ? Path.Combine(UmaFolder, "gym", file + ".png")
                : Path.Combine(folder, texture + ".png");
            _texturePaths[loaded] = path;
            if (File.Exists(path))
            {
                loaded.LoadImage(File.ReadAllBytes(path));
            }
            textures[texture] = loaded;
        }
        return loaded;
    }

    // "gym:waku" / "gym:zekken_<bust>" (the gym uniform's clothes and number bib) -> that texture in colour `colour`.
    static string GymTextureName(string texture, int colour)
    {
        if (texture == "gym:waku")
        {
            return "tex_bdy0001_00_00_waku" + colour + "_diff";
        }
        if (texture.StartsWith("gym:zekken_"))
        {
            return "tex_bdy0001_00_zekken" + colour + "_" + texture.Substring("gym:zekken_".Length) + "_diff";
        }
        return texture;
    }

    static readonly Dictionary<string, Material> _gymColours = new();

    // The number bib is the shirt's own front and back panel (the shirt has holes there), so it stays, in white and
    // without its number, reading as part of the gym shirt.
    const int WhiteBib = 4;

    // Like the game: every girl in gym clothes gets a random gate-frame colour (8) and number-bib colour (5).
    static void PaintGymColours(GameObject uma)
    {
        int frame = Random.Range(0, 8), bib = WhiteBib;
        foreach (var renderer in uma.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                string name = materials[i] != null && materials[i].mainTexture != null ? materials[i].mainTexture.name : "";
                string wanted = name.Contains("_waku") ? "tex_bdy0001_00_00_waku" + frame + "_diff"
                    : name.Contains("_zekken")
                        ? "tex_bdy0001_00_zekken" + bib + name.Substring(name.IndexOf('_', name.IndexOf("zekken")))
                    : null;
                if (wanted == null || wanted == name)
                {
                    continue;
                }
                materials[i] = GymMaterial(materials[i], wanted);
                changed = true;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    static Material GymMaterial(Material like, string texture)
    {
        if (_gymColours.TryGetValue(texture, out var cached) && cached != null)
        {
            return cached;
        }
        var colour = new Texture2D(2, 2, TextureFormat.RGBA32, true)
        {
            name = texture,
        };
        string path = Path.Combine(UmaFolder, "gym", texture + ".png");
        _texturePaths[colour] = path;
        if (File.Exists(path))
        {
            colour.LoadImage(File.ReadAllBytes(path));
        }
        var material = new Material(like)
        {
            mainTexture = colour,
        };
        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", colour);
        }
        _gymColours[texture] = material;
        return material;
    }

    // ---- rain ----

    // Where every loaded texture came from, so its rain version (<file>_rain.png, from the exporter) can be found.
    static readonly Dictionary<Texture, string> _texturePaths = new();
    static readonly Dictionary<Material, Material> _rainMaterials = new();
    static bool _umaMuddy;

    // Raining (Weather): every horse girl swaps to her wet, muddy textures for the rest of the race.
    static void MuddyUmasWhenRaining()
    {
        if (_umaMuddy || Weather.Current != WeatherKind.Rain)
        {
            return;
        }
        _umaMuddy = true;
        SetDirt(1f);   // the real shaders' own wet / dirty look
        OnClear(() => SetDirt(0f));
        foreach (var costume in _costumes.Values.Where(costume => costume.Kind.StartsWith("uma:") && costume.Outfit != null))
        {
            Muddy(costume.Outfit.gameObject);
        }
    }

    static void Muddy(GameObject uma)
    {
        foreach (var renderer in uma.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = RainMaterial(materials[i]);
            }
            renderer.sharedMaterials = materials;
        }
    }

    static Material RainMaterial(Material dry)
    {
        if (dry == null || dry.mainTexture == null || !_texturePaths.TryGetValue(dry.mainTexture, out var path))
        {
            return dry;
        }
        if (_rainMaterials.TryGetValue(dry, out var cached) && cached != null)
        {
            return cached;
        }
        string rain = path.Substring(0, path.Length - ".png".Length) + "_rain.png";
        var wet = dry;
        if (File.Exists(rain))
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = dry.mainTexture.name + "_rain",
            };
            texture.LoadImage(File.ReadAllBytes(rain));
            wet = new Material(dry)
            {
                mainTexture = texture,
            };
            if (wet.HasProperty("_EmissionMap"))
            {
                wet.SetTexture("_EmissionMap", texture);
            }
        }
        _rainMaterials[dry] = wet;
        return wet;
    }

    // The anime outline without an outline shader: a copy of the skinned mesh pushed out along its normals with every
    // triangle turned inside out, so the normal back-face culling leaves only a thin dark rim around her. Skips the
    // face parts (face, eyes, brows, lines, blush: a shell there would draw a dark film over her face).
    static void AddOutline(SkinnedMeshRenderer skinned, Mesh mesh)
    {
        string name = mesh.name.ToLowerInvariant();
        if (IsFacePart(mesh.name) || name.Contains("eye") || mesh.normals.Length == 0)
        {
            return;
        }
        var normals = mesh.normals;
        var shell = mesh.vertices;
        for (int i = 0; i < shell.Length; i++)
        {
            shell[i] += normals[i] * OutlineWidth;
        }
        var inside = new List<int>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var triangles = mesh.GetTriangles(sub);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                inside.Add(triangles[i]);
                inside.Add(triangles[i + 2]);
                inside.Add(triangles[i + 1]);
            }
        }
        var outline = new Mesh
        {
            name = mesh.name + " outline",
            indexFormat = mesh.indexFormat,
            vertices = shell,
            normals = normals,
            boneWeights = mesh.boneWeights,
            bindposes = mesh.bindposes,
        };
        outline.SetTriangles(inside, 0);
        outline.RecalculateBounds();
        var holder = new GameObject(mesh.name + " outline");
        holder.transform.SetParent(skinned.transform.parent, false);
        var rim = holder.AddComponent<SkinnedMeshRenderer>();
        rim.bones = skinned.bones;
        rim.rootBone = skinned.rootBone;
        rim.sharedMesh = outline;
        rim.updateWhenOffscreen = true;
        rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rim.sharedMaterial = OutlineMaterial();
    }

    static Material OutlineMaterial()
    {
        if (_outline != null)
        {
            return _outline;
        }
        _outline = new Material(Shader.Find("Standard"))
        {
            color = Color.black,
        };
        _outline.SetFloat("_Glossiness", 0f);
        _outline.EnableKeyword("_EMISSION");
        _outline.SetColor("_EmissionColor", OutlineColor);
        return _outline;
    }

    // ---- the gallop ----

    // A run cycle on her skeleton: thighs and arms swing opposite, knees fold on the way through, elbows stay bent,
    // she leans into it and bobs. Faster car = faster cycle. Each bone is set from its rest pose every frame, then
    // turned about her own sideways axis, parents first, so it works whatever way the bones' own axes point.
    static System.Action<float, float> Gallop(Transform uma, GallopForm form)
    {
        var all = uma.GetComponentsInChildren<Transform>(true);
        Transform Find(string name) => all.FirstOrDefault(bone => bone.name == name);
        var hip = Find("Hip");
        var parts = new List<(Transform bone, Quaternion rest, System.Func<float, float> angle, float down)>();
        void Add(string name, System.Func<float, float> angle, float down = 0f)
        {
            var bone = Find(name);
            if (bone != null)
            {
                parts.Add((bone, bone.localRotation, angle, down));
            }
        }
        Add("Spine", _ => form.Lean);
        Add("Thigh_L", phase => -form.Thigh * Mathf.Sin(phase));
        Add("Thigh_R", phase => form.Thigh * Mathf.Sin(phase));
        Add("Knee_L", phase => form.Knee * Mathf.Max(0f, Mathf.Sin(phase + Mathf.PI * 0.5f)));
        Add("Knee_R", phase => form.Knee * Mathf.Max(0f, Mathf.Sin(phase - Mathf.PI * 0.5f)));
        Add("Arm_L", phase => form.Arms * Mathf.Sin(phase), UmaArmsDown);    // her left arm points -x: down = +z turn
        Add("Arm_R", phase => -form.Arms * Mathf.Sin(phase), -UmaArmsDown);
        Add("Elbow_L", _ => -UmaElbowBend);
        Add("Elbow_R", _ => -UmaElbowBend);
        var hipRest = hip != null ? hip.localPosition : Vector3.zero;
        float phase = 0f;
        return (seconds, speed) =>
        {
            if (uma == null)
            {
                return;
            }
            float cycles = Mathf.Clamp(Mathf.Abs(speed) / UmaStride, UmaMinCycles, UmaMaxCycles);
            phase += cycles * Mathf.PI * 2f * Time.deltaTime;
            var side = uma.right;
            var forward = uma.forward;
            foreach (var (bone, rest, angle, down) in parts)
            {
                bone.localRotation = rest;
                bone.rotation = Quaternion.AngleAxis(angle(phase), side) * Quaternion.AngleAxis(down, forward)
                    * bone.rotation;
            }
            if (hip != null)
            {
                hip.localPosition = hipRest + Vector3.up * (UmaBob * Mathf.Abs(Mathf.Sin(phase)));
            }
        };
    }

    // ---- the music ----

    static AudioSource _umaTheme;
    static readonly List<(AudioSource source, float volume)> _quietedMusic = new();

    // One of the game's race tracks (race_*.wav, exported from the game; theme.wav when there are none) on a loop,
    // read off the main thread; the game's own music sources go quiet until it stops.
    static IEnumerator PlayUmaMusic()
    {
        var tracks = Directory.Exists(UmaFolder) ? Directory.GetFiles(UmaFolder, "race_*.wav").ToList() : new List<string>();
        string path = tracks.Count > 0 ? tracks[Random.Range(0, tracks.Count)] : Path.Combine(UmaFolder, "theme.wav");
        if (!File.Exists(path))
        {
            yield break;
        }
        var decoding = System.Threading.Tasks.Task.Run(() => Wav.Decode(File.ReadAllBytes(path)));
        while (!decoding.IsCompleted)
        {
            yield return null;
        }
        if (decoding.IsFaulted || decoding.Result == null)
        {
            WarnUma(Path.GetFileName(path) + " is not a 16-bit PCM wav");
            yield break;
        }
        foreach (var source in Object.FindObjectsOfType<AudioSource>().Where(IsMusic))
        {
            _quietedMusic.Add((source, source.volume));
            source.volume = 0f;
        }
        _umaTheme = Root.AddComponent<AudioSource>();
        _umaTheme.clip = Wav.Clip(decoding.Result, Path.GetFileNameWithoutExtension(path));
        _umaTheme.loop = true;
        _umaTheme.spatialBlend = 0f;
        _umaTheme.volume = UmaMusicVolume;
        _umaTheme.Play();
        OnClear(RestoreMusic);
    }

    // The game's music: a looping source playing a long clip.
    static bool IsMusic(AudioSource source) =>
        source.loop && source.isPlaying && source.clip != null && source.clip.length > 40f;

    static void RestoreMusic()
    {
        foreach (var (source, volume) in _quietedMusic)
        {
            if (source != null)
            {
                source.volume = volume;
            }
        }
        _quietedMusic.Clear();
        _umaTheme = null;   // it lives on the chaos root, which is being destroyed
    }

    static void WarnUma(string why)
    {
        if (_umaWarned)
        {
            return;
        }
        _umaWarned = true;
        Plugin.Log.LogWarning("uma race: " + why);
    }
}
