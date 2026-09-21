using System.Collections.Generic;
using System.Linq;
using Cage.StreamRacer;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StreamRacerApi;

// In-game picture-in-picture mini map, fully custom: a second orthographic camera that renders ONLY our
// private layer, which holds a translucent backdrop, a line traced along the route, and one colored dot per car.
static class Minimap
{
    const int Layer = 30; // unused by the game

    public class Cfg
    {
        public bool enabled = true;
        public float x = 0.02f, y = 0.03f, w = 0.18f, h = 0f; // screen fractions from bottom-left; h = 0 -> follow the track's aspect
        public float marker = 3f;        // dot diameter as % of the map's height
        public string bg = "#000000"; public float alpha = 0.55f;
        public string track = "#ffffff";
        public float pad = 1.15f;        // margin around the track bounds
        public bool leaderBig = true;
        public bool names = true;        // labels next to the dots (nudged apart so they don't overlap)
        public string aspect = "1:1";    // /minimap page: fills the window, keeps this ratio (16:9, 4:3, 1:1, 21:9, auto = track)
    }
    public static Cfg Current = new();

    static Camera _cam;
    static Transform _backdrop;
    static LineRenderer _line;
    static readonly Dictionary<CFBJLEBOFHJ, Transform> _markers = new();
    static readonly Dictionary<CFBJLEBOFHJ, TextMesh> _labels = new();
    static Font _font;
    static Material _mat;
    static Bounds _bounds;
    static float _half;

    public static void Configure(JObject o) { Current = o?.ToObject<Cfg>() ?? new Cfg(); Apply(); }

    public static object State => new
    {
        Current.enabled, Current.x, Current.y, Current.w, Current.h, Current.marker, Current.bg, Current.alpha, Current.track, Current.pad, Current.leaderBig, Current.names, Current.aspect,
        live = _cam != null,
    };

    static Material Mat() => _mat ??= new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
    static Color Parse(string hex, float alpha, Color fallback) { var c = ColorUtility.TryParseHtmlString(hex ?? "", out var p) ? p : fallback; c.a = alpha; return c; }

    // Build or refresh everything from the config and the current track. Safe to call any time.
    public static void Apply()
    {
        if (!Current.enabled || !Game.Running || Camera.main == null) { Destroy(); return; }
        var route = Game.TrackPoints();
        if (route.Count < 2) { Destroy(); return; }
        _bounds = new Bounds(route[0], Vector3.zero);
        foreach (var p in route) _bounds.Encapsulate(p);

        float trackAspect = Mathf.Max(0.1f, _bounds.size.x) / Mathf.Max(0.1f, _bounds.size.z);
        float h = Pure.MapHeight(Current.w, Screen.width, Screen.height, Pure.AspectRatio(Current.aspect, trackAspect)); // same ratio as the /minimap page
        float boxAspect = (Current.w * Screen.width) / Mathf.Max(1f, h * Screen.height);
        _half = Mathf.Max(_bounds.extents.z, _bounds.extents.x / boxAspect) * Current.pad + 2f;

        if (_cam == null)
        {
            var go = new GameObject("StreamRacerApi.Minimap");
            _cam = go.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.Depth;   // draw over the main view; our backdrop supplies the box
            _cam.cullingMask = 1 << Layer;              // only our stuff
            _cam.depth = Camera.main.depth + 5;
            _cam.farClipPlane = 3000f;
            Camera.main.cullingMask &= ~(1 << Layer);
        }
        _cam.rect = new Rect(Current.x, Current.y, Current.w, h);
        _cam.orthographicSize = _half;
        _cam.transform.position = new Vector3(_bounds.center.x, _bounds.max.y + 400f, _bounds.center.z);
        _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // backdrop: a quad far below everything, sized to the viewport
        if (_backdrop == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>());
            q.name = "SRMinimap.Backdrop"; q.layer = Layer; q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.GetComponent<Renderer>().material = new Material(Mat());
            _backdrop = q.transform;
        }
        _backdrop.position = new Vector3(_bounds.center.x, _bounds.min.y - 200f, _bounds.center.z);
        _backdrop.localScale = new Vector3(_half * 2f * boxAspect + 10f, _half * 2f + 10f, 1f);
        _backdrop.GetComponent<Renderer>().material.color = Parse(Current.bg, Mathf.Clamp01(Current.alpha), Color.black);

