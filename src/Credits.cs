// Adds an "API" block at the top of the game's Settings → Credits list (Menu scene): version (green = up to date,
// red = update available, white = unknown), commit (only when built from a git checkout), developer, Twitch link
// with hover/press states and a visible pointer. Re-injected whenever the menu reloads.
using TMPro;
using UnityEngine.EventSystems;

namespace StreamRacerApi;

public static class Credits
{
    const string ListPath = "Canvas/Main Panels/Settings/Content/Box 1/Panels/Credits/Content/List";
    const string Url = "https://twitch.tv/ShibikoX";
    static TextMeshProUGUI _title, _body; static float _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 2f;
        if (_body != null) { var link = _body.GetComponent<LinkOpener>(); _body.text = Body(link != null && link.Hover, false); return; }
        var listObject = GameObject.Find(ListPath); if (listObject == null) return;
        var list = listObject.transform;
        Transform titleSource = list.Find("VideoTitle"), descriptionSource = list.Find("VideoDescription");
        if (titleSource == null || descriptionSource == null) return;
        Updates.Kick();
        _title = Object.Instantiate(titleSource.gameObject, list).GetComponent<TextMeshProUGUI>();
        _title.gameObject.name = "ApiTitle"; _title.text = "API"; _title.transform.SetSiblingIndex(0);
        _body = Object.Instantiate(descriptionSource.gameObject, list).GetComponent<TextMeshProUGUI>();
        _body.gameObject.name = "ApiDescription"; _body.richText = true; _body.raycastTarget = true; _body.text = Body(); _body.transform.SetSiblingIndex(1);
        _body.gameObject.AddComponent<LinkOpener>();
        // the game's first title is taller (top padding for the list); we're first now, so take that height and give
        // the old first title the regular one
        var firstTitle = list.Find("MusicTitle") as RectTransform; var titleRect = _title.rectTransform;
        if (firstTitle != null) { var tall = firstTitle.sizeDelta; firstTitle.sizeDelta = ((RectTransform)titleSource).sizeDelta; titleRect.sizeDelta = tall; }
        Plugin.Instance.StartCoroutine(Relayout(list));
    }
    // the list grew: rebuild the layout (size fitter) and put the scroll back at the top
    static System.Collections.IEnumerator Relayout(Transform list)
    {
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            // the cloned box is a fixed height; our text is taller, so grow it or the link's bottom half isn't hoverable
            if (_body != null) { _body.ForceMeshUpdate(); var size = _body.rectTransform.sizeDelta; size.y = Mathf.Max(size.y, _body.preferredHeight + 6f); _body.rectTransform.sizeDelta = size; }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(list as RectTransform);
            var scrollRect = list.GetComponentInParent<UnityEngine.UI.ScrollRect>(); if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    static bool HasCommit => !string.IsNullOrEmpty(Plugin.Commit) && Plugin.Commit != "dev";
    static string Body(bool hover = false, bool press = false)
    {
        string versionColor = Updates.UpToDate == true ? "#3ddc84" : Updates.UpToDate == false ? "#ff3b30" : "#ffffff";
        string update = Updates.UpToDate == false ? $" <color=#ff3b30>(update available: {Updates.Latest})</color>" : "";
        string linkColor = press ? "#ffffff" : hover ? "#d5b8ff" : "#a970ff";
        string commit = HasCommit ? $"\nCommit - {Plugin.Commit}" : "";
        return $"Version - <color={versionColor}>{Plugin.Version}</color>{update}{commit}\nDeveloper - Shibiko\n<link=\"{Url}\"><color={linkColor}><u>{Url}</u></color></link>";
    }

    // Hover/press colors for the <link>, click opens it. Pointer enter/exit tell us when the mouse is over the text
    // (and which camera the canvas uses); inside that we hit-test the link every frame. The game hides the cursor in
    // its menus, so while over the link we show it ourselves (LateUpdate, so we win over the game's handling).
    public class LinkOpener : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        TMP_Text _text; bool _press, _inside; Camera _camera;
        public bool Hover { get; private set; }
        void Awake() => _text = GetComponent<TMP_Text>();
        Camera EventCamera()
        {
            if (_camera != null) return _camera;
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
        bool OverLink(Vector2 position) => _text != null && _text.isActiveAndEnabled && TMP_TextUtilities.FindIntersectingLink(_text, position, EventCamera()) >= 0;
        void LateUpdate()
        {
            bool over = _inside && OverLink(Input.mousePosition);
            if (over != Hover) { Hover = over; if (!over) _press = false; _text.text = Body(Hover, _press); }
            if (over) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        }
        public void OnPointerEnter(PointerEventData eventData) { _inside = true; _camera = eventData.enterEventCamera ?? _camera; }
        public void OnPointerExit(PointerEventData eventData) { _inside = false; }
        public void OnPointerDown(PointerEventData eventData) { _camera = eventData.pressEventCamera ?? _camera; if (OverLink(eventData.position)) { _press = true; _text.text = Body(true, true); } }
        public void OnPointerUp(PointerEventData eventData) { _press = false; _text.text = Body(Hover, false); }
        public void OnPointerClick(PointerEventData eventData)
        {
            int index = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, eventData.pressEventCamera);
            if (index >= 0) { Plugin.Log.LogInfo("credits link -> " + _text.textInfo.linkInfo[index].GetLinkID()); Application.OpenURL(_text.textInfo.linkInfo[index].GetLinkID()); }
        }
    }
}
