// Chaos.UmaLook: the horse girls in Umamusume's own look. Stream Racer's Unity 2021 loads the game's 2022 bundles
// (its 405 shaders report supported), so the exporter writes each girl's decrypted material / texture / shader
// bundles to StreamRacerApi.uma/bundles and names the material on every mesh part; here they are loaded, the global
// values the game feeds its character shaders are set (UmaViewer's UmaViewerGlobalShader defaults), and each girl
// gets the real materials: her own copy of the face material (its face-centre / up / forward follow her head every
// frame, like UmaViewer), the gym uniform's slots filled with frame-colour / skin textures the way the game does.
// No bundles (an older export) = Chaos.Uma's baked look.
using System.IO;
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

static partial class Chaos
{
    const float FaceCentreUp = 0.03f, FaceCentreForward = 0.03f;   // m from her Head bone (UmaViewer's offsets)
    const float CylinderBlend = 0.25f;                              // UmaViewer's value for face, eyes and hair

    static readonly Dictionary<string, AssetBundle> _umaBundles = new();
    static readonly Dictionary<string, Material> _umaMaterials = new();
    static readonly Dictionary<string, Texture2D> _umaTextures = new();
    static bool _umaGlobalsSet, _gymBundlesLoaded;

    // Her bundles (from model.json "bundles"), loaded once each; false when the folder has none or Unity refused one.
    static bool LoadRealLook(JObject info, bool gym)
    {
        var bundles = info["bundles"] as JArray;
        if (bundles == null || bundles.Count == 0)
        {
            return false;
        }
        bool ok = bundles.Select(token => (string)token).All(LoadUmaBundle);
        if (gym && !_gymBundlesLoaded)
        {
            _gymBundlesLoaded = true;
            string list = Path.Combine(UmaFolder, "gym", "bundles.json");
            if (File.Exists(list))
            {
                foreach (var token in (JArray)JObject.Parse(File.ReadAllText(list))["bundles"])
                {
                    LoadUmaBundle((string)token);
                }
            }
        }
        SetUmaGlobals();
        return ok && _umaMaterials.Count > 0;
    }

    static bool LoadUmaBundle(string file)
    {
        if (_umaBundles.ContainsKey(file))
        {
            return _umaBundles[file] != null;
        }
        string path = Path.Combine(UmaFolder, "bundles", file);
        var bundle = File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
        _umaBundles[file] = bundle;
        if (bundle == null)
        {
            return false;
        }
        foreach (var material in bundle.LoadAllAssets<Material>())
        {
            _umaMaterials[material.name] = material;
        }
        foreach (var texture in bundle.LoadAllAssets<Texture2D>())
        {
            _umaTextures[texture.name] = texture;
        }
        return true;
    }

    // What the Uma game feeds its character shaders every frame; without these they draw black.
    static void SetUmaGlobals()
    {
        if (_umaGlobalsSet)
        {
            return;
        }
        _umaGlobalsSet = true;
        Shader.SetGlobalColor("_GlobalToonColor", new Color(1f, 1f, 1f, 0f));
        Shader.SetGlobalColor("_GlobalRimColor", new Color(1f, 1f, 1f, 0f));
        Shader.SetGlobalColor("_Global_LightmapColor", Color.white);
        Shader.SetGlobalColor("_Global_LightmapDensityAddColor", Color.clear);
        Shader.SetGlobalColor("_Global_LightmapModulateColor", Color.white);
        Shader.SetGlobalFloat("_Global_MaxDensity", 1f);
        Shader.SetGlobalFloat("_Global_MaxHeight", 10f);
        Shader.SetGlobalFloat("_GlobalOutlineOffset", 1f);
        Shader.SetGlobalFloat("_GlobalOutlineWidth", 1f);
        Shader.SetGlobalFloat("_GlobalCameraFov", 0.1f);
        Shader.SetGlobalVectorArray("_HighParam1", new List<Vector4> { new(0f, 0f, 0f, 1f), new(0f, 0f, 0f, 1f), Vector4.zero });
        Shader.SetGlobalVectorArray("_HighParam2", new List<Vector4> { new(0f, 0f, 0f, 1f), new(0f, 0f, 0f, 1f) });
        Shader.SetGlobalVectorArray("_MainParam", new List<Vector4> { Vector4.zero, Vector4.zero });
        SetDirt(0f);
        Shader.SetGlobalColor("_AmbientColor", Color.white * 0.5f);
    }

