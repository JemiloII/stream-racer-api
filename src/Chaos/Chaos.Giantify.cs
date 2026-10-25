// Chaos.Giantify: Twitch's "Gigantify an Emote" bits reward, played in the race. The chat line carries the IRC tag
// msg-id=gigantified-emote-message; its last emote is the giant one. Booms random cars (one per 10 bits, never in
// Ranked), paints the emote on the gifter's car (roof + both sides, replaced by their next one) and as decals on the
// road (start, behind their car, near the finish; never overlapping). Emotes come from Twitch's CDN, cached per id:
// PNG for static ones, GIF (src/Gif.cs) for animated ones, played at their frame delays. Everything lives under
// the chaos root or is undone by Clear.
using System.Collections;
using System.Net;
using System.Threading;
using TwitchLib.Client.Models;
using UnityEngine.Rendering;

namespace StreamRacerApi;

static partial class Chaos
{
    const string GiantifyMessageId = "gigantified-emote-message";
    const string EmoteUrl = "https://static-cdn.jtvnw.net/emoticons/v2/{0}/default/dark/3.0";
    const int BitsPerBoom = 10;
    const float BoomGapSeconds = 0.4f;
    const float DecalSize = 4.5f;            // ground emote width (m)
    const float DecalLift = 0.06f;           // above the ground, so it never z-fights the road
    const float StartMeters = 6f;            // the start decal: this far along the route from its start
    const float BehindCarMeters = 7f;        // the gifter's decal: this far behind their car
    const float FinishBackMeters = 25f;      // the finish decal: this far before the finish line
    const float CarSurfaceGap = 0.03f;       // car emotes float this far off the body
    const string CarEmoteName = "giant-emote";

    // Where a new decal tries to go when its spot is taken, in decal widths (along the road, across it).
    static readonly Vector2[] DecalSlides =
    {
        new(0f, 0f),
        new(1f, 0f),
        new(-1f, 0f),
        new(0f, 1f),
        new(0f, -1f),
        new(2f, 0f),
        new(-2f, 0f),
    };

    // One emote ready to draw: its frames (one for PNG), how long each shows, and the material every quad of it
    // shares (so animating it animates them all).
    sealed class EmoteArt
    {
        public Texture2D[] Frames;
        public float[] Seconds;
        public Material Material;
        public float Aspect;   // width / height
    }

    // A download in flight; the worker thread fills it, the main thread turns it into textures.
    sealed class EmoteDownload
    {
        public volatile bool Done;
        public byte[] Bytes;
        public GifImage Animation;
    }

    sealed class GiantRequest
    {
        public string Login;
        public string DisplayName;
        public string EmoteId;
        public string EmoteName;
    }

    static readonly Dictionary<string, EmoteArt> _emoteArt = new();            // by emote id, kept across races
    static readonly Dictionary<string, EmoteDownload> _emoteDownloads = new();
    static readonly Dictionary<string, GameObject> _carEmotes = new();          // by login: the holder on their car
    static readonly List<(Vector3 centre, float radius)> _decals = new();
    static readonly HashSet<string> _animating = new();
    static bool _giantifyHooked;
    static bool _downloadWarned;

    // ---- chat ----

    // Every chat line (Patches.OnChat). May run off the main thread: only strings here, the rest is queued.
    public static void OnChat(ChatMessage message)
    {
        if (message == null || !Settings.Current.chaos.giantify || !IsGiantified(message.RawIrcMessage))
        {
            return;
        }
        var emote = message.EmoteSet?.Emotes?.OrderByDescending(candidate => candidate.StartIndex).FirstOrDefault();
        if (emote == null || string.IsNullOrEmpty(emote.Id))
        {
            return;
        }
        var request = new GiantRequest
        {
            Login = (message.Username ?? "").ToLowerInvariant(),
            DisplayName = string.IsNullOrEmpty(message.DisplayName) ? message.Username : message.DisplayName,
            EmoteId = emote.Id,
            EmoteName = emote.Name,
        };
        Plugin.RunOnMain(() => Guard("giantify", () => BeginGiantify(request)));
    }

