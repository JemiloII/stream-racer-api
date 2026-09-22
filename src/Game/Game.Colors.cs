// Game.Colors: a racer's color. Parsing (hex / named), setting it live on the car label and remembering it in settings,
// re-applying saved colors on join, and painting names in the in-game leaderboard and the lobby/results rows.
using HarmonyLib;

namespace StreamRacerApi;

static partial class Game
{
    static readonly Dictionary<string, string> NamedColors = new()
    {
        ["pink"] = "#ff5fa2", ["hotpink"] = "#ff69b4", ["gold"] = "#ffd400", ["lime"] = "#3ddc84", ["sky"] = "#35e0ff", ["violet"] = "#b07cff",
        ["orange"] = "#ff8a00", ["turquoise"] = "#40e0d0", ["salmon"] = "#fa8072", ["coral"] = "#ff7f50", ["indigo"] = "#4b0082", ["mint"] = "#98ff98",
    };
    public static bool ParseColor(string text, out Color color)
    {
        color = Color.white; if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim().ToLowerInvariant();
        if (NamedColors.TryGetValue(value, out var hex)) value = hex;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-f]{6}$")) value = "#" + value;
        return ColorUtility.TryParseHtmlString(value, out color);
    }

    // Set a racer's color now (car label, leaderboard, overlays) and remember it for future joins.
    public static bool SetColor(string login, string text, bool persist = true)
    {
        if (login == null || !ParseColor(text, out var color)) return false;
        login = login.ToLowerInvariant();
        if (persist) { Settings.Current.colors[login] = "#" + ColorUtility.ToHtmlStringRGB(color); Settings.Persist(); }
        var vehicle = Find(login);
        if (vehicle != null) { vehicle.Profile().SetColor(color); RecolorLabel(vehicle, color); }
        if (!Running) foreach (var row in Object.FindObjectsOfType<PreGamePlayerListItem>()) // the lobby row is built once: repaint its name now
            if (row.Profile()?.Login() == login) ColorRowName(row.Profile(), row.NameText());
        return true;
    }

    static readonly System.Reflection.FieldInfo CarLabelObjectField = AccessTools.Field(typeof(CarLabel), GameNames.CarLabelObject);
    static void RecolorLabel(Vehicle vehicle, Color color)
    {
        var label = vehicle.Car()?.GetComponent<CarLabel>();
        if (label == null) return;
        // the visible label is a separate object the game spawns carrying a VehicleLabel
        var labelObject = CarLabelObjectField?.GetValue(label) as GameObject;
        var vehicleLabel = labelObject?.GetComponent<VehicleLabel>();
        if (vehicleLabel?.NameText() != null) vehicleLabel.NameText().color = color;
        if (labelObject != null) foreach (var text in labelObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) text.color = color;
    }

    // Apply a remembered color when someone joins (runs from the AddVehicle prefix and postfix).
    public static void ApplySavedColor(Vehicle vehicle)
    {
        if (Settings.Current.colors.TryGetValue(Login(vehicle) ?? "", out var hex) && ParseColor(hex, out var color)) vehicle.Profile().SetColor(color);
    }

    // In-game leaderboard: paint each row's name in the car's color (or back to white when turned off).
    static readonly System.Reflection.FieldInfo LeaderboardItemVehicle = AccessTools.Field(typeof(VehicleLeaderboardUIItem), GameNames.LeaderboardItemVehicle);
    static VehicleLeaderboardUIItem[] _leaderboardItems = new VehicleLeaderboardUIItem[0]; static float _leaderboardScanned = -10f, _leaderboardNext;
    public static void ColorLeaderboard()
    {
        if (Time.unscaledTime < _leaderboardNext) return; _leaderboardNext = Time.unscaledTime + 0.25f;
        if (!Running) return;
        if (Time.unscaledTime - _leaderboardScanned > 2f) { _leaderboardItems = Object.FindObjectsOfType<VehicleLeaderboardUIItem>(); _leaderboardScanned = Time.unscaledTime; }
        bool enabled = Settings.Current.colorLeaderboard;
        foreach (var item in _leaderboardItems)
        {
            if (item == null || item.NameText() == null) continue;
            var vehicle = LeaderboardItemVehicle?.GetValue(item) as Vehicle;
            item.NameText().color = enabled && vehicle != null && MayShowColoredName(vehicle) ? vehicle.Profile().Color() : Color.white;
        }
    }

    // Lobby / results row: the name in the car's color (same switch and tier rule as the leaderboard). Runs from the list-item postfix.
    public static void ColorRowName(RacerProfile profile, TMPro.TextMeshProUGUI nameText)
    {
        if (profile == null || nameText == null) return;
        var vehicle = Find(profile.Login());
        bool enabled = Settings.Current.colorLeaderboard && Pure.TierAllows(Settings.Current.perks.coloredNames,
            IsFollower(profile.Login()), profile.IsSubscriber() || (vehicle != null && IsSub(vehicle)), vehicle != null && IsDev(vehicle), profile.Login() != null && profile.Login() == StreamerLogin);
        nameText.color = enabled ? profile.Color() : Color.white;
    }
}
