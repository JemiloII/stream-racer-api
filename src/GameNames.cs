// The obfuscation boundary. Together with GlobalUsings.cs (type aliases) this is the only file that spells a
// Beebyte-obfuscated identifier of the game assembly:
//   - GameNames: string constants for members reached through Harmony patches or reflection,
//   - Instances: the game's static singletons,
//   - extension methods that give readable access to obfuscated fields/properties/methods.
// ponytail: names are hard-coded, not discovered by signature. A game update that renames a member makes the route
// using it return 501; the fix is one line here.
using HarmonyLib;
using UnityStandardAssets.Utility;
using UnityStandardAssets.Vehicles.Car;

namespace StreamRacerApi;

static class GameNames
{
    // ---- Harmony patch targets and parameter names ----
    public const string VehicleExplode = "NPGMKJHIGNO";          // Vehicle.Explode(): the boom, whatever triggered it (bits, API, chat)
    public const string VehicleUseBoost = "MOEACENIMLO";         // Vehicle.UseBoost(): spend one pool boost; true when it fired
    public const string FreeCamVerticalMove = "PKFKLIPEBJP";     // FreeCam: the hardcoded Q/E up-down handling, called from Update
    public const string FreeCamSetPosition = "CONGPDEJHFM";      // FreeCam.SetPosition(Vector3)
    public const string FreeCamFastFlag = "ICHHONAIDDJ";         // FreeCam bool field: shift held -> 1 unit per frame instead of 0.25
    public const string ChatMessageReceived = "CHOJMKCDOGG";     // TwitchCommandListener.OnMessageReceived(object sender, OnMessageReceivedArgs args)
    public const string ChatMessageArg = "KGCAKNNGBDA";          // ... its args parameter
    public const string AiSpeedUpdate = "AEPCFOLDGFE";           // CarAIControl: per-frame logic that resets the top-speed multiplier
    public const string AiCarController = "GBHAMILJILE";         // CarAIControl field: the CarController it drives
    public const string AiRespawnCoroutine = "ANOOALMHGCC";      // CarAIControl: the game's own stuck-car respawn coroutine
    public const string CarFinishTrigger = "AEHEKHFIAEK";        // Car: what touching the FinishLine collider runs
    public const string AddVehicleArg = "LOHGEJIOPJL";           // VehicleManager.AddVehicle(vehicle) parameter
    public const string FinisherArg = "KGEKACHOBHK";             // GameController.AddFinisherToCurrentGame(vehicle) parameter

    // ---- fields and methods reached by reflection ----
    public const string VehicleBoosts = "MKFJMCDNHMA";           // Vehicle int: !boost pool left
    public const string VehicleFinishAt = "BNMEBMGJHDF";         // Vehicle float: route distance of the finish line for this car
    public const string ChatClient = "LMGJJMLKMPA";              // TwitchCommandListener: the TwitchLib Client logged in as the streamer
    public const string FollowCamZoom = "JNEOPKGDGJB";           // FollowCam float: distance behind the target (mouse wheel)
    public const string CarLabelObject = "ODKBABJEGKK";          // CarLabel GameObject: the spawned name label carrying a VehicleLabel
    public const string LeaderboardItemVehicle = "LOHGEJIOPJL";  // VehicleLeaderboardUIItem: the vehicle of that row
    public const string ListItemProfile = "JDDOIMHIFHK";         // PreGame/PostGamePlayerListItem: the racer profile of that row
    public const string ListItemAvatar = "LJLJGFLLMOF";          // ...: the RawImage avatar
    public const string ListItemName = "AKAMDOKJAIE";            // ...: the TextMeshProUGUI name
    public const string MapQueueList = "DCKCLNFBGFM";            // MapQueueGame static List<MapOverviewListItem>: the playlist
    public const string MapQueueIndex = "MCMLCNHMDPJ";           // MapQueueGame static int: position in the playlist
    public const string CamUsePropCam = "LCMHCNJMNPM";           // CamController.UsePropCam(GameObject camera): switch to a track camera
}