        // route line
        if (_line == null)
        {
            var go = new GameObject("SRMinimap.Track"); go.layer = Layer;
            _line = go.AddComponent<LineRenderer>();
            _line.material = new Material(Mat());
            _line.useWorldSpace = true; _line.loop = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _line.receiveShadows = false;
            _line.numCornerVertices = 4; _line.numCapVertices = 4;
        }
        float lift = _bounds.max.y + 30f;
        var pts = route.Select(p => new Vector3(p.x, lift, p.z)).ToArray();
        _line.positionCount = pts.Length; _line.SetPositions(pts);
        _line.startWidth = _line.endWidth = _half * 0.035f;
        _line.material.color = Parse(Current.track, 1f, Color.white);
    }

    // StartCurrentGame only kicks off a countdown; build once the race is really running.
    public static System.Collections.IEnumerator WhenRunning()
    {
        for (float t = 0; t < 120f && !Game.Running; t += 0.5f) yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(0.5f);
        Apply();
    }

    public static void Destroy()
    {
        foreach (var m in _markers.Values) if (m != null) Object.Destroy(m.gameObject);
        _markers.Clear();
        foreach (var l in _labels.Values) if (l != null) Object.Destroy(l.gameObject);
        _labels.Clear();
        if (_line != null) { Object.Destroy(_line.gameObject); _line = null; }
        if (_backdrop != null) { Object.Destroy(_backdrop.gameObject); _backdrop = null; }
        if (_cam != null) { Object.Destroy(_cam.gameObject); _cam = null; }
    }

    // Per frame: one colored disc per car above the route line.
    public static void Tick()
    {
        if (_cam == null) return;
        // The game swaps cameras (prop cams, follow, free); none of them may draw our layer.
        foreach (var c in Camera.allCameras) if (c != _cam && (c.cullingMask & (1 << Layer)) != 0) c.cullingMask &= ~(1 << Layer);
        var alive = new HashSet<CFBJLEBOFHJ>();
        var leader = Cam.Leader();
        float lift = _bounds.max.y + 40f;
        foreach (var v in Game.Vehicles())
        {
            if (v.JPHIMKLIAAO == null) continue;
            alive.Add(v);
            if (!_markers.TryGetValue(v, out var m) || m == null) _markers[v] = m = MakeMarker(v);
            float s = _half * 2f * Current.marker / 100f * (Current.leaderBig && v == leader ? 1.6f : 1f) * (v.NIKOEDJIAFB ? 0.6f : 1f);
            var p = v.JPHIMKLIAAO.transform.position;
            m.position = new Vector3(p.x, lift + (v == leader ? 1f : 0f), p.z);
            m.localScale = new Vector3(s, s, s);
        }
        foreach (var k in _markers.Keys.Where(k => !alive.Contains(k)).ToList()) { if (_markers[k] != null) Object.Destroy(_markers[k].gameObject); _markers.Remove(k); }
        Labels(alive, lift);
    }

    // Names sit to the right of their dot (left if that would run off the map), pushed apart vertically so they never overlap.
    static void Labels(HashSet<CFBJLEBOFHJ> alive, float lift)
    {
        if (!Current.names) { if (_labels.Count > 0) { foreach (var l in _labels.Values) if (l != null) Object.Destroy(l.gameObject); _labels.Clear(); } return; }
        float size = _half * 2f * Current.marker / 100f;      // world units per dot
        float lineH = size * 1.3f, charW = size * 0.62f, gap = size * 0.7f;
        float viewW = _cam.orthographicSize * _cam.aspect, right = _bounds.center.x + viewW - size, left = _bounds.center.x - viewW + size;
        float top = _bounds.center.z + _cam.orthographicSize - lineH * 0.6f, bottom = _bounds.center.z - _cam.orthographicSize + lineH * 0.6f;
        var placed = new List<(float x0, float x1, float z)>();
        foreach (var v in alive.OrderByDescending(v => v.JPHIMKLIAAO.transform.position.z))
        {
            if (!_labels.TryGetValue(v, out var tm) || tm == null) _labels[v] = tm = MakeLabel(v);
            var p = v.JPHIMKLIAAO.transform.position;
            string txt = v.JDDOIMHIFHK.AMCIKHEHBGM ?? Game.Login(v);
            float w = txt.Length * charW;
            bool flip = p.x + gap + w > right;               // would run off the right edge -> put it on the left
            float x0 = flip ? p.x - gap - w : p.x + gap, x1 = x0 + w;
            float z = Mathf.Clamp(p.z, bottom, top);
            bool moved = true; int guard = 0;
            while (moved && guard++ < 20)
            {
                moved = false;
                foreach (var o in placed)
                    if (x0 < o.x1 && x1 > o.x0 && Mathf.Abs(z - o.z) < lineH) { z = o.z - lineH; moved = true; }
            }
            placed.Add((x0, x1, z));
            tm.text = txt;
            tm.color = v.JDDOIMHIFHK.EKPDDGFGLNI;
            tm.anchor = flip ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            tm.alignment = flip ? TextAlignment.Right : TextAlignment.Left;
            tm.characterSize = size * 0.32f;
            tm.transform.position = new Vector3(flip ? p.x - gap : p.x + gap, lift + 2f, z);
        }
        foreach (var k in _labels.Keys.Where(k => !alive.Contains(k)).ToList()) { if (_labels[k] != null) Object.Destroy(_labels[k].gameObject); _labels.Remove(k); }
    }

    static TextMesh MakeLabel(CFBJLEBOFHJ v)
    {
        _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("SRMinimap.Label." + Game.Login(v)) { layer = Layer };
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.font = _font; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleLeft; tm.alignment = TextAlignment.Left;
        tm.color = Color.white; tm.fontStyle = FontStyle.Bold;
        var r = go.GetComponent<MeshRenderer>();
        r.material = _font.material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return tm;
    }

    static Transform MakeMarker(CFBJLEBOFHJ v)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(go.GetComponent<Collider>());
        go.name = "SRMinimap." + Game.Login(v); go.layer = Layer;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var r = go.GetComponent<Renderer>();
        r.material = new Material(Mat()) { color = v.JDDOIMHIFHK.EKPDDGFGLNI, mainTexture = Disc() };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return go.transform;
    }

    static Texture2D _disc;
    static Texture2D Disc()
    {
        if (_disc != null) return _disc;
        const int n = 64; _disc = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            _disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 8f)));
        }
        _disc.Apply();
        return _disc;
    }
}
