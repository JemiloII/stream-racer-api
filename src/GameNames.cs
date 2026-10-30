// The obfuscation boundary. Together with GlobalUsings.cs (type aliases) this is the only file that spells a
// Beebyte-obfuscated identifier of the game assembly:
//   - GameNames: string constants for members reached through Harmony patches or reflection,
//   - Instances: the game's static singletons,
//   - extension methods that give readable access to obfuscated fields/properties/methods.
// ponytail: names are hard-coded, not discovered by signature. A game update that renames a member makes the route
// using it return 501; the fix is one line here.
using HarmonyLib;

namespace StreamRacerApi;

static class GameNames
{
    // ---- Harmony patch targets and parameter names ----
    public const string NewGame = "DADNHHFPGGP";                 // GameController.NewGame(GameSettings): a lobby opens
    public const string StartCurrentGame = "KENLHJNGFOO";        // GameController.StartCurrentGame(): the race starts
    public const string AddFinisher = "KDPMHCDNCAI";             // GameController.AddFinisherToCurrentGame(vehicle)
    public const string EndCurrentGame = "HCDKBNNFMGD";          // GameController.EndCurrentGame(): race over
    public const string AddVehicle = "IPFFGJMMMAA";              // VehicleManager.AddVehicle(vehicle)
    public const string VehicleExplode = "KNBKIBMMMEK";          // Vehicle.Explode(string by) (+ an Explode() overload: patch by signature): the boom, whatever triggered it (bits, API, chat, extension)
    public const string VehicleUseBoost = "DOHAOPIFMHJ";         // Vehicle.UseBoost(): spend one pool boost; true when it fired
    public const string ChatMessageReceived = "AHLPBGGHNHG";     // TwitchCommandListener.OnMessageReceived(object sender, OnMessageReceivedArgs args)
    public const string ChatMessageArg = "DMEFJDFIAKG";          // ... its args parameter
    public const string AiSpeedUpdate = "BLCIOOKBJAO";           // CarAIControl: per-frame logic that resets the top-speed multiplier
    public const string AiCarController = "IFIGGGIHHID";         // CarAIControl field: the CarController it drives
    public const string AiRespawnCoroutine = "IAPMNOLPJML";      // CarAIControl: the game's own stuck-car respawn coroutine
    public const string CarFinishTrigger = "NPKHIFPGFAM";        // Car: what touching the FinishLine collider runs
    public const string AddVehicleArg = "NHDHMGIKOLL";           // VehicleManager.AddVehicle(vehicle) parameter
    public const string FinisherArg = "IPOLPDNENJO";             // GameController.AddFinisherToCurrentGame(vehicle) parameter

    // ---- fields and methods reached by reflection ----
    public const string VehicleBoosts = "PFGEBLFKKNB";           // Vehicle int: !boost pool left
    public const string VehicleFinishAt = "JDPFEJNJHON";         // Vehicle float: route distance of the finish line for this car
    public const string ChatClient = "IMFLNGCOBLK";              // TwitchCommandListener: the TwitchLib Client logged in as the streamer
    public const string FollowCamPlace = "EODKJILLJGO";          // FollowCam(): put the camera behind its target (checks active + target itself)
    public const string FollowCamZoom = "NCEOGBJJDIB";           // FollowCam float: distance behind the target (mouse wheel)
    public const string CarLabelObject = "COPAKHMIENJ";          // CarLabel GameObject: the spawned name label carrying a VehicleLabel
    public const string LeaderboardItemVehicle = "NHDHMGIKOLL";  // VehicleLeaderboardUIItem: the vehicle of that row
    public const string ListItemProfile = "ACJPEEAIOJG";         // PreGame/PostGamePlayerListItem: the racer profile of that row
    public const string ListItemAvatar = "CMAAJCKFCAN";          // ...: the RawImage avatar
    public const string ListItemName = "PJNFHJGDMPO";            // ...: the TextMeshProUGUI name
    public const string MapQueueList = "PLGOMIBLIIG";            // MapQueueGame static List<MapOverviewListItem>: the playlist
    public const string PortraitStoreAdd = "EMPPNCIOFDG";    // DriverPortraits(profile, DriverPortrait, string): a join reply's portrait
    public const string MapQueueLoadNext = "EDKCLEFEIOE";      // MapQueueGame static: build the lobby for the next queued map
    public const string MapQueueIndex = "LCGIMIJBKLB";           // MapQueueGame static int: position in the playlist
    public const string ResultUpload = "JGIIFGNLPEL";            // MapApi.SendRaceResult(game, RaceResultCallback): POST /RaceResult (season points)
    public const string CamUsePropCam = "JICAAJCCNGG";           // CamController.UsePropCam(GameObject camera): switch to a track camera
    public const string VehicleGripRoutine = "GLJKBFIHCGK";      // Vehicle Coroutine: the running GripFor effect, null when none
    public const string VehicleSaveGrip = "FMPNIMPDOCC";
    public const string CarBoomRoutine = "ILEPOOGHHPH";          // Car Coroutine: the running boom (5 s fuse, blast, 4 s stun, recover)
    public const string CarBoomBusy = "ICCPPIIAMNF";             // Car bool property (private set): true while that boom runs
    public const string VehicleOilGrip = "KFBGDCFOACI";          // Vehicle internal (forward, sideways): the oil spill's friction multipliers (1, 1 = off)
    public const string VehicleOilSpin = "KAAAJADKHKD";          // Vehicle internal (steer -1..1): the oil spill's spin-out steering kick         // Vehicle(): saves every wheel's friction ONCE: the baseline GripFor restores
}