// The game's static singletons (each class has its own obfuscated "Instance" field) and static constants.
static class Instances
{
    public static GameController GameController => Cage.StreamRacer.GameController.NEPFAEJAMGI;
    public static VehicleManager VehicleManager => Cage.StreamRacer.VehicleManager.NEPFAEJAMGI;
    public static WaypointController WaypointController => Cage.StreamRacer.WaypointController.NEPFAEJAMGI;
    public static InGameScreenController InGameScreen => InGameScreenController.NEPFAEJAMGI;
    public static FollowCam FollowCam => Cage.StreamRacer.FollowCam.NEPFAEJAMGI;
    public static FreeCam FreeCam => Cage.StreamRacer.FreeCam.NEPFAEJAMGI;
    public static CamController CamController => Cage.StreamRacer.CamController.NEPFAEJAMGI;
    public static TwitchCommandListener TwitchListener => TwitchCommandListener.NEPFAEJAMGI;
    public static BackendSession Backend => BackendSession.JIMBAOHPFOG;
    public static MapApi MapApi => KBCFIIFFLFL.JIMBAOHPFOG;
    // The game's own Twitch app client id (a constant, not an instance; it lives here because it is the same kind of static entry point).
    public static string TwitchClientId => ApplicationController.IANOCCLIMAA;
}

static class VehicleExtensions
{
    public static RacerProfile Profile(this Vehicle vehicle) => vehicle.JDDOIMHIFHK;
    public static GameObject Car(this Vehicle vehicle) => vehicle.JPHIMKLIAAO;          // null until the car is spawned
    public static float Progress(this Vehicle vehicle) => vehicle.MNHJMCLOGPB;          // route distance driven
    public static void SetProgress(this Vehicle vehicle, float distance) => vehicle.MNHJMCLOGPB = distance;
    public static bool HasFinished(this Vehicle vehicle) => vehicle.NIKOEDJIAFB;
    public static VehicleType Type(this Vehicle vehicle) => vehicle.BDBCNPKDNMC;
    public static void Explode(this Vehicle vehicle) => vehicle.NPGMKJHIGNO();
    public static bool UseBoost(this Vehicle vehicle) => vehicle.MOEACENIMLO();
    public static Vehicle NewVehicle(RacerProfile profile) => new Vehicle(profile);
}

static class RacerProfileExtensions
{
    public static string TwitchId(this RacerProfile profile) => profile.DNJFGHLIAIM;
    public static string Login(this RacerProfile profile) => profile.JLDPKDLFPJP;
    public static string DisplayName(this RacerProfile profile) => profile.AMCIKHEHBGM;
    public static Color Color(this RacerProfile profile) => profile.EKPDDGFGLNI;
    public static void SetColor(this RacerProfile profile, Color color) => profile.EKPDDGFGLNI = color;
    public static bool IsSubscriber(this RacerProfile profile) => profile.HIMCIABAFNG;
    public static string CustomTitle(this RacerProfile profile) => profile.GGCGDCLGPPL;  // backend title ("Developer", "Streamer", ...), "" until it arrives
    public static int BackendId(this RacerProfile profile) => profile.IGMMBKDMPLL;       // the backend's id for this join; 0 until the backend has answered
    // What the lobby row prints under the name: the backend CustomTitle, else Subscriber / Normal Racer.
    public static string Title(this RacerProfile profile) => RacerTitles.LJKJGOKBIJF(profile);

    public static RacerProfile NewProfile(string twitchId, string login, string displayName, Color color, bool subscriber) => new RacerProfile
    {
        DNJFGHLIAIM = twitchId,
        JLDPKDLFPJP = login,
        AMCIKHEHBGM = displayName,
        EKPDDGFGLNI = color,
        HIMCIABAFNG = subscriber,
        POKHEANEDLK = new RacerStats(),
    };
}

