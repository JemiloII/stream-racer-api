// Game.Resize: shrink and grow cars for a while. Below 1 = the game's own shrink ray (Vehicle.ShrinkFor, which
// restores itself); above 1 = the same shape done here, because the game clamps its scale at 1. Both post a line in
// the game's event feed (where the booms' RIP lines go) with the viewer who caused it.
using System.Collections;

namespace StreamRacerApi;

static partial class Game
{
    public const float ShrinkScale = 0.55f, GrowScale = 1.6f, ResizeSeconds = 12f;   // the game's shrink ray: 0.55 for 12 s
    static readonly HashSet<Vehicle> Growing = new();

    // Cars that can take an effect right now: on the road and not finished.
    static List<Vehicle> Resizable() => Vehicles().Where(vehicle => vehicle.Car() != null && !vehicle.HasFinished()).ToList();

    // target: a login, "all", or a number = that many random cars (like the game's shrink ray, which takes 10).
    // except (id or login) is left out of "all" and the random pick: a redeemer never hits their own car.
    public static List<Vehicle> ResizeTargets(string target, float scale, float seconds, string except = null)
    {
        if (!Running) return new List<Vehicle>();
        var pool = Resizable().Where(vehicle => except == null || (vehicle.Profile().TwitchId() != except && Login(vehicle) != except.ToLowerInvariant())).ToList();
        List<Vehicle> cars;
        if (target == "all")
        {
            cars = pool;
        }
        else if (int.TryParse(target, out var count))
        {
            cars = pool.OrderBy(_ => Random.value).Take(count).ToList();
        }
        else
        {
            var vehicle = Find(target);
            cars = vehicle != null && Resizable().Contains(vehicle) ? new List<Vehicle> { vehicle } : new List<Vehicle>();
        }
        return cars.Where(vehicle => Resize(vehicle, scale, seconds)).ToList();
    }

    static bool Resize(Vehicle vehicle, float scale, float seconds)
    {
        if (scale <= 1f)
        {
            vehicle.ShrinkFor(scale, seconds, pulse: true);
            return true;
        }
        if (Growing.Contains(vehicle)) return false;
        Plugin.Instance.StartCoroutine(Grow(vehicle, vehicle.Car().transform, scale, seconds));
        return true;
    }

    // The game's shape: flicker between sizes for 0.9 s, hold, ease back over the last 0.3 s.
    static IEnumerator Grow(Vehicle vehicle, Transform car, float scale, float seconds)
    {
        Growing.Add(vehicle);
        var normal = car.localScale;
        var big = normal * scale;
        for (float elapsed = 0f; elapsed < 0.9f && car != null; elapsed += Time.deltaTime)
        {
            car.localScale = Mathf.Min(4, Mathf.FloorToInt(elapsed / 0.18f)) % 2 == 0 ? big : normal;
            yield return null;
        }
        for (float elapsed = 0f; elapsed < seconds && car != null; elapsed += Time.deltaTime)
        {
            car.localScale = Vector3.Lerp(big, normal, Mathf.InverseLerp(seconds - 0.3f, seconds, elapsed));
            yield return null;
        }
        if (car != null) car.localScale = normal;
        Growing.Remove(vehicle);
    }

    // One line in the game's event feed, where the booms' "RIP name · by viewer" lines go, gone after 5 s like theirs.
    // One car: its name, and clicking the line follows it.
    public static void AnnounceResize(bool grow, List<Vehicle> cars, string by)
    {
        if (Instances.Events == null || cars.Count == 0) return;
        bool one = cars.Count == 1;
        string what = one ? cars[0].Profile().DisplayName() : cars.Count + " cars";
        FeedClick follow = one ? () => Instances.CamController?.FollowVehicle(cars[0]) : null;
        string byPart = string.IsNullOrWhiteSpace(by) ? "" : " · by " + by;
        var line = Instances.Events.AddEvent((grow ? "GROW RAY · " : "SHRINK RAY · ") + what + byPart, follow);
        // the line is the boom template (Oct 10 build): a bomb icon on the left, then one line of text. Ours swaps
        // the bomb for a word in the same spot.
        IconWord(line, grow ? "HUGE" : "TINY");
        Plugin.Instance.StartCoroutine(RemoveFeedLine(line, 5f));
        Cage.StreamRacer.UI.ChaosEffectStatus.GetOrCreate()?.PlayShrinkSound();
    }

    // Hide the line's bomb icon and put a short bold word where it was, in the line's own font.
    static void IconWord(Component line, string word)
    {
        var icon = line != null ? line.transform.Find("Main/Image/Bomb icon") : null;
        var font = line != null ? line.transform.Find("Main/Content/Top/Text")?.GetComponent<TMPro.TextMeshProUGUI>() : null;
        if (icon == null || font == null)
        {
            return;
        }
        icon.gameObject.SetActive(false);
        var label = Object.Instantiate(font.gameObject, icon.parent, false).GetComponent<TMPro.TextMeshProUGUI>();
        label.name = "SRApiIconWord";
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        label.text = word;
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = font.fontSize;
        label.color = Color.white;
    }

    static void SetFeedText(Component line, string path, string text)
    {
        var label = line != null ? line.transform.Find(path)?.GetComponent<TMPro.TMP_Text>() : null;
        if (label != null) label.text = text;
    }

    static IEnumerator RemoveFeedLine(Cage.StreamRacer.Events.GameEvent line, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (line != null) line.Remove();
    }
}