    // The game's own rain look: how dirty (and wet) every girl's shader draws her, 0..1.
    static void SetDirt(float rate) => Shader.SetGlobalFloatArray("_DirtRate", new List<float> { rate, rate, rate });

    // The real material named on a mesh part, with UmaViewer's tweaks; null when it isn't in her bundles.
    static Material RealMaterial(string name)
    {
        if (name == null || !_umaMaterials.TryGetValue(name, out var material) || material == null)
        {
            return null;
        }
        string shader = material.shader != null ? material.shader.name : "";
        if (shader.Contains("ToonFace") || shader.Contains("ToonEye") || shader.Contains("ToonHair"))
        {
            material.SetFloat("_CylinderBlend", CylinderBlend);
        }
        if (shader.Contains("ToonFace"))
        {
            material.SetColor("_RimColor", new Color(0f, 0f, 0f, 0f));
        }
        if (shader.Contains("ToonMayu") && material.renderQueue < 3000)
        {
            material.renderQueue += 1;   // UmaViewer: keeps the eyebrows from vanishing behind the hair
        }
        return material;
    }

    // ---- night races ----

    const int UmaLayer = 31;                  // a render layer nothing in Stream Racer uses: only the girls are on it
    const float UmaNightLight = 0.95f;        // her own white key light's intensity on a night map

    static Light _umaLight;
    static readonly Dictionary<Light, int> _maskedLights = new();   // a game light -> its culling mask before ours

    // Night maps light everything with the game's MoonLight, a deep blue with almost no red: no colour multiplier
    // can bring that back. So on a night map the girls (on UmaLayer) are lit by their own white light from the moon's
    // direction instead, and the game's lights leave that layer alone; the map keeps its night. By day: the game's sun.
    static void BalanceUmaLight()
    {
        var key = KeyLight();
        bool night = key != null && key.color.r < 0.3f * key.color.maxColorComponent;
        if (night)
        {
            UseOwnLight(key);
        }
        else
        {
            UseSceneLight();
        }
    }

    static void UseOwnLight(Light key)
    {
        if (_umaLight == null)
        {
            _umaLight = new GameObject("sr-uma-light").AddComponent<Light>();
            _umaLight.transform.SetParent(Root.transform, false);
            _umaLight.type = LightType.Directional;
            _umaLight.color = new Color(1f, 0.97f, 0.93f);
            _umaLight.cullingMask = 1 << UmaLayer;
            _umaLight.shadows = LightShadows.None;
            OnClear(UseSceneLight);
        }
        _umaLight.intensity = UmaNightLight;
        _umaLight.transform.rotation = key.transform.rotation;
        _umaLight.enabled = true;
        foreach (var light in Object.FindObjectsOfType<Light>().Where(light => light != _umaLight))
        {
            if (!_maskedLights.ContainsKey(light))
            {
                _maskedLights[light] = light.cullingMask;
            }
            light.cullingMask &= ~(1 << UmaLayer);
        }
    }

    static void UseSceneLight()
    {
        if (_umaLight != null)
        {
            _umaLight.enabled = false;
        }
        foreach (var entry in _maskedLights.Where(entry => entry.Key != null))
        {
            entry.Key.cullingMask = entry.Value;
        }
        _maskedLights.Clear();
    }

    // Every part of her on UmaLayer (so her own light reaches her and only her).
    static void OnUmaLayer(GameObject uma)
    {
        foreach (var part in uma.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = UmaLayer;
        }
    }

    const float UmaAmbient = 0.75f;   // the soft white ambient the girls get instead of the map's (a night sky is blue)

    // Her own ambient light: every renderer gets custom-provided light-probe data (neutral white), so the map's sky
    // colour never tints her while the map itself keeps its own.
    static void NeutralAmbient(GameObject uma)
    {
        var probe = new UnityEngine.Rendering.SphericalHarmonicsL2();
        probe.AddAmbientLight(Color.white * UmaAmbient);
        var block = new MaterialPropertyBlock();
        block.CopySHCoefficientArraysFrom(new[] { probe });
        foreach (var renderer in uma.GetComponentsInChildren<Renderer>(true))
        {
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
            renderer.SetPropertyBlock(block);
        }
    }

    // The light actually lighting the map: the brightest active directional light (a night map's moon is its own
    // light, not RenderSettings.sun).
    static Light KeyLight() => Object.FindObjectsOfType<Light>()
        .Where(light => light.type == LightType.Directional && light.isActiveAndEnabled && !light.name.StartsWith("sr-"))
        .OrderByDescending(light => light.intensity * light.color.maxColorComponent)
        .FirstOrDefault();

    // ---- per girl ----