// The game's static singletons (each class has its own obfuscated "Instance" field) and static constants.
static class Instances
{
    public static GameController GameController => GameController.JGBLPOMIIBJ;
    public static VehicleManager VehicleManager => VehicleManager.JGBLPOMIIBJ;
    public static WaypointController WaypointController => WaypointController.JGBLPOMIIBJ;
    public static InGameScreenController InGameScreen => InGameScreenController.JGBLPOMIIBJ;
    public static FollowCam FollowCam => FollowCam.JGBLPOMIIBJ;
    public static FreeCam FreeCam => FreeCam.JGBLPOMIIBJ;
    public static CamController CamController => CamController.JGBLPOMIIBJ;
    public static TwitchCommandListener TwitchListener => TwitchCommandListener.JGBLPOMIIBJ;
    public static BackendSession Backend => BackendSession.NGMGAAIOMLJ;
    public static MinimapView Minimap => MinimapView.NGMGAAIOMLJ;
    public static EventManager Events => EventManager.JGBLPOMIIBJ;   // the RIP feed
    public static MapApi MapApi => GLMMOFINBGE.NGMGAAIOMLJ;
    // The game's own Twitch app client id (a constant, not an instance; it lives here because it is the same kind of static entry point).
    public static string TwitchClientId => ApplicationController.AOHJEOAEOFJ;
}

