// In-game picture-in-picture mini map, fully custom: a second orthographic camera that renders ONLY our
// private layer, which holds a translucent backdrop, a line traced along the route, and one colored dot per car.
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

static class Minimap
{
    const int Layer = 30; // unused by the game

    public class Cfg
    {
        public bool enabled = true;      // the IN-GAME map only; the /minimap browser source always works
        public float x = 0.02f, y = 0.03f, w = 0.18f, h = 0f; // screen fractions from bottom-left; h = 0 -> follow the track's aspect
        public float marker = 3f;        // dot diameter as % of the map's height
        public string bg = "#000000"; public float alpha = 0f;
        public string track = "#ffffff";
        public float pad = 1.15f;        // margin around the track bounds
        public bool leaderBig = true;
        public bool names = true;        // labels to the right of the dots; they never move (the view leaves room for them)
        public bool mapTitle = true;     // the map's name and author above the map (in game and on the /minimap page)
        public string aspect = "1:1";    // /minimap page: fills the window, keeps this ratio (16:9, 4:3, 1:1, 21:9, auto = track)
    }
    public static Cfg Current = new();

    static Camera _camera;
    static Transform _backdrop;
    static LineRenderer _line;
    static readonly Dictionary<Vehicle, Transform> _markers = new();
    static readonly Dictionary<Vehicle, TextMesh> _labels = new();
    static TextMesh _mapTitle;   // "Gherkin Gauntlent · by UnclePickle89" above the map
    static Font _font;
    static Material _material;
    static Bounds _bounds;
    static float _halfHeight, _centerOffsetX, _centerOffsetZ;

    public static void Configure(JObject config) { Current = config?.ToObject<Cfg>() ?? new Cfg(); Apply(); }

    public static object State => new
    {
        Current.enabled, Current.x, Current.y, Current.w, Current.h, Current.marker, Current.bg, Current.alpha, Current.track, Current.pad, Current.leaderBig, Current.names, Current.mapTitle, Current.aspect,
        live = _camera != null,
    };

    static Material BaseMaterial() => _material ??= new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
    static Color Parse(string hex, float alpha, Color fallback) { var color = ColorUtility.TryParseHtmlString(hex ?? "", out var parsed) ? parsed : fallback; color.a = alpha; return color; }

    // Build or refresh everything from the config and the current track. Safe to call any time.
    public static void Apply()
    {
        if (!Current.enabled || !Game.Running || Camera.main == null) { Destroy(); return; }
        var route = Game.TrackPoints();
        if (route.Count < 2) { Destroy(); return; }
        _bounds = new Bounds(route[0], Vector3.zero);
        foreach (var point in route) _bounds.Encapsulate(point);

        float trackAspect = Mathf.Max(0.1f, _bounds.size.x) / Mathf.Max(0.1f, _bounds.size.z);
        float height = Pure.MapHeight(Current.w, Screen.width, Screen.height, Pure.AspectRatio(Current.aspect, trackAspect)); // same ratio as the /minimap page
        float boxAspect = (Current.w * Screen.width) / Mathf.Max(1f, height * Screen.height);
        // room on the right for the names (they sit to the right of their dot and never move): about 12 characters
        float baseHalf = Mathf.Max(_bounds.extents.z, _bounds.extents.x / boxAspect) * Current.pad + 2f;
        float labelRoom = Current.names ? 12f * (baseHalf * 2f * Current.marker / 100f) * 0.62f : 0f;
        _halfHeight = Mathf.Max(_bounds.extents.z * Current.pad, (_bounds.extents.x * Current.pad + labelRoom) / boxAspect) + 2f;
        _centerOffsetX = labelRoom / 2f;
        // room at the top for the map title, so it never sits on the track
        float titleRoom = Current.mapTitle ? (_halfHeight * 2f * Current.marker / 100f) * 1.5f : 0f;
        _halfHeight += titleRoom / 2f;
        _centerOffsetZ = -titleRoom / 2f;

        if (_camera == null)
        {
            var cameraObject = new GameObject("StreamRacerApi.Minimap");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.Depth;   // draw over the main view; our backdrop supplies the box
            _camera.cullingMask = 1 << Layer;              // only our stuff
            _camera.depth = Camera.main.depth + 5;
            _camera.farClipPlane = 3000f;
            Camera.main.cullingMask &= ~(1 << Layer);
        }
        _camera.rect = new Rect(Current.x, Current.y, Current.w, height);
        _camera.orthographicSize = _halfHeight;
        _camera.transform.position = new Vector3(_bounds.center.x + _centerOffsetX, _bounds.max.y + 400f, _bounds.center.z - _centerOffsetZ);
        _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // backdrop: a quad far below everything, sized to the viewport
        if (_backdrop == null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(quad.GetComponent<Collider>());
            quad.name = "SRMinimap.Backdrop"; quad.layer = Layer; quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.GetComponent<Renderer>().material = new Material(BaseMaterial());
            _backdrop = quad.transform;
        }
        _backdrop.position = new Vector3(_bounds.center.x, _bounds.min.y - 200f, _bounds.center.z);
        _backdrop.localScale = new Vector3(_halfHeight * 2f * boxAspect + 10f, _halfHeight * 2f + 10f, 1f);
        _backdrop.GetComponent<Renderer>().material.color = Parse(Current.bg, Mathf.Clamp01(Current.alpha), Color.black);

        // route line
        if (_line == null)
        {
            var lineObject = new GameObject("SRMinimap.Track"); lineObject.layer = Layer;
            _line = lineObject.AddComponent<LineRenderer>();
            _line.material = new Material(BaseMaterial());
            _line.useWorldSpace = true; _line.loop = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _line.receiveShadows = false;
            _line.numCornerVertices = 4; _line.numCapVertices = 4;
        }
        float lift = _bounds.max.y + 30f;
        var linePoints = route.Select(point => new Vector3(point.x, lift, point.z)).ToArray();
        _line.positionCount = linePoints.Length; _line.SetPositions(linePoints);
        _line.startWidth = _line.endWidth = _halfHeight * 0.035f;
        _line.material.color = Parse(Current.track, 1f, Color.white);
    }

