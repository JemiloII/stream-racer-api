using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StreamRacerApi;

// Adds an "API" block at the top of the game's Settings → Credits list (Menu scene): version (green = up to date,
// red = update available, white = unknown), commit (only when built from a git checkout), developer, Twitch link
// with hover/press states and a visible pointer. Re-injected whenever the menu reloads.
public static class Credits
{
    const string ListPath = "Canvas/Main Panels/Settings/Content/Box 1/Panels/Credits/Content/List";
    const string Url = "https://twitch.tv/ShibikoX";
    static TextMeshProUGUI _title, _body; static float _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 2f;
        if (_body != null) { var l = _body.GetComponent<LinkOpener>(); _body.text = Body(l != null && l.Hover, false); return; }
        var list = GameObject.Find(ListPath); if (list == null) return;
        var t = list.transform;
        Transform titleSrc = t.Find("VideoTitle"), descSrc = t.Find("VideoDescription");
        if (titleSrc == null || descSrc == null) return;
        Updates.Kick();
        _title = UnityEngine.Object.Instantiate(titleSrc.gameObject, t).GetComponent<TextMeshProUGUI>();
        _title.gameObject.name = "ApiTitle"; _title.text = "API"; _title.transform.SetSiblingIndex(0);
        _body = UnityEngine.Object.Instantiate(descSrc.gameObject, t).GetComponent<TextMeshProUGUI>();
        _body.gameObject.name = "ApiDescription"; _body.richText = true; _body.raycastTarget = true; _body.text = Body(); _body.transform.SetSiblingIndex(1);
        _body.gameObject.AddComponent<LinkOpener>();
        // the game's first title is taller (top padding for the list); we're first now, so take that height and give
        // the old first title the regular one
        var first = t.Find("MusicTitle") as RectTransform; var tr = _title.rectTransform;
        if (first != null) { var tall = first.sizeDelta; first.sizeDelta = ((RectTransform)titleSrc).sizeDelta; tr.sizeDelta = tall; }
        Plugin.Instance.StartCoroutine(Relayout(t));
    }
    // the list grew: rebuild the layout (size fitter) and put the scroll back at the top
    static System.Collections.IEnumerator Relayout(Transform list)
    {
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            // the cloned box is a fixed height; our text is taller, so grow it or the link's bottom half isn't hoverable
            if (_body != null) { _body.ForceMeshUpdate(); var sd = _body.rectTransform.sizeDelta; sd.y = Mathf.Max(sd.y, _body.preferredHeight + 6f); _body.rectTransform.sizeDelta = sd; }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(list as RectTransform);
            var sr = list.GetComponentInParent<UnityEngine.UI.ScrollRect>(); if (sr != null) sr.verticalNormalizedPosition = 1f;
        }
    }

    static bool HasCommit => !string.IsNullOrEmpty(Plugin.Commit) && Plugin.Commit != "dev";
    static string Body(bool hover = false, bool press = false)
    {
        string vc = Updates.UpToDate == true ? "#3ddc84" : Updates.UpToDate == false ? "#ff3b30" : "#ffffff";
        string upd = Updates.UpToDate == false ? $" <color=#ff3b30>(update available: {Updates.Latest})</color>" : "";
        string lc = press ? "#ffffff" : hover ? "#d5b8ff" : "#a970ff";
        string commit = HasCommit ? $"\nCommit - {Plugin.Commit}" : "";
        return $"Version - <color={vc}>{Plugin.Version}</color>{upd}{commit}\nDeveloper - Shibiko\n<link=\"{Url}\"><color={lc}><u>{Url}</u></color></link>";
    }

    // Hover/press colors for the <link>, click opens it. Pointer enter/exit tell us when the mouse is over the text
    // (and which camera the canvas uses); inside that we hit-test the link every frame. The game hides the cursor in
    // its menus, so while over the link we show it ourselves (LateUpdate, so we win over the game's handling).
    public class LinkOpener : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        TMP_Text _tmp; bool _press, _inside; Camera _cam;
        public bool Hover { get; private set; }
        void Awake() => _tmp = GetComponent<TMP_Text>();
        Camera Cam() { if (_cam != null) return _cam; var c = GetComponentInParent<Canvas>(); return c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null; }
        bool OverLink(Vector2 pos) => _tmp != null && _tmp.isActiveAndEnabled && TMP_TextUtilities.FindIntersectingLink(_tmp, pos, Cam()) >= 0;
        void LateUpdate()
        {
            bool over = _inside && OverLink(Input.mousePosition);
            if (over != Hover) { Hover = over; if (!over) _press = false; _tmp.text = Body(Hover, _press); }
            if (over) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        }
        public void OnPointerEnter(PointerEventData e) { _inside = true; _cam = e.enterEventCamera ?? _cam; }
        public void OnPointerExit(PointerEventData e) { _inside = false; }
        public void OnPointerDown(PointerEventData e) { _cam = e.pressEventCamera ?? _cam; if (OverLink(e.position)) { _press = true; _tmp.text = Body(true, true); } }
        public void OnPointerUp(PointerEventData e) { _press = false; _tmp.text = Body(Hover, false); }
        public void OnPointerClick(PointerEventData e)
        {
            int i = TMP_TextUtilities.FindIntersectingLink(_tmp, e.position, e.pressEventCamera);
            if (i >= 0) { Plugin.Log.LogInfo("credits link -> " + _tmp.textInfo.linkInfo[i].GetLinkID()); Application.OpenURL(_tmp.textInfo.linkInfo[i].GetLinkID()); }
        }
    }
}
