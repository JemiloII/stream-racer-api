// Game.LeaderboardExtras: the in-game leaderboard rows only carry a background and the name, so we add two things of
// our own to each row and keep them fed: the racer's picture (custom image or Twitch avatar) on the left, and their
// completion percent on the right. Both are switchable (settings.leaderboardAvatars / leaderboardPercent).
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using TMPro;
using UnityEngine.UI;

namespace StreamRacerApi;

static partial class Game
{
    const string AvatarName = "SRApiAvatar", PercentName = "SRApiPercent", ColorBarName = "SRApiColorBar";
    static readonly Dictionary<string, Sprite> _avatarSprites = new();   // login -> picture, loaded once per source
    static readonly HashSet<string> _avatarLoading = new();
    static readonly Dictionary<string, string> _avatarSource = new();    // login -> what we loaded, so a change reloads

    /// The row's picture and percent, created on first sight and updated on every tick.
    public static void DecorateLeaderboardRow(VehicleLeaderboardUIItem item, Vehicle vehicle)
    {
        // The component also sits on the place numbers and the finish backgrounds: only the real rows get decorated.
        if (item.transform.parent == null || item.transform.parent.name != "ListItems") return;
        var nameText = item.NameText(); if (nameText == null) return;
        bool wantAvatar = Settings.Current.leaderboardAvatars, wantPercent = Settings.Current.leaderboardPercent;
        var row = (RectTransform)item.transform;

        // long names shrink to fit the narrower column instead of being cut off ("TTS-chan", not "TTS-cha")
        if (!nameText.enableAutoSizing && (wantAvatar || wantPercent))
        {
            nameText.fontSizeMax = nameText.fontSize;
            nameText.fontSizeMin = nameText.fontSize * 0.55f;
            nameText.enableAutoSizing = true;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        var avatar = row.Find(AvatarName) as RectTransform;
        if (wantAvatar && avatar == null) avatar = MakeAvatar(row, (RectTransform)nameText.transform);
        if (avatar != null)
        {
            avatar.gameObject.SetActive(wantAvatar && vehicle != null);
            if (wantAvatar && vehicle != null) FeedAvatar(avatar.Find("Picture")?.GetComponent<Image>(), Login(vehicle));
        }

        var colorBar = row.Find(ColorBarName) as RectTransform;
        if (colorBar == null) colorBar = MakeColorBar(row);
        colorBar.gameObject.SetActive(vehicle != null && Settings.Current.colorLeaderboard);
        if (vehicle != null) colorBar.GetComponent<Image>().color = vehicle.Profile().Color();

        var percent = row.Find(PercentName) as RectTransform;
        if (wantPercent && percent == null) percent = MakePercent(row, nameText);
        if (percent != null)
        {
            percent.gameObject.SetActive(wantPercent && vehicle != null);
            if (wantPercent && vehicle != null)
            {
                var label = percent.GetComponent<TextMeshProUGUI>();
                label.text = vehicle.HasFinished() ? "FIN" : Mathf.RoundToInt(ProgressPercent(vehicle)) + "%";
                label.color = vehicle.HasFinished() ? new Color(0.24f, 0.86f, 0.52f) : new Color(1f, 1f, 1f, 0.75f);
            }
        }
    }

    // A round picture at the left of the row; the name is pushed right to make room.
    static RectTransform MakeAvatar(RectTransform row, RectTransform nameText)
    {
        float height = row.rect.height, size = Mathf.Min(height - 12f, 44f);
        var holder = new GameObject(AvatarName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)holder.transform;
        rect.SetParent(row, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = new Vector2(14f, 0f);   // just clear of the colour bar and the place number
        var holderImage = holder.GetComponent<Image>();
        holderImage.sprite = DiscSprite(); holderImage.type = Image.Type.Simple; holderImage.raycastTarget = false;
        holder.AddComponent<Mask>().showMaskGraphic = false;   // the disc masks the picture into a circle
        var picture = new GameObject("Picture", typeof(RectTransform), typeof(Image));
        var pictureRect = (RectTransform)picture.transform;
        pictureRect.SetParent(rect, false);
        pictureRect.anchorMin = Vector2.zero; pictureRect.anchorMax = Vector2.one; pictureRect.offsetMin = pictureRect.offsetMax = Vector2.zero;
        var image = picture.GetComponent<Image>();
        image.preserveAspect = true; image.raycastTarget = false; image.enabled = false;
        // the name starts after the picture (the offset is applied once; a second pass would keep pushing it)
        nameText.offsetMin = new Vector2(Mathf.Max(nameText.offsetMin.x, size + 26f), nameText.offsetMin.y);   // and the name clear of the picture
        return rect;
    }

    // A thin bar down the far left of the whole row in the racer's colour, full height, like the browser leaderboard.
    // The place numbers sit in their own column left of the row, so the bar is pushed out past them to the panel edge.
    static RectTransform MakeColorBar(RectTransform row)
    {
        var bar = new GameObject(ColorBarName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)bar.transform;
        rect.SetParent(row, false);
        rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(5f, 0f);   // full row height, no margin
        rect.anchoredPosition = new Vector2(-NumberColumnWidth(row), 0f);
        bar.GetComponent<Image>().raycastTarget = false;
        return rect;
    }

    /// How far the place-number column reaches left of a row: its width plus the gap the layout puts between them.
    static float NumberColumnWidth(RectTransform row)
    {
        var content = row.parent != null ? row.parent.parent : null;   // ListItems -> Content
        var numbers = content != null ? content.Find("ListNums") as RectTransform : null;
        if (numbers == null || numbers.childCount == 0) return 0f;
        float width = ((RectTransform)numbers.GetChild(0)).rect.width;
        var layout = content.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
        return width + (layout != null ? layout.spacing : 0f);
    }

    // The percent at the right, in the same font as the name.
    static RectTransform MakePercent(RectTransform row, TextMeshProUGUI nameText)
    {
        var clone = Object.Instantiate(nameText.gameObject, row);
        clone.name = PercentName;
        foreach (var extra in clone.GetComponents<Component>())
            if (!(extra is RectTransform) && !(extra is CanvasRenderer) && !(extra is TextMeshProUGUI)) Object.Destroy(extra);
        var rect = (RectTransform)clone.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(62f, row.rect.height);
        rect.anchoredPosition = new Vector2(-12f, 0f);
        var label = clone.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.fontSize = Mathf.Max(14f, nameText.fontSize * 0.8f);
        label.enableAutoSizing = false; label.richText = false; label.raycastTarget = false;
        // the name stops before the percent
        var nameRect = (RectTransform)nameText.transform;
        nameRect.offsetMax = new Vector2(Mathf.Min(nameRect.offsetMax.x, -66f), nameRect.offsetMax.y);
        return rect;
    }

    static void FeedAvatar(Image image, string login)
    {
        if (image == null || string.IsNullOrEmpty(login)) return;
        string source = ImageSource(login) ?? (Avatars.TryGetValue(login, out var url) ? url : null);
        if (source == null) { EnsureAvatars(); return; }
        if (_avatarSprites.TryGetValue(login, out var sprite) && _avatarSource.TryGetValue(login, out var loaded) && loaded == source)
        {
            if (sprite != null) { image.sprite = sprite; image.enabled = true; }
            return;
        }
        if (!_avatarLoading.Add(login)) return;
        _avatarSource[login] = source;
        Plugin.Instance.StartCoroutine(LoadAvatarSprite(login, source));
    }

    static IEnumerator LoadAvatarSprite(string login, string source)
    {
        byte[] bytes = null; var done = new ManualResetEventSlim();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (File.Exists(source)) bytes = File.ReadAllBytes(source);
                else { var web = new WebClient(); web.Headers["User-Agent"] = "StreamRacerApi"; bytes = web.DownloadData(source); }
            }
            catch (System.Exception error) { Plugin.Log.LogWarning($"leaderboard avatar failed for {login}: {error.Message}"); }
            done.Set();
        });
        while (!done.IsSet) yield return null;
        _avatarLoading.Remove(login);
        if (bytes == null) { _avatarSprites[login] = null; yield break; }
        var texture = new Texture2D(2, 2);
        if (!texture.LoadImage(bytes)) { _avatarSprites[login] = null; yield break; }
        _avatarSprites[login] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    // A white circle we can use as a mask so the pictures are round like everywhere else.
    static Sprite _disc;
    static Sprite DiscSprite()
    {
        if (_disc != null) return _disc;
        const int size = 128; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size / 2f - 1f, centre = size / 2f - 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
            }
        texture.Apply();
        return _disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    public static void ForgetLeaderboardAvatars() { _avatarSprites.Clear(); _avatarSource.Clear(); }
}