static class VehicleExtensions
{
    public static RacerProfile Profile(this Vehicle vehicle) => vehicle.ACJPEEAIOJG;
    public static GameObject Car(this Vehicle vehicle) => vehicle.MBMJNBOOJHO;          // null until the car is spawned
    public static float Progress(this Vehicle vehicle) => vehicle.DMPJFPDDNCP;          // route distance driven
    public static void SetProgress(this Vehicle vehicle, float distance) => vehicle.DMPJFPDDNCP = distance;
    public static bool HasFinished(this Vehicle vehicle) => vehicle.GICJJCMONFC;
    public static VehicleType Type(this Vehicle vehicle) => vehicle.BIMIEHGOLAH;
    public static void Explode(this Vehicle vehicle) => vehicle.KNBKIBMMMEK(null);   // by = who caused it, shown in the event feed; null = nobody named
    public static void Explode(this Vehicle vehicle, string by) => vehicle.KNBKIBMMMEK(by);   // the game takes "by" but (Oct 10 build) no longer shows it
    // The game's timed grip effect: every wheel's forward + sideways friction stiffness x multiplier for `seconds`, then
    // restored from the friction it saved the FIRST time (it never re-reads it). A new call replaces a running one.
    public static void GripFor(this Vehicle vehicle, float multiplier, float seconds) => vehicle.EBHLHGAHCJD(multiplier, seconds);
    public static bool UseBoost(this Vehicle vehicle) => vehicle.DOHAOPIFMHJ();
    // GripFor's state, for !drift (Game.Drift): is the effect running now, and make the game take its one-time friction
    // snapshot while the wheels are still untouched (so it never saves a drift's lowered grip as the car's own).
    static readonly System.Reflection.FieldInfo GripRoutineField = AccessTools.Field(typeof(Vehicle), GameNames.VehicleGripRoutine);
    static readonly System.Reflection.MethodInfo SaveGripMethod = AccessTools.Method(typeof(Vehicle), GameNames.VehicleSaveGrip);
    public static bool GripActive(this Vehicle vehicle) => GripRoutineField?.GetValue(vehicle) != null;
    public static void SaveGripBaseline(this Vehicle vehicle) => SaveGripMethod?.Invoke(vehicle, null);
    // The game's oil spill on this car: friction multipliers (it clamps to 0.1..1) and the steering kick it gives.
    static readonly System.Reflection.MethodInfo OilGripMethod = AccessTools.Method(typeof(Vehicle), GameNames.VehicleOilGrip);
    static readonly System.Reflection.MethodInfo OilSpinMethod = AccessTools.Method(typeof(Vehicle), GameNames.VehicleOilSpin);
    public static void OilGrip(this Vehicle vehicle, float forward, float sideways) => OilGripMethod?.Invoke(vehicle, new object[] { forward, sideways });
    public static void OilSpin(this Vehicle vehicle, float steer) => OilSpinMethod?.Invoke(vehicle, new object[] { steer });
    // The game's boom, split so it can go off at once: Explode() starts the car's routine (wait 5 s, explosion force +
    // Blast, wait 4 s, Recover); StopBoomFuse cancels that routine so the caller can run the blast itself right now.
    static readonly System.Reflection.FieldInfo BoomRoutineField = AccessTools.Field(typeof(Car), GameNames.CarBoomRoutine);
    static readonly System.Reflection.PropertyInfo BoomBusyProperty = AccessTools.Property(typeof(Car), GameNames.CarBoomBusy);
    public static bool StopBoomFuse(this Car car)
    {
        if (BoomRoutineField?.GetValue(car) is not Coroutine fuse)
        {
            return false;
        }
        car.StopCoroutine(fuse);
        return true;
    }
    public static void SetBoomBusy(this Car car, bool busy) => BoomBusyProperty?.SetValue(car, busy);
    public static void BoomBlast(this Car car) => car.NLDBFHEJPLJ();     // engine off, AI stops driving: the "dead" car
    public static void BoomRecover(this Car car) => car.OKFLDOLONHC();   // back on the road, driving again
    public static Vehicle NewVehicle(RacerProfile profile) => new Vehicle(profile);
}

static class RacerProfileExtensions
{
    public static string TwitchId(this RacerProfile profile) => profile.JAJPMEDFPNE;
    public static string Login(this RacerProfile profile) => profile.CIOCCBHFIPL;
    public static string DisplayName(this RacerProfile profile) => profile.IAMLKJGAADO;
    public static Color Color(this RacerProfile profile) => profile.DHMIEFCCHOO;
    public static void SetColor(this RacerProfile profile, Color color) => profile.DHMIEFCCHOO = color;
    public static bool IsSubscriber(this RacerProfile profile) => profile.HOBAFBOOIEL;
    public static string CustomTitle(this RacerProfile profile) => profile.DOCHNGPLLHM;  // backend title ("Developer", "Streamer", ...), "" until it arrives
    public static int BackendId(this RacerProfile profile) => profile.FJKNHDMAMCB;       // the backend's id for this join; 0 until the backend has answered
    // What the lobby row prints under the name: the backend CustomTitle, else Subscriber / Normal Racer.
    public static string Title(this RacerProfile profile) => RacerTitles.LKMHNPNCMIL(profile);

    public static RacerProfile NewProfile(string twitchId, string login, string displayName, Color color, bool subscriber) => new RacerProfile
    {
        JAJPMEDFPNE = twitchId,
        CIOCCBHFIPL = login,
        IAMLKJGAADO = displayName,
        DHMIEFCCHOO = color,
        HOBAFBOOIEL = subscriber,
        EMKOENFDFKB = new RacerStats(),
    };
}

static class TwitchUserExtensions
{
    public static string Id(this TwitchUser user) => user.JAJPMEDFPNE;
    public static string Login(this TwitchUser user) => user.KKGBCDMAJJP;
    public static string AccessToken(this TwitchUser user) => user.BMEOEPHJJHL;
}