    // The IRC tags are "@key=value;key=value ..." up to the first space.
    static bool IsGiantified(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw[0] != '@')
        {
            return false;
        }
        int space = raw.IndexOf(' ');
        string tags = space > 0 ? raw.Substring(1, space - 1) : raw.Substring(1);
        return tags.Split(';').Contains("msg-id=" + GiantifyMessageId);
    }

    static void BeginGiantify(GiantRequest request)
    {
        if (Game.Running && Settings.Current.chaos.giantify)
        {
            Spawn(Giantify(request));
        }
    }

    // ---- the event ----

    static IEnumerator Giantify(GiantRequest request)
    {
        if (!_emoteArt.ContainsKey(request.EmoteId))
        {
            yield return LoadEmote(request.EmoteId);
        }
        if (!Game.Running)
        {
            yield break;
        }
        int booms = !Game.ChaosAllowed ? 0 : Mathf.Max(1, Settings.Current.chaos.giantifyBits / BitsPerBoom);
        Guard("giantify paint", () => Paint(request));
        Announce("GIANT " + request.EmoteName + " · " + request.DisplayName, "GIANT");
        Plugin.Emit("giantify", new
        {
            login = request.Login,
            displayName = request.DisplayName,
            emote = request.EmoteName,
            id = request.EmoteId,
            booms,
        });
        string by = "giant " + request.EmoteName + " · " + request.DisplayName;
        for (int boom = 0; boom < booms && Game.Running; boom++)
        {
            Guard("giantify boom", () => BoomRandomRacer(by));
            yield return new WaitForSeconds(BoomGapSeconds);
        }
    }

    static void BoomRandomRacer(string by)
    {
        var target = RandomRacer();
        if (target != null)
        {
            Game.StrikeNow(target, by);
        }
    }

    // Painting is cosmetic, so Ranked keeps it. No art (download failed) = nothing painted.
    static void Paint(GiantRequest request)
    {
        if (!_emoteArt.TryGetValue(request.EmoteId, out var art))
        {
            return;
        }
        HookGiantifyClear();
        StartAnimating(request.EmoteId, art);
        var vehicle = Game.Find(request.Login);
        if (vehicle?.Car() != null)
        {
            PaintCar(request.Login, vehicle.Car().transform, art);
            var heading = Flat(vehicle.Car().transform.forward);
            PlaceDecal(art, CarPosition(vehicle) - heading * BehindCarMeters, heading);
        }
        if (RouteLength <= 0f)
        {
            return;
        }
        PlaceDecal(art, RoadAt(StartMeters), RoadDirectionAt(StartMeters));
        float finish = Game.FinishLine(out _, out _, out var finishDistance) && finishDistance > 0f
            ? finishDistance
            : RouteLength;
        float nearFinish = Mathf.Max(0f, finish - FinishBackMeters);
        PlaceDecal(art, RoadAt(nearFinish), RoadDirectionAt(nearFinish));
    }

    // ---- on the car ----

    // Roof + left + right, as children of the car; the gifter's previous emotes go.
    static void PaintCar(string login, Transform car, EmoteArt art)
    {
        var body = CarBounds(car);
        if (_carEmotes.TryGetValue(login, out var old) && old != null)
        {
            Object.Destroy(old);
        }
        var holder = Holder(CarEmoteName, car);
        _carEmotes[login] = holder.gameObject;
        float roof = Mathf.Min(body.size.x, body.size.z) * 0.85f;
        float side = Mathf.Min(body.size.y * 0.9f, body.size.z * 0.7f);
        var top = new Vector3(body.center.x, body.max.y + CarSurfaceGap, body.center.z);
        var left = new Vector3(body.min.x - CarSurfaceGap, body.center.y, body.center.z);
        var right = new Vector3(body.max.x + CarSurfaceGap, body.center.y, body.center.z);
        EmoteQuad(holder, art, top, new Vector3(90f, 0f, 0f), roof);     // faces up, image top toward the bonnet
        EmoteQuad(holder, art, left, new Vector3(0f, 90f, 0f), side);    // faces out of the left side
        EmoteQuad(holder, art, right, new Vector3(0f, -90f, 0f), side);  // faces out of the right side
    }

    // The car's body box in its own space, from every mesh under it (ours excluded); a sedan-sized guess if none.
    static Bounds CarBounds(Transform car)
    {
        Bounds? body = null;
        foreach (var filter in car.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.transform.parent?.name == CarEmoteName)
            {
                continue;
            }
            body = Encapsulate(body, car, filter.transform, filter.sharedMesh.bounds);
        }
        return body ?? new Bounds(new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.4f, 4.2f));
    }

    // `bounds` (in `part`'s space) added to `body` (in `car`'s space), corner by corner.
    static Bounds Encapsulate(Bounds? body, Transform car, Transform part, Bounds bounds)
    {
        var result = body ?? new Bounds(car.InverseTransformPoint(part.TransformPoint(bounds.center)), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3(
                (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
            result.Encapsulate(car.InverseTransformPoint(part.TransformPoint(bounds.center + offset)));
        }
        return result;
    }

    // ---- on the ground ----

    // A flat decal at the first free spot near `point` (sliding along / across the road); none if all are taken.
    static void PlaceDecal(EmoteArt art, Vector3 point, Vector3 forward)
    {
        forward = Flat(forward);
        var across = Vector3.Cross(Vector3.up, forward);
        float width = DecalSize * Mathf.Max(1f, art.Aspect);
        float radius = 0.5f * DecalSize * Mathf.Sqrt(art.Aspect * art.Aspect + 1f);
        foreach (var slide in DecalSlides)
        {
            var spot = point + forward * (slide.x * width) + across * (slide.y * width);
            if (!IsFree(spot, radius))
            {
                continue;
            }
            var ground = Ground(spot) + Vector3.up * DecalLift;
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            EmoteQuad(Root.transform, art, ground, new Vector3(90f, heading, 0f), DecalSize);
            _decals.Add((spot, radius));
            return;
        }
    }

    static bool IsFree(Vector3 spot, float radius) => _decals.All(decal =>
        new Vector2(decal.centre.x - spot.x, decal.centre.z - spot.z).magnitude >= decal.radius + radius);

    static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude < 1e-6f ? Vector3.forward : direction.normalized;
    }

    // ---- quads ----

    // An unlit, alpha-blended, two-faced quad (Sprites/Default culls nothing) `size` tall, in `parent`'s space.
    static GameObject EmoteQuad(Transform parent, EmoteArt art, Vector3 position, Vector3 euler, float size)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "giant-emote-quad";
        Object.DestroyImmediate(quad.GetComponent<Collider>());   // now, not at frame end: it may sit on a car body
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localRotation = Quaternion.Euler(euler);
        quad.transform.localScale = new Vector3(size * art.Aspect, size, 1f);
        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = art.Material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return quad;
    }

    // ---- loading ----

    static IEnumerator LoadEmote(string id)
    {
        if (!_emoteDownloads.TryGetValue(id, out var download))
        {
            download = StartDownload(id);
        }
        while (!download.Done)
        {
            yield return null;
        }
        _emoteDownloads.Remove(id);
        if (!_emoteArt.ContainsKey(id))
        {
            Guard("giantify art", () => StoreArt(id, download));
        }
    }

    // Off the main thread: the bytes, and the decoded GIF when it is one ("GIF8"; Twitch serves PNG otherwise).
    static EmoteDownload StartDownload(string id)
    {
        var download = new EmoteDownload();
        _emoteDownloads[id] = download;
        string url = string.Format(EmoteUrl, System.Uri.EscapeDataString(id));
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var web = new WebClient();
                web.Headers["User-Agent"] = "StreamRacerApi";
                download.Bytes = web.DownloadData(url);
                download.Animation = Gif.IsGif(download.Bytes) ? Gif.Decode(download.Bytes) : null;
            }
            catch (System.Exception error)
            {
                WarnDownload(url, error);
            }
            download.Done = true;
        });
        return download;
    }

    static void WarnDownload(string url, System.Exception error)
    {
        if (_downloadWarned)
        {
            return;
        }
        _downloadWarned = true;
        Plugin.Log.LogWarning("giantify: emote download failed (" + url + "): " + error.Message + " (further hidden)");
    }

    static void StoreArt(string id, EmoteDownload download)
    {
        if (download.Bytes == null)
        {
            return;
        }
        var art = download.Animation != null ? GifArt(download.Animation) : PngArt(download.Bytes);
        if (art == null)
        {
            return;
        }
        art.Material = new Material(Shader.Find("Sprites/Default"))
        {
            mainTexture = art.Frames[0],
        };
        _emoteArt[id] = art;
    }

    static EmoteArt PngArt(byte[] bytes)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes, true))
        {
            Object.Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        return new EmoteArt
        {
            Frames = new[] { texture },
            Seconds = new[] { 1f },
            Aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f,
        };
    }

    // One texture per frame; GIF rows run top-down, Unity's bottom-up.
    static EmoteArt GifArt(GifImage animation)
    {
        if (animation.Width <= 0 || animation.Height <= 0)
        {
            return null;
        }
        int rowBytes = animation.Width * 4;
        var frames = new Texture2D[animation.Frames.Count];
        for (int index = 0; index < frames.Length; index++)
        {
            var flipped = new byte[animation.Frames[index].Rgba.Length];
            for (int row = 0; row < animation.Height; row++)
            {
                System.Array.Copy(animation.Frames[index].Rgba, row * rowBytes, flipped,
                    (animation.Height - 1 - row) * rowBytes, rowBytes);
            }
            frames[index] = new Texture2D(animation.Width, animation.Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
            };
            frames[index].LoadRawTextureData(flipped);
            frames[index].Apply(false, true);
        }
        return new EmoteArt
        {
            Frames = frames,
            Seconds = animation.Frames.Select(frame => Gif.PlaybackDelay(frame.DelayMs) / 1000f).ToArray(),
            Aspect = (float)animation.Width / animation.Height,
        };
    }

    // ---- animation ----

    // One loop per animated emote on the chaos root: it swaps the shared material's texture, so every quad follows.
    static void StartAnimating(string id, EmoteArt art)
    {
        if (art.Frames.Length > 1 && _animating.Add(id))
        {
            Spawn(Animate(art));
        }
    }

    static IEnumerator Animate(EmoteArt art)
    {
        int frame = 0;
        float next = Time.time + art.Seconds[0];
        while (art.Material != null)
        {
            while (Time.time >= next)
            {
                frame = (frame + 1) % art.Frames.Length;
                next = Mathf.Max(next + art.Seconds[frame], Time.time - 1f);   // a long hitch skips, never spins
            }
            art.Material.mainTexture = art.Frames[frame];
            yield return null;
        }
    }

    // ---- clear ----

    // Chaos.Clear: car emotes go too (they hang off cars, not the root), the decal list and loops reset. Ground
    // decals and the animation loops die with the root on their own.
    static void HookGiantifyClear()
    {
        if (_giantifyHooked)
        {
            return;
        }
        _giantifyHooked = true;
        OnClear(ClearGiantify);
    }

    static void ClearGiantify()
    {
        foreach (var holder in _carEmotes.Values.Where(holder => holder != null))
        {
            Object.Destroy(holder);
        }
        _carEmotes.Clear();
        _decals.Clear();
        _animating.Clear();
        _giantifyHooked = false;
    }
}