    // StartCurrentGame only kicks off a countdown; build once the race is really running.
    public static System.Collections.IEnumerator WhenRunning()
    {
        for (float waited = 0; waited < 120f && !Game.Running; waited += 0.5f) yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(0.5f);
        Apply();
    }

    public static void Destroy()
    {
        foreach (var marker in _markers.Values) if (marker != null) Object.Destroy(marker.gameObject);
        _markers.Clear();
        foreach (var label in _labels.Values) if (label != null) Object.Destroy(label.gameObject);
        _labels.Clear();
        if (_mapTitle != null) { Object.Destroy(_mapTitle.gameObject); _mapTitle = null; }
        if (_line != null) { Object.Destroy(_line.gameObject); _line = null; }
        if (_backdrop != null) { Object.Destroy(_backdrop.gameObject); _backdrop = null; }
        if (_camera != null) { Object.Destroy(_camera.gameObject); _camera = null; }
    }

    // Per frame: one colored disc per car above the route line.
    public static void Tick()
    {
        if (_camera == null) return;
        // The game swaps cameras (prop cams, follow, free); none of them may draw our layer.
        foreach (var camera in Camera.allCameras) if (camera != _camera && (camera.cullingMask & (1 << Layer)) != 0) camera.cullingMask &= ~(1 << Layer);
        var alive = new HashSet<Vehicle>();
        var leader = Cam.Leader();
        float lift = _bounds.max.y + 40f;
        ShowMapTitle(lift);
        foreach (var vehicle in Game.Vehicles())
        {
            if (vehicle.Car() == null) continue;
            alive.Add(vehicle);
            if (!_markers.TryGetValue(vehicle, out var marker) || marker == null) _markers[vehicle] = marker = MakeMarker(vehicle);
            float size = _halfHeight * 2f * Current.marker / 100f * (Current.leaderBig && vehicle == leader ? 1.6f : 1f) * (vehicle.HasFinished() ? 0.6f : 1f);
            var position = vehicle.Car().transform.position;
            marker.position = new Vector3(position.x, lift + (vehicle == leader ? 1f : 0f), position.z);
            marker.localScale = new Vector3(size, size, size);
        }
        foreach (var gone in _markers.Keys.Where(vehicle => !alive.Contains(vehicle)).ToList()) { if (_markers[gone] != null) Object.Destroy(_markers[gone].gameObject); _markers.Remove(gone); }
        Labels(alive, lift);
    }

