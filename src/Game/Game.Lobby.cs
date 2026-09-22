// Game.Lobby: joining and the race lifecycle. Join / JoinStreamer, the title announce after a join, the map list and
// lobby creation, the screen state, start / end / next race.
using System.Collections;
using HarmonyLib;

namespace StreamRacerApi;

static partial class Game
{
    // ---- joining ----

    public static Color AutoColor(string login) { ColorUtility.TryParseHtmlString(Pure.AutoColorHex(login), out var color); return color; }

    public static bool Join(string id, string login, string displayName, string colorHex, bool subscriber, string image = null)
    {
        if (!string.IsNullOrWhiteSpace(image)) Images[login] = image; else Images.Remove(login);
        if (Running || Ended || Instances.VehicleManager == null) return false;
        if (Instances.VehicleManager.HasPlayerAlreadyJoined(login)) return false;
        if (!ColorUtility.TryParseHtmlString(colorHex ?? "", out var color)) color = AutoColor(login);
        var profile = RacerProfileExtensions.NewProfile(id, login, string.IsNullOrEmpty(displayName) ? login : displayName, color, subscriber);
        Instances.VehicleManager.AddVehicle(VehicleExtensions.NewVehicle(profile));
        return Instances.VehicleManager.HasPlayerAlreadyJoined(login);
    }

    // What the lobby's JOIN GAME button does: adds the logged-in streamer with the STREAMER title.
    public static bool JoinStreamer(string colorHex = null)
    {
        if (Running || Instances.InGameScreen == null || StreamerLogin == null) return false;
        if (Instances.VehicleManager.HasPlayerAlreadyJoined(StreamerLogin)) return false;
        Instances.InGameScreen.JoinGameClicked();
        var me = Find(StreamerLogin);
        string wanted = string.IsNullOrEmpty(colorHex) ? Settings.Current.streamerColor ?? "" : colorHex;
        if (me != null && ColorUtility.TryParseHtmlString(wanted, out var color)) me.Profile().SetColor(color);
        return me != null;
    }

    // The title arrives asynchronously from the game's backend after a join; wait for it, then announce devs.
    public static IEnumerator AnnounceTitle(Vehicle vehicle)
    {
        for (float waited = 0; waited < 8f && string.IsNullOrEmpty(vehicle.Profile().CustomTitle()) && vehicle.Profile().BackendId() == 0; waited += 0.25f)
            yield return new WaitForSeconds(0.25f);
        if (!Vehicles().Contains(vehicle)) yield break;
        if (IsDev(vehicle)) Plugin.Emit("developer", EventDto(vehicle));
        GrantPerks(vehicle);
    }

    // ---- maps and lobby creation ----

    public static List<MapOverviewListItem> Maps = new();
    static bool _mapsLoading;

    public static void FetchMaps(System.Action<List<MapOverviewListItem>> then = null)
    {
        if (_mapsLoading) return;
        _mapsLoading = true;
        Instances.MapApi.FetchMaps(response =>
        {
            _mapsLoading = false;
            Maps = response?.Maps?.ToList() ?? new List<MapOverviewListItem>();
            then?.Invoke(Maps);
        });
    }

    public static object MapDto(MapOverviewListItem map) => new { id = map.ID, name = map.Name, creator = map.Creator, official = map.Official, length = map.Length, avgTime = map.Time };

    // Lobby creation = what the map list's green play button does: put a playlist into the game's
    // static queue (first = chosen map) and call its "next map in queue", which builds the lobby
    // from the Play-tab settings (max cars, type, time) and loads the Play scene. Works from any screen.
    public static string CreateLobby(string mapQuery)
    {
        if (Running || InLobby) return null;
        FetchMaps(maps =>
        {
            if (maps.Count == 0) { Plugin.Log.LogWarning("/lobby: map list empty"); return; }
            var query = (mapQuery ?? "").Trim().ToLowerInvariant();
            var chosen = maps.FirstOrDefault(map => query != "" && (map.ID.ToString() == query || (map.Name ?? "").ToLowerInvariant().Contains(query))) ?? maps[0];
            var playlist = new List<MapOverviewListItem> { chosen };
            playlist.AddRange(maps.Where(map => map != chosen));
            MapQueue.Set(playlist, 0);
            MapQueue.LoadNext();
        });
        return "opening";
    }

    // ---- screen state ----

    // Where the game is right now: home | play | settings | lobby | racing | postgame | trackbuilder | <panel/scene name>
    public static object ScreenState()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        GameScreen screen; string rawName = null;
        var inGameScreen = Instances.InGameScreen;
        if (Running) screen = GameScreen.Racing;
        else if (Ended && Instances.GameController?.CurrentGame() != null) screen = GameScreen.Postgame;
        else if (inGameScreen != null && InLobby)
        {
            var screenState = inGameScreen.GetCurrentGameScreenState().ToString().ToLowerInvariant();
            screen = screenState.Contains("post") ? GameScreen.Postgame : GameScreen.Lobby;
        }
        else if (inGameScreen != null)
        {
            rawName = inGameScreen.GetCurrentGameScreenState().ToString().ToLowerInvariant();
            screen = GameScreens.Parse(rawName);
        }
        else
        {
            var tabs = Object.FindObjectOfType<Michsky.UI.Zone.MainPanelManager>();
            if (tabs != null && tabs.panels.Count > 0)
            {
                int index = Mathf.Clamp(Traverse.Create(tabs).Field("currentPanelIndex").GetValue<int>(), 0, tabs.panels.Count - 1);
                rawName = tabs.panels[index].name.ToLowerInvariant().Replace(" panel", "").Replace("panel", "").Trim();
                screen = GameScreens.Parse(rawName);
            }
            else if (scene.ToLowerInvariant().Contains("builder")) screen = GameScreen.TrackBuilder;
            else { rawName = scene.ToLowerInvariant(); screen = GameScreens.Parse(rawName); }
        }
        return new { screen = screen.ToApiString(rawName), scene, running = Running, lobby = InLobby, vehicles = Vehicles().Count };
    }

    // ---- race lifecycle ----

    // True from the end of a race until the next lobby (NewGame). The old cars are still in the scene then: joining or
    // starting would double the field, so those routes refuse until POST /race/next (or the Play tab) loads a lobby.
    public static bool Ended;
    public static bool InLobby => !Running && !Ended && Instances.GameController != null && Instances.GameController.CurrentGame() != null && Instances.VehicleManager != null;

    // Default: what the lobby's START button does — kick off the lobby countdown (Settings → start countdown, 60 s),
    // the race starts when it hits zero. now = true skips the countdown and starts immediately.
    public static bool StartRace(bool now = false)
    {
        if (!InLobby || Vehicles().Count == 0) return false;
        var screen = Instances.InGameScreen ?? Object.FindObjectOfType<InGameScreenController>();
        if (!now && screen != null) { screen.StartCountdown(); return true; }
        screen?.InGameSetup();
        Instances.GameController.StartCurrentGame();
        return true;
    }

    // EndCurrentGame is the path the game takes when the clock runs out (stops cars, tears down); ForceEndGame skips it and our race_end patch.
    public static void EndRace() { if (!Running) return; Ended = true; Instances.GameController.EndCurrentGame(); }

    // Next map from the queue set by /lobby (or the Play tab). Works from the post-game screen or anywhere idle.
    public static bool NextRace()
    {
        if (Running || !MapQueue.HasNext()) return false;
        MapQueue.LoadNext();
        return true;
    }
}