    // After her copy is made: her own face material (fed every frame) and, in gym clothes, the uniform's slots.
    // Returns what to run every frame.
    static System.Action RealLookFor(GameObject uma, string gymSkin)
    {
        bool gym = gymSkin != null;
        foreach (var renderer in uma.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null || material.shader == null)
                {
                    continue;
                }
                if (material.shader.name.Contains("ToonFace"))
                {
                    materials[i] = new Material(material);
                    changed = true;
                }
                else if (gym && material.name.StartsWith("mtl_bdy0001_00_"))
                {
                    materials[i] = GymSlot(new Material(material), i, gymSkin);
                    changed = true;
                }
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
        return FaceFeeder(uma);
    }

    // Feeds her (own copy of the) face material every frame; nothing to do when she has none.
    static System.Action FaceFeeder(GameObject uma)
    {
        var head = uma.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == "Head");
        var faces = uma.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null && material.shader != null && material.shader.name.Contains("ToonFace"))
            .Distinct()
            .ToList();
        return () => FeedFace(head, faces);
    }

    // The face shader shades her by where her face is and which way it points (UmaViewer UpdateFaceLightRuntime).
    static void FeedFace(Transform head, List<Material> faces)
    {
        if (head == null || faces.Count == 0)
        {
            return;
        }
        var up = head.up.normalized;
        var forward = head.forward.normalized;
        float scale = head.lossyScale.y;
        var centre = head.position + (up * FaceCentreUp + forward * FaceCentreForward) * scale;
        foreach (var face in faces)
        {
            face.SetVector("_FaceCenterPos", centre);
            face.SetVector("_FaceUp", up);
            face.SetVector("_FaceForward", forward);
            face.SetMatrix("_faceShadowHeadMat", head.worldToLocalMatrix);
        }
    }

    // The gym uniform's three slots, as the game fills them (UmaViewer, case "0001"): 0 = the clothes in a gate-frame
    // colour, 1 = her skin for her skin / bust / socks (the baked export names it), 2 = the bib panel (white, no number).
    static Material GymSlot(Material material, int slot, string skin)
    {
        if (slot == 0)
        {
            SetTextures(material, "tex_bdy0001_00_00_waku" + Random.Range(0, 8), "tex_bdy0001_00_00_waku0");
        }
        else if (slot == 1)
        {
            string bust = skin.Split('_')[5];
            SetTextures(material, skin, "tex_bdy0001_00_00_0_" + bust + "_00");
        }
        else if (slot == 2)
        {
            string bust = skin.Split('_')[5];
            SetTextures(material, "tex_bdy0001_00_zekken" + WhiteBib + "_" + bust, "tex_bdy0001_00_zekken0_" + bust);
            if (material.HasProperty("_ZekkenNumberTex"))
            {
                material.SetTexture("_ZekkenNumberTex", ClearTexture());   // no number on her bib
            }
        }
        return material;
    }

    // The shared tail material carries no colour (the game fills it in per girl): her own copy with her tail textures
    // (the export names them: tex_tail<model>_00_<id>_diff), masks from the model's shared set (_0000_base / _ctrl).
    static Texture2D _clear;

    static Texture2D ClearTexture()
    {
        if (_clear == null)
        {
            _clear = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _clear.SetPixel(0, 0, Color.clear);
            _clear.Apply();
        }
        return _clear;
    }

    static Material TailMaterial(Material shared, string texture)
    {
        if (texture == null || !texture.EndsWith("_diff"))
        {
            return shared;
        }
        string stem = texture.Substring(0, texture.Length - "_diff".Length);
        int sharedAt = stem.IndexOf("_00_");
        string masks = sharedAt >= 0 ? stem.Substring(0, sharedAt + "_00_".Length) + "0000" : stem;   // exclusive: her own
        var own = new Material(shared);
        SetTextures(own, stem, masks);
        return own;
    }

    // _MainTex / _ToonMap from `stem` (_diff / _shad_c), _TripleMaskMap / _OptionMaskMap from `masks` (_base / _ctrl).
    static void SetTextures(Material material, string stem, string masks)
    {
        void Set(string property, string texture)
        {
            if (_umaTextures.TryGetValue(texture, out var found) && found != null && material.HasProperty(property))
            {
                material.SetTexture(property, found);
            }
        }
        Set("_MainTex", stem + "_diff");
        Set("_ToonMap", stem + "_shad_c");
        Set("_TripleMaskMap", masks + "_base");
        Set("_OptionMaskMap", masks + "_ctrl");
    }
}
