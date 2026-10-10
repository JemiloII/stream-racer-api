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
    public const string VehicleExplode = "JOPPCLLPIKD";          // Vehicle.Explode(string by) (+ an Explode() overload: patch by signature): the boom, whatever triggered it (bits, API, chat, extension)
    public const string VehicleUseBoost = "DLOGABHGBCO";         // Vehicle.UseBoost(): spend one pool boost; true when it fired
    public const string ChatMessageReceived = "LJHMLBPPIJA";     // TwitchCommandListener.OnMessageReceived(object sender, OnMessageReceivedArgs args)
    public const string ChatMessageArg = "PMJAEHLOADK";          // ... its args parameter
    public const string AiSpeedUpdate = "JGHHELNLLFL";           // CarAIControl: per-frame logic that resets the top-speed multiplier
    public const string AiCarController = "OKKLDMMDDKO";         // CarAIControl field: the CarController it drives
    public const string AiRespawnCoroutine = "LAEINDGLJNI";      // CarAIControl: the game's own stuck-car respawn coroutine
    public const string CarFinishTrigger = "INOMOBDCAAO";        // Car: what touching the FinishLine collider runs
    public const string AddVehicleArg = "NECAFMMOLLO";           // VehicleManager.AddVehicle(vehicle) parameter
    public const string FinisherArg = "BHLJLLFABON";             // GameController.AddFinisherToCurrentGame(vehicle) parameter

    // ---- fields and methods reached by reflection ----
    public const string VehicleBoosts = "JNDEPCMEDDP";           // Vehicle int: !boost pool left
    public const string VehicleFinishAt = "LOEEELAENJD";         // Vehicle float: route distance of the finish line for this car
    public const string ChatClient = "ABHFEMPBBIN";              // TwitchCommandListener: the TwitchLib Client logged in as the streamer
    public const string FollowCamZoom = "BMPOJKJDJKI";           // FollowCam float: distance behind the target (mouse wheel)
    public const string CarLabelObject = "FKMOHAICKBJ";          // CarLabel GameObject: the spawned name label carrying a VehicleLabel
    public const string LeaderboardItemVehicle = "NECAFMMOLLO";  // VehicleLeaderboardUIItem: the vehicle of that row
    public const string ListItemProfile = "NMNLOHMKGMB";         // PreGame/PostGamePlayerListItem: the racer profile of that row
    public const string ListItemAvatar = "CFHDPDEIFHP";          // ...: the RawImage avatar
    public const string ListItemName = "MBPBDDLJIML";            // ...: the TextMeshProUGUI name
    public const string MapQueueList = "JINKKNDCIDN";            // MapQueueGame static List<MapOverviewListItem>: the playlist
    public const string MapQueueIndex = "PGCDMGIBGCP";           // MapQueueGame static int: position in the playlist
    public const string ResultUpload = "NLCMDLPPKIB";            // MapApi.SendRaceResult(game, RaceResultCallback): POST /RaceResult (season points)
    public const string CamUsePropCam = "ANEHKEHKLIK";           // CamController.UsePropCam(GameObject camera): switch to a track camera
    public const string VehicleGripRoutine = "JECEJPEEFPJ";      // Vehicle Coroutine: the running GripFor effect, null when none
    public const string VehicleSaveGrip = "EIFFOMNKEKC";         // Vehicle(): saves every wheel's friction ONCE: the baseline GripFor restores
}

// The game's static singletons (each class has its own obfuscated "Instance" field) and static constants.
static class Instances
{
    public static GameController GameController => Cage.StreamRacer.GameController.CPNLBANJLLG;
    public static VehicleManager VehicleManager => Cage.StreamRacer.VehicleManager.CPNLBANJLLG;
    public static WaypointController WaypointController => Cage.StreamRacer.WaypointController.CPNLBANJLLG;
    public static InGameScreenController InGameScreen => InGameScreenController.CPNLBANJLLG;
    public static FollowCam FollowCam => Cage.StreamRacer.FollowCam.CPNLBANJLLG;
    public static FreeCam FreeCam => Cage.StreamRacer.FreeCam.CPNLBANJLLG;
    public static CamController CamController => Cage.StreamRacer.CamController.CPNLBANJLLG;
    public static TwitchCommandListener TwitchListener => TwitchCommandListener.CPNLBANJLLG;
    public static BackendSession Backend => BackendSession.JCCKHOEHMBM;
    public static MinimapView Minimap => MinimapView.JCCKHOEHMBM;
    public static Cage.StreamRacer.Events.EventManager Events => Cage.StreamRacer.Events.EventManager.CPNLBANJLLG;   // the RIP feed
    public static MapApi MapApi => EPHALAALFKL.JCCKHOEHMBM;
    // The game's own Twitch app client id (a constant, not an instance; it lives here because it is the same kind of static entry point).
    public static string TwitchClientId => ApplicationController.BBHMCGOFCPF;
}

