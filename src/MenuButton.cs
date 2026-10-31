// A "CONTROL PANEL" button on the game's Home menu (a copy of its WHAT'S NEW button, placed to its left) that opens the
// mod's web page in the browser; the F10 hotkey (config hotkeys.OpenControlPanel) does the same anywhere in the game.
// Also fits the game's profile pill (bottom left) to the streamer's name. Re-injected whenever the menu reloads.
using TMPro;

namespace StreamRacerApi;

public static class MenuButton
{
    const string HomePath = "Canvas/Main Panels/Home/Content";
    const string Label = "CONTROL PANEL";
    const float Gap = 12f;  // px between ours and WHAT'S NEW, on its left
    static GameObject _button;
    static RectTransform _source;
    static float _placeAt;
    static float _nextFit;
    const string ProfilePath = "Canvas/Main Panels/Bottom/Profile";
    const float ProfileChrome = 115f;   // the profile pill around its name: picture + paddings (600 wide, 485 of it name)
    static float _next;

    public static string Url => $"http://127.0.0.1:{Plugin.Port.Value}/";

    public static void OpenControlPanel()
    {
        Plugin.Log.LogInfo("opening the control panel: " + Url);
        Application.OpenURL(Url);
    }

    public static void Tick()
    {
        if (Time.unscaledTime >= _nextFit)
        {
            _nextFit = Time.unscaledTime + 1f;
            FitProfile();
        }
        if (_button != null && _placeAt > 0f && Time.unscaledTime >= _placeAt)
        {
            Place();
        }
        if (_button != null || Time.unscaledTime < _next)
        {
            return;
        }
        _next = Time.unscaledTime + 1f;
        var source = GameObject.Find(HomePath)?.transform.Find("What's New");
        if (source == null)
        {
            return;
        }
        _button = Object.Instantiate(source.gameObject, source.parent);
        _button.name = "Control Panel";
        var rect = (RectTransform)_button.transform;
        var sourceRect = (RectTransform)source;
        _source = sourceRect;
        _placeAt = Time.unscaledTime + 0.2f;   // placed once the game's layout has sized it to the new text
        foreach (var text in _button.GetComponentsInChildren<TMP_Text>(true))
        {
            foreach (var behaviour in text.GetComponents<MonoBehaviour>())
            {
                if (behaviour is not TMP_Text && behaviour is not UnityEngine.UI.ContentSizeFitter && behaviour is not UnityEngine.UI.LayoutElement)
                {
                    Object.Destroy(behaviour);   // the game's localiser would put WHAT'S NEW back
                }
            }
            text.text = Label;
        }
        var button = _button.GetComponent<UnityEngine.UI.Button>();
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();   // drops the game's "open news" listener
        button.onClick.AddListener(OpenControlPanel);
    }

    // Left of WHAT'S NEW: our left edge sits our visible width (the widest of its pill parts, which size to the text
    // from the left) plus a gap before its left edge.
    static void Place()
    {
        _placeAt = 0f;
        if (_source == null)
        {
            return;
        }
        var rect = (RectTransform)_button.transform;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        float width = rect.rect.width;
        foreach (RectTransform part in rect)
        {
            width = Mathf.Max(width, part.rect.width);
        }
        float sourceLeft = _source.anchoredPosition.x - _source.pivot.x * _source.rect.width;
        rect.anchoredPosition = new Vector2(sourceLeft - Gap - width + rect.pivot.x * rect.rect.width, _source.anchoredPosition.y);
    }

    // The game's profile pill (picture + streamer name, bottom left) is a fixed 600 px wide, so its hover box reached
    // over our button: size it to the name instead.
    static void FitProfile()
    {
        var profile = GameObject.Find(ProfilePath)?.transform as RectTransform;
        var name = profile?.Find("Normal/Profile Name")?.GetComponent<TMP_Text>();
        if (name == null)
        {
            return;
        }
        float width = Mathf.Ceil(ProfileChrome + name.preferredWidth + 10f);
        if (Mathf.Abs(profile.rect.width - width) < 1f)
        {
            return;
        }
        profile.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        var layout = profile.GetComponent<UnityEngine.UI.LayoutElement>() ?? profile.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.preferredWidth = width;   // in case the bottom bar's layout sizes its children
        layout.flexibleWidth = 0f;
    }
}
