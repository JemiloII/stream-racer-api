// Mini map. In game the map is the game's own (MinimapView, Oct 2026 build); we only show or hide it (`enabled`).
// The /minimap browser source draws the game's road picture (GET /minimap/track.png) and places the dots with the
// game's own projection, so both maps line up. The look settings below are the browser page's.
using Newtonsoft.Json.Linq;

namespace StreamRacerApi;

static class Minimap
{
    public class Cfg
    {
        public bool enabled = true;      // the game's in-race mini map; the /minimap browser source always works
        public float marker = 3f;        // dot diameter as % of the map's height
        public string bg = "#000000"; public float alpha = 0f;
        public string track = "#ffffff"; // route line colour when the game has no picture yet, and the title colour
        public float pad = 1.15f;        // margin around the route line (the game's picture brings its own)
        public bool leaderBig = true;
        public bool names = true;        // labels to the right of the dots
        public bool mapTitle = true;     // the map's name above the map
        public bool mapAuthor = false;   // and who built it, on a second line
        public string mapTitleAlign = "center", mapAuthorAlign = "center";   // left | center | right
        public string aspect = "1:1";    // /minimap page: fills the window, keeps this ratio (16:9, 4:3, 1:1, 21:9, auto = track)
    }
    public static Cfg Current = new();

    public static void Configure(JObject config) => Current = config?.ToObject<Cfg>() ?? new Cfg();

    public static object State => new
    {
        Current.enabled, Current.marker, Current.bg, Current.alpha, Current.track, Current.pad, Current.leaderBig, Current.names,
        Current.mapTitle, Current.mapAuthor, Current.mapTitleAlign, Current.mapAuthorAlign, Current.aspect,
        live = Instances.Minimap != null && Instances.Minimap.gameObject.activeSelf,
    };

    // Per frame: keep the game's map shown or hidden as configured (the game re-creates it per map).
    public static void Tick()
    {
        var view = Instances.Minimap;
        if (view != null && view.gameObject.activeSelf != Current.enabled) view.gameObject.SetActive(Current.enabled);
    }

    // A racer's colour changed mid-race: the game's map picks dot colours when it binds the cars, so bind again.
    public static void Recolor()
    {
        if (Game.Running && Instances.Minimap != null) Instances.Minimap.BindVehicles(Game.Vehicles().ToList());
    }

    // "!race show" / "!show": the browser page pops that racer's name for this long (the `show` event).
    public const float RevealSeconds = 5f;

    // The game's projection for GET /track: picture u = 0.5 + (x - cx) / ex, v = 0.5 + (z - cz) / ez (v up).
    public static object GameMap()
    {
        var capture = Instances.Minimap?.Capture();
        var picture = capture?.Picture();
        if (picture == null) return null;
        var centre = capture.Centre(); var extent = capture.Extent();
        return new { cx = centre.x, cz = centre.z, ex = extent.x, ez = extent.y, image = "/minimap/track.png?v=" + picture.GetInstanceID() };
    }

    // The game's road picture as PNG. Read back once per capture (main thread only).
    static byte[] _png; static int _pngOf;
    public static byte[] TrackPng()
    {
        var picture = Instances.Minimap?.Capture()?.Picture();
        if (picture == null) return null;
        if (_png != null && _pngOf == picture.GetInstanceID()) return _png;
        var previous = RenderTexture.active;
        var texture = new Texture2D(picture.width, picture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = picture;
            texture.ReadPixels(new Rect(0, 0, picture.width, picture.height), 0, 0);
            texture.Apply();
            _png = texture.EncodeToPNG(); _pngOf = picture.GetInstanceID();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.Destroy(texture);
        }
        return _png;
    }
}