static class TwitchUserExtensions
{
    public static string Id(this TwitchUser user) => user.DNJFGHLIAIM;
    public static string Login(this TwitchUser user) => user.DBOIOBOEHLN;
    public static string AccessToken(this TwitchUser user) => user.BINBLDPOLPE;
}

static class BackendSessionExtensions
{
    public static TwitchUser CurrentUser(this BackendSession session) => session.HENMPBNKMKD();
}

static class MapApiExtensions
{
    public static void FetchMaps(this MapApi api, System.Action<MapOverviewListResponse> then) => api.LEMLINILCDG(response => then(response));
}

// The game's static map playlist: what the map list's play button fills and "next map in queue" consumes.
static class MapQueue
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueList);
    static readonly System.Reflection.FieldInfo IndexField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueIndex);

    public static void Set(List<MapOverviewListItem> playlist, int index) { ListField.SetValue(null, playlist); IndexField.SetValue(null, index); }
    public static bool HasNext() => MapQueueGame.BNKMAHINMGP();
    // Builds the lobby from the Play-tab settings for the next map in the playlist and loads the Play scene.
    public static void LoadNext() => MapQueueGame.JKMCAIDPAAB();
}

static class GameControllerExtensions
{
    public static GameSettings CurrentGame(this GameController controller) => controller.BFOGGNCDONA;
}

static class GameSettingsExtensions
{
    public static int MapId(this GameSettings game) => game.MHOHHJCNJOP;
    public static string MapName(this GameSettings game) => game.FLAENOELFHN;
}

static class CircuitExtensions
{
    public static Transform[] Waypoints(this WaypointCircuit circuit) => circuit.IPOOGHHOEGD;  // the road itself, in order, closed by a hop back to the start
    public static float Length(this WaypointCircuit circuit) => circuit.BDFOFBNGLBA;
    public static Vector3 Position(this RoutePoint point) => point.PFHEOLIHHHD;
    public static Vector3 Direction(this RoutePoint point) => point.AHLOFCFINIO;
}

static class CameraExtensions
{
    public static bool IsActive(this FreeCam cam) => cam.MKJKDNAFJML;
    public static void SetActive(this FreeCam cam, bool active) => cam.MKJKDNAFJML = active;
    public static bool IsActive(this FollowCam cam) => cam.MKJKDNAFJML;
    public static void SetActive(this FollowCam cam, bool active) => cam.MKJKDNAFJML = active;
    public static Transform Target(this FollowCam cam) => cam.KKPFHEIDONO;
    public static float Yaw(this CamController controller) => controller.JAPOGLMOALK;
    public static void SetYaw(this CamController controller, float degrees) => controller.JAPOGLMOALK = degrees;
    public static float Pitch(this CamController controller) => controller.JMFMLGGKDLA;
    public static void SetPitch(this CamController controller, float degrees) => controller.JMFMLGGKDLA = degrees;
    // The child the follow cam attaches to: the smooth thing to track.
    public static GameObject CameraAnchor(this Car car) => car.AHBGCGOEADP;
}

static class CarControllerExtensions
{
    public static float TopSpeedMultiplier(this CarController controller) => controller.DAOEOLBGMGM;
    public static void SetTopSpeedMultiplier(this CarController controller, float multiplier) => controller.DAOEOLBGMGM = multiplier;
}

static class ScreenExtensions
{
    // What the lobby's START button does: begin the start countdown.
    public static void StartCountdown(this InGameScreenController screen) => screen.PCLCKCHAICB();
}

static class UiItemExtensions
{
    public static UnityEngine.UI.Text NameText(this VehicleLabel label) => label.BBLPLCOPBPP;
    public static TMPro.TextMeshProUGUI NameText(this VehicleLeaderboardUIItem item) => item.AKAMDOKJAIE;
    public static RacerProfile Profile(this PreGamePlayerListItem row) => row.JDDOIMHIFHK;
    public static TMPro.TextMeshProUGUI NameText(this PreGamePlayerListItem row) => row.AKAMDOKJAIE;
}