static class BackendSessionExtensions
{
    public static TwitchUser CurrentUser(this BackendSession session) => session.PMPJIPMFBNI();
}

static class MapApiExtensions
{
    public static void FetchMaps(this MapApi api, System.Action<MapOverviewListResponse> then) => api.NMEAECONCOD(response => then(response));
}

// The game's static map playlist: what the map list's play button fills and "next map in queue" consumes.
static class MapQueue
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueList);
    static readonly System.Reflection.FieldInfo IndexField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueIndex);

    public static void Set(List<MapOverviewListItem> playlist, int index) { ListField.SetValue(null, playlist); IndexField.SetValue(null, index); }
    public static bool HasNext() => MapQueueGame.LIAADDDAHKG();
    // Moves the playlist position past maps in settings.hiddenMaps (the game reads the map at that position next).
    public static void SkipHidden()
    {
        if (ListField.GetValue(null) is not List<MapOverviewListItem> playlist) return;
        int index = (int)IndexField.GetValue(null);
        while (index < playlist.Count && Settings.Current.hiddenMaps.Contains(playlist[index].ID)) index++;
        IndexField.SetValue(null, index);
    }
    // Builds the lobby from the Play-tab settings for the next map in the playlist and loads the Play scene.
    public static void LoadNext() => MapQueueGame.EDKCLEFEIOE();
}

static class GameControllerExtensions
{
    public static GameSettings CurrentGame(this GameController controller) => controller.KMPCIFBJGLI;
}

static class GameSettingsExtensions
{
    public static int MapId(this GameSettings game) => game.NPGHIEHJDNI;
    public static string MapName(this GameSettings game) => game.HNCAHKBAICL;
}

static class CircuitExtensions
{
    public static Transform[] Waypoints(this WaypointCircuit circuit) => circuit.EHMFHIBBDND;  // the road itself, in order, closed by a hop back to the start
    public static float Length(this WaypointCircuit circuit) => circuit.IEENMDELNBG;
    public static Vector3 Position(this RoutePoint point) => point.BHHAEMHKKDP;
    public static Vector3 Direction(this RoutePoint point) => point.FHHBIJBDLJO;
}

static class CameraExtensions
{
    public static bool IsActive(this FreeCam cam) => cam.ODLPEHOELLH;
    public static void SetActive(this FreeCam cam, bool active) => cam.ODLPEHOELLH = active;
    public static bool IsActive(this FollowCam cam) => cam.ODLPEHOELLH;
    public static void SetActive(this FollowCam cam, bool active) => cam.ODLPEHOELLH = active;
    public static Transform Target(this FollowCam cam) => cam.BLPGJONELMM;
    public static float Yaw(this CamController controller) => controller.PIGKFKALNKJ;
    public static void SetYaw(this CamController controller, float degrees) => controller.PIGKFKALNKJ = degrees;
    public static float Pitch(this CamController controller) => controller.AAIJFJKLNOK;
    public static void SetPitch(this CamController controller, float degrees) => controller.AAIJFJKLNOK = degrees;
    // The child the follow cam attaches to: the smooth thing to track.
    public static GameObject CameraAnchor(this Car car) => car.KPPJJHEFMJB;
    public static WheelCollider[] Wheels(this Car car) => car.PMBKAOLDIAA;   // the four WheelColliders (order as the prefab lists them)
}

static class CarControllerExtensions
{
    public static float TopSpeedMultiplier(this CarController controller) => controller.CJHAFLOHLII;
    public static void SetTopSpeedMultiplier(this CarController controller, float multiplier) => controller.CJHAFLOHLII = multiplier;
}

static class ScreenExtensions
{
    // What the lobby's START button does: begin the start countdown.
    public static void StartCountdown(this InGameScreenController screen) => screen.GameStartClicked();
}

static class UiItemExtensions
{
    public static TMPro.TMP_Text NameText(this VehicleLabel label) => label.JKPOKFFECMJ;
    public static TMPro.TextMeshProUGUI NameText(this VehicleLeaderboardUIItem item) => item.PJNFHJGDMPO;
    public static RacerProfile Profile(this PreGamePlayerListItem row) => row.ACJPEEAIOJG;
    public static TMPro.TextMeshProUGUI NameText(this PreGamePlayerListItem row) => row.PJNFHJGDMPO;
}