static class VehicleExtensions
{
    public static RacerProfile Profile(this Vehicle vehicle) => vehicle.NMNLOHMKGMB;
    public static GameObject Car(this Vehicle vehicle) => vehicle.DLHDBMAKMCA;          // null until the car is spawned
    public static float Progress(this Vehicle vehicle) => vehicle.BKOMFDMBNNF;          // route distance driven
    public static void SetProgress(this Vehicle vehicle, float distance) => vehicle.BKOMFDMBNNF = distance;
    public static bool HasFinished(this Vehicle vehicle) => vehicle.DOMGOGHOLAC;
    public static VehicleType Type(this Vehicle vehicle) => vehicle.BEDHBLCMINF;
    public static void Explode(this Vehicle vehicle) => vehicle.JOPPCLLPIKD(null);   // by = who caused it, shown in the event feed; null = nobody named
    public static void Explode(this Vehicle vehicle, string by) => vehicle.JOPPCLLPIKD(by);   // the game takes "by" but (Oct 10 build) no longer shows it
    // The game's shrink ray on one car: scale (clamped 0.1..1 by the game) for `seconds`, pulse = flicker in; restores itself.
    public static void ShrinkFor(this Vehicle vehicle, float scale, float seconds, bool pulse) => vehicle.FPDPHAOPGFA(scale, seconds, pulse);
    // The game's timed grip effect: every wheel's forward + sideways friction stiffness x multiplier for `seconds`, then
    // restored from the friction it saved the FIRST time (it never re-reads it). A new call replaces a running one.
    public static void GripFor(this Vehicle vehicle, float multiplier, float seconds) => vehicle.IOGCKHMNEDG(multiplier, seconds);
    public static bool UseBoost(this Vehicle vehicle) => vehicle.DLOGABHGBCO();
    // GripFor's state, for !drift (Game.Drift): is the effect running now, and make the game take its one-time friction
    // snapshot while the wheels are still untouched (so it never saves a drift's lowered grip as the car's own).
    static readonly System.Reflection.FieldInfo GripRoutineField = AccessTools.Field(typeof(Vehicle), GameNames.VehicleGripRoutine);
    static readonly System.Reflection.MethodInfo SaveGripMethod = AccessTools.Method(typeof(Vehicle), GameNames.VehicleSaveGrip);
    public static bool GripActive(this Vehicle vehicle) => GripRoutineField?.GetValue(vehicle) != null;
    public static void SaveGripBaseline(this Vehicle vehicle) => SaveGripMethod?.Invoke(vehicle, null);
    public static Vehicle NewVehicle(RacerProfile profile) => new Vehicle(profile);
}

static class RacerProfileExtensions
{
    public static string TwitchId(this RacerProfile profile) => profile.MIGNIOHMLKM;
    public static string Login(this RacerProfile profile) => profile.GLAJDMGEINP;
    public static string DisplayName(this RacerProfile profile) => profile.JGGFMLBFPCH;
    public static Color Color(this RacerProfile profile) => profile.JIGEIIEIDJL;
    public static void SetColor(this RacerProfile profile, Color color) => profile.JIGEIIEIDJL = color;
    public static bool IsSubscriber(this RacerProfile profile) => profile.HKNKCLGADAK;
    public static string CustomTitle(this RacerProfile profile) => profile.CLOBLIBDPIL;  // backend title ("Developer", "Streamer", ...), "" until it arrives
    public static int BackendId(this RacerProfile profile) => profile.NFOFJBLBGDB;       // the backend's id for this join; 0 until the backend has answered
    // What the lobby row prints under the name: the backend CustomTitle, else Subscriber / Normal Racer.
    public static string Title(this RacerProfile profile) => RacerTitles.LAGCDGFIBEM(profile);

    public static RacerProfile NewProfile(string twitchId, string login, string displayName, Color color, bool subscriber) => new RacerProfile
    {
        MIGNIOHMLKM = twitchId,
        GLAJDMGEINP = login,
        JGGFMLBFPCH = displayName,
        JIGEIIEIDJL = color,
        HKNKCLGADAK = subscriber,
        MKNBDJDBEBF = new RacerStats(),
    };
}

static class TwitchUserExtensions
{
    public static string Id(this TwitchUser user) => user.MIGNIOHMLKM;
    public static string Login(this TwitchUser user) => user.NKIBOHDBJOI;
    public static string AccessToken(this TwitchUser user) => user.JFOMJMKPLFB;
}

