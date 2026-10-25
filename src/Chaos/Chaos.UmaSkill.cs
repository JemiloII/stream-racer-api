// Chaos.UmaSkill: in an Uma race a boost is a speed skill, shown the way Umamusume shows a skill's effect in a race:
// its own "Velocity Up" indicator (the atlas/race/race sprite utx_race_skill_icon_0_000, exported as velocity_up.png)
// pops up over the girl's head, rises a little and fades. Built from Unity UI on a screen overlay under the chaos
// root (a clear removes it).
using System.Collections;
using System.IO;
using UnityEngine.UI;

namespace StreamRacerApi;

static partial class Chaos
{
    const float SkillSeconds = 1.4f;              // on screen this long
    const float SkillRise = 40f;                  // px (1080p) it drifts up over that time
    const float SkillFadeSeconds = 0.4f;          // fading out at the end
    const float SkillAboveHead = 0.45f;           // m above her head (at her size) where it appears
    const float SkillWidth = 239f, SkillHeight = 50f;   // the sprite's own size

    static Canvas _skillCanvas;
    static Texture2D _velocityUp;

    // Patches (pool boost) and Game.Boost (free boost): a boost on a horse girl's car shows her skill effect.
    public static void OnBoost(Vehicle vehicle) => Guard("skill", () =>
    {
        if (vehicle != null && _costumes.TryGetValue(vehicle, out var costume) && costume.Kind.StartsWith("uma:")
            && costume.Outfit != null)
        {
            Spawn(SkillEffect(costume.Outfit));
        }
    });

    static IEnumerator SkillEffect(Transform outfit)
    {
        var head = outfit.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == "Head");
        var texture = VelocityUpTexture();
        var canvas = SkillCanvas();
        if (head == null || texture == null || canvas == null)
        {
            yield break;
        }
        var image = new GameObject("sr-skill", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.rectTransform.SetParent(canvas.transform, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
        image.rectTransform.sizeDelta = new Vector2(SkillWidth, SkillHeight);
        image.texture = texture;
        image.raycastTarget = false;
        for (float time = 0f; time < SkillSeconds && image != null && head != null; time += Time.deltaTime)
        {
            PlaceOverHead(image.rectTransform, head, time / SkillSeconds * SkillRise);
            float alpha = Mathf.Min(time / 0.12f, Mathf.Clamp01((SkillSeconds - time) / SkillFadeSeconds));
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            yield return null;
        }
        if (image != null)
        {
            Object.Destroy(image.gameObject);
        }
    }

    // Over her head on screen (plus `rise` px); hidden while she is behind the camera.
    static void PlaceOverHead(RectTransform panel, Transform head, float rise)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        var screen = camera.WorldToScreenPoint(head.position + Vector3.up * (SkillAboveHead * head.lossyScale.y));
        panel.gameObject.SetActive(screen.z > 0f);
        var canvas = (RectTransform)_skillCanvas.transform;
        float scale = canvas.rect.width / Mathf.Max(1f, Screen.width);
        panel.anchoredPosition = new Vector2(screen.x * scale, screen.y * scale + rise);
    }

    static Canvas SkillCanvas()
    {
        if (_skillCanvas != null)
        {
            return _skillCanvas;
        }
        var holder = new GameObject("sr-skill-canvas", typeof(Canvas), typeof(CanvasScaler));
        holder.transform.SetParent(Root.transform, false);
        _skillCanvas = holder.GetComponent<Canvas>();
        _skillCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _skillCanvas.sortingOrder = 500;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return _skillCanvas;
    }

    static Texture2D VelocityUpTexture()
    {
        if (_velocityUp != null)
        {
            return _velocityUp;
        }
        string path = Path.Combine(UmaFolder, "velocity_up.png");
        if (!File.Exists(path))
        {
            return null;
        }
        _velocityUp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _velocityUp.LoadImage(File.ReadAllBytes(path));
        return _velocityUp;
    }
}