static class VehicleManagerExtensions
{
    // The game dropped HasPlayerAlreadyJoined (Oct 2026 build); same check by login.
    public static bool HasPlayerAlreadyJoined(this VehicleManager manager, string login) => manager.GetVehicleByTwitchUsername(login) != null;
}

// Oct 21 2026 build: the game's readable methods got obfuscated as well. These keep their old names for call sites.
static class RenamedMethods
{
    public static void EndCurrentGame(this GameController controller) => controller.HCDKBNNFMGD();
    public static void StartCurrentGame(this GameController controller) => controller.KENLHJNGFOO();
    public static bool IsGameRunning(this GameController controller) => controller.IOLJCECJCDN();
    public static RoutePoint GetRoutePoint(this WaypointCircuit circuit, float distance) => circuit.ICBGNCODGCC(distance);
    public static WaypointCircuit GetCircuit(this WaypointController controller) => controller.HEELJENILCH();
    public static GFFOKBKBFCG.NOANAEHMEOG GetCurrentGameScreenState(this InGameScreenController screen) => screen.FMAMCIBDBOP();
    public static void InGameSetup(this InGameScreenController screen) => screen.GNCPAFNCFMG();
    public static void Boost(this Car car, float a, float b) => car.DOHAOPIFMHJ(a, b);
    public static void SetVehicle(this VehicleLeaderboardUIItem item, Vehicle vehicle) => item.AOIKDACIPAA(vehicle);
    public static void BindVehicles(this MinimapView view, IList<Vehicle> vehicles) => view.AAGNEOANIOB(vehicles);
    public static bool FollowVehicle(this CamController controller, Vehicle vehicle) => controller.KJGJBKOHNCP(vehicle);
    public static void ReleasePropCam(this CamController controller) => controller.DPEIBKICFCN();
    public static void Remove(this GameEvent line) => line.MFPHLAIHCBF();
    public static Vehicle GetVehicleByTwitchUsername(this VehicleManager manager, string login) => manager.CAPKKMDBCIO(login);
    public static List<Vehicle> GetVehicles(this VehicleManager manager) => manager.NKDMOFONKOL();
    public static bool RemoveVehicleByTwitchUsername(this VehicleManager manager, string login) => manager.DNCDEFGDCIF(login);
    public static void AddVehicle(this VehicleManager manager, Vehicle vehicle) => manager.IPFFGJMMMAA(vehicle);
    public static GameEvent AddEvent(this EventManager events, string text, FeedClick onClick) => events.JNKBJGINCGD(text, onClick);
    public static void Follow(this FollowCam cam, Vehicle vehicle) => cam.KCMIBPPBFEE(vehicle);
    public static void StopFollow(this FollowCam cam) => cam.FHIGDDOAMNL();
    public static bool IsDriving(this CarAIControl ai) => ai.EDLNGCAOGBL();
    public static bool BeginSafetyCar(string by) => VirtualSafetyCarController.KBNKPJPCCGD(by);
    public static Cage.StreamRacer.Drivers.DriverLibrary DriverLibrary() => Object.FindObjectOfType<DriverSeats>()?.FAPBKHKLMJM;
    public static bool BeginOilSpill(string by) => OilSpillController.KBNKPJPCCGD(by);   // dev aid only (/find/oil)
}

static class ChaosStatusExtensions
{
    public static ChaosEffectStatus ChaosStatus() => ChaosEffectStatus.IMEIADIHPPE();   // GetOrCreate: null before the in-game canvas exists
    public static void PlayShrinkSound(this ChaosEffectStatus status) => status.OCFDBIPKCLD();
}

static class MinimapExtensions
{
    public static MinimapCapture Capture(this MinimapView view) => view.BHAKEHLIKCF;          // null until the map is captured
    public static RenderTexture Picture(this MinimapCapture capture) => capture.NCHHIPFJICD;  // 512x512, transparent background
    public static Vector3 Centre(this MinimapCapture capture) => capture.KLEHOJKINKN.CMPECFHHJLK;
    public static Vector2 Extent(this MinimapCapture capture) => capture.KLEHOJKINKN.BLMDIMAGLMN;
}