static class BackendSessionExtensions
{
    public static TwitchUser CurrentUser(this BackendSession session) => session.DDAPEDKAEOA();
}

static class MapApiExtensions
{
    public static void FetchMaps(this MapApi api, System.Action<MapOverviewListResponse> then) => api.MECJOJMGOKD(response => then(response));
}

// The game's static map playlist: what the map list's play button fills and "next map in queue" consumes.
static class MapQueue
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueList);
    static readonly System.Reflection.FieldInfo IndexField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueIndex);

    public static void Set(List<MapOverviewListItem> playlist, int index) { ListField.SetValue(null, playlist); IndexField.SetValue(null, index); }
    public static bool HasNext() => MapQueueGame.GKKCBDFIIFH();
    // Builds the lobby from the Play-tab settings for the next map in the playlist and loads the Play scene.
    public static void LoadNext() => MapQueueGame.NEDDJAABNFB();
}

static class GameControllerExtensions
{
    public static GameSettings CurrentGame(this GameController controller) => controller.ADGHJGLGEEA;
}

static class GameSettingsExtensions
{
    public static int MapId(this GameSettings game) => game.PCDPHBBCEHM;
    public static string MapName(this GameSettings game) => game.MBAIDFIACDJ;
}

static class CircuitExtensions
{
    public static Transform[] Waypoints(this WaypointCircuit circuit) => circuit.DAABBHABJAA;  // the road itself, in order, closed by a hop back to the start
    public static float Length(this WaypointCircuit circuit) => circuit.NGBACAFDHNC;
    public static Vector3 Position(this RoutePoint point) => point.PCHOOBLCNEG;
    public static Vector3 Direction(this RoutePoint point) => point.PNOMCJJDDLA;
}

static class CameraExtensions
{
    public static bool IsActive(this FreeCam cam) => cam.HOMIMONNLLA;
    public static void SetActive(this FreeCam cam, bool active) => cam.HOMIMONNLLA = active;
    public static bool IsActive(this FollowCam cam) => cam.HOMIMONNLLA;
    public static void SetActive(this FollowCam cam, bool active) => cam.HOMIMONNLLA = active;
    public static Transform Target(this FollowCam cam) => cam.GHPDNFHLGOM;
    public static float Yaw(this CamController controller) => controller.NDAGOBNDMBD;
    public static void SetYaw(this CamController controller, float degrees) => controller.NDAGOBNDMBD = degrees;
    public static float Pitch(this CamController controller) => controller.NAILLCJPLJN;
    public static void SetPitch(this CamController controller, float degrees) => controller.NAILLCJPLJN = degrees;
    // The child the follow cam attaches to: the smooth thing to track.
    public static GameObject CameraAnchor(this Car car) => car.AGHNEGGFGDH;
    public static WheelCollider[] Wheels(this Car car) => car.NGFMHNBONJN;   // the four WheelColliders (order as the prefab lists them)
}

static class CarControllerExtensions
{
    public static float TopSpeedMultiplier(this CarController controller) => controller.JLIJKFGHLGF;
    public static void SetTopSpeedMultiplier(this CarController controller, float multiplier) => controller.JLIJKFGHLGF = multiplier;
}

static class ScreenExtensions
{
    // What the lobby's START button does: begin the start countdown.
    public static void StartCountdown(this InGameScreenController screen) => screen.GameStartClicked();
}

static class UiItemExtensions
{
    public static UnityEngine.UI.Text NameText(this VehicleLabel label) => label.DLDNNAIJJNH;
    public static TMPro.TextMeshProUGUI NameText(this VehicleLeaderboardUIItem item) => item.MBPBDDLJIML;
    public static RacerProfile Profile(this PreGamePlayerListItem row) => row.NMNLOHMKGMB;
    public static TMPro.TextMeshProUGUI NameText(this PreGamePlayerListItem row) => row.MBPBDDLJIML;
}

static class VehicleManagerExtensions
{
    // The game dropped HasPlayerAlreadyJoined (Oct 2026 build); same check by login.
    public static bool HasPlayerAlreadyJoined(this VehicleManager manager, string login) => manager.GetVehicleByTwitchUsername(login) != null;
}

static class MinimapExtensions
{
    public static MinimapCapture Capture(this MinimapView view) => view.GJIMCAPPALL;          // null until the map is captured
    public static RenderTexture Picture(this MinimapCapture capture) => capture.PKAMBMIOANB;  // 512x512, transparent background
    public static Vector3 Centre(this MinimapCapture capture) => capture.GOAFBKAODNI.NOAGNJFGKFH;
    public static Vector2 Extent(this MinimapCapture capture) => capture.GOAFBKAODNI.FEIFIAHBGDO;
}