    // Names sit to the right of their dot and stay there: no flipping at the edge, no pushing apart. The view is sized
    // with room on the right (Apply), so a label near the edge still fits.
    static void Labels(HashSet<Vehicle> alive, float lift)
    {
        if (!Current.names) { if (_labels.Count > 0) { foreach (var label in _labels.Values) if (label != null) Object.Destroy(label.gameObject); _labels.Clear(); } return; }
        float size = _halfHeight * 2f * Current.marker / 100f;      // world units per dot
        float gap = size * 0.7f;
        foreach (var vehicle in alive)
        {
            if (!_labels.TryGetValue(vehicle, out var label) || label == null) _labels[vehicle] = label = MakeLabel(vehicle);
            var position = vehicle.Car().transform.position;
            label.text = vehicle.Profile().DisplayName() ?? Game.Login(vehicle);
            label.color = vehicle.Profile().Color();
            label.anchor = TextAnchor.MiddleLeft;
            label.alignment = TextAlignment.Left;
            label.characterSize = size * 0.32f;
            label.transform.position = new Vector3(position.x + gap, lift + 2f, position.z);
        }
        foreach (var gone in _labels.Keys.Where(vehicle => !alive.Contains(vehicle)).ToList()) { if (_labels[gone] != null) Object.Destroy(_labels[gone].gameObject); _labels.Remove(gone); }
    }

    // The map's name and author, centred just above the map box (same idea as the browser page).
    static void ShowMapTitle(float lift)
    {
        if (!Current.mapTitle)
        {
            if (_mapTitle != null) { Object.Destroy(_mapTitle.gameObject); _mapTitle = null; }
            return;
        }
        var game = Instances.GameController?.CurrentGame();
        string name = game?.MapName();
        if (string.IsNullOrEmpty(name)) return;
        string creator = Game.MapCreator(game.MapId());
        if (_mapTitle == null)
        {
            _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var titleObject = new GameObject("SRMinimap.MapTitle") { layer = Layer };
            titleObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapTitle = titleObject.AddComponent<TextMesh>();
            _mapTitle.font = _font; _mapTitle.fontSize = 48; _mapTitle.fontStyle = FontStyle.Bold;
            _mapTitle.anchor = TextAnchor.LowerCenter; _mapTitle.alignment = TextAlignment.Center;
            var renderer = titleObject.GetComponent<MeshRenderer>();
            renderer.material = _font.material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        float size = _halfHeight * 2f * Current.marker / 100f;
        _mapTitle.text = string.IsNullOrEmpty(creator) ? name : name + "  ·  by " + creator;
        _mapTitle.characterSize = size * 0.34f;
        _mapTitle.color = Parse(Current.track, 1f, Color.white);
        _mapTitle.transform.position = new Vector3(_bounds.center.x + _centerOffsetX, lift + 2f, _camera.transform.position.z + _halfHeight * 0.965f);
    }

    static TextMesh MakeLabel(Vehicle vehicle)
    {
        _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var labelObject = new GameObject("SRMinimap.Label." + Game.Login(vehicle)) { layer = Layer };
        labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var label = labelObject.AddComponent<TextMesh>();
        label.font = _font; label.fontSize = 48; label.anchor = TextAnchor.MiddleLeft; label.alignment = TextAlignment.Left;
        label.color = Color.white; label.fontStyle = FontStyle.Bold;
        var renderer = labelObject.GetComponent<MeshRenderer>();
        renderer.material = _font.material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        return label;
    }

    static Transform MakeMarker(Vehicle vehicle)
    {
        var markerObject = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(markerObject.GetComponent<Collider>());
        markerObject.name = "SRMinimap." + Game.Login(vehicle); markerObject.layer = Layer;
        markerObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var renderer = markerObject.GetComponent<Renderer>();
        renderer.material = new Material(BaseMaterial()) { color = vehicle.Profile().Color(), mainTexture = Disc() };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        return markerObject.transform;
    }

    static Texture2D _disc;
    static Texture2D Disc()
    {
        if (_disc != null) return _disc;
        const int size = 64; _disc = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
            _disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - distance) * 8f)));
        }
        _disc.Apply();
        return _disc;
    }
}
