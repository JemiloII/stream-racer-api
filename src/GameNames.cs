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
    public const string NewGame = "PEDBEAIBEPH";                 // GameController.NewGame(GameSettings): a lobby opens
    public const string StartCurrentGame = "LNIFLEEJLKF";        // GameController.StartCurrentGame(): the race starts
    public const string AddFinisher = "IBDCFILOBAN";             // GameController.AddFinisherToCurrentGame(vehicle)
    public const string EndCurrentGame = "INMJMBIDAFE";          // GameController.EndCurrentGame(): race over
    public const string AddVehicle = "KJMHFOOGGID";              // VehicleManager.AddVehicle(vehicle)
    public const string VehicleExplode = "DJKPOLHJOOK";          // Vehicle.Explode(string by) (+ an Explode() overload: patch by signature): the boom, whatever triggered it (bits, API, chat, extension)
    public const string VehicleUseBoost = "GEOHOLHIKPC";         // Vehicle.UseBoost(): spend one pool boost; true when it fired
    public const string ChatMessageReceived = "FEMEDFCLDBH";     // TwitchCommandListener.OnMessageReceived(object sender, OnMessageReceivedArgs args)
    public const string ChatMessageArg = "IFGBBKGPMHN";          // ... its args parameter
    public const string AiSpeedUpdate = "GAOFDFGCFHB";           // CarAIControl: per-frame logic that resets the top-speed multiplier
    public const string AiCarController = "PEEFEOBNBLG";         // CarAIControl field: the CarController it drives
    public const string AiRespawnCoroutine = "PECKJNOJKEP";      // CarAIControl: the game's own stuck-car respawn coroutine
    public const string CarFinishTrigger = "MBAMCEMBOGD";        // Car: what touching the FinishLine collider runs
    public const string AddVehicleArg = "LBGHIDMPAJP";           // VehicleManager.AddVehicle(vehicle) parameter
    public const string FinisherArg = "BFMGHKMGGOA";             // GameController.AddFinisherToCurrentGame(vehicle) parameter

    // ---- fields and methods reached by reflection ----
    public const string VehicleBoosts = "CJDFGFLCDMJ";           // Vehicle int: !boost pool left
    public const string VehicleFinishAt = "AFEHPKMJNDJ";         // Vehicle float: route distance of the finish line for this car
    public const string ChatClient = "IFINKKGILAO";              // TwitchCommandListener: the TwitchLib Client logged in as the streamer
    public const string FollowCamPlace = "LNPNNGOLHOO";          // FollowCam(): put the camera behind its target (checks active + target itself)
    public const string FollowCamZoom = "PMEEDKAEDBL";           // FollowCam float: distance behind the target (mouse wheel)
    public const string CarLabelObject = "ACIIKHMFPCN";          // CarLabel GameObject: the spawned name label carrying a VehicleLabel
    public const string LeaderboardItemVehicle = "LBGHIDMPAJP";  // VehicleLeaderboardUIItem: the vehicle of that row
    public const string ListItemProfile = "ILKNIPGLDFH";         // PreGame/PostGamePlayerListItem: the racer profile of that row
    public const string ListItemAvatar = "GPJBBIOJLEI";          // ...: the RawImage avatar
    public const string ListItemName = "ILJIJCCAPNN";            // ...: the TextMeshProUGUI name
    public const string MapQueueList = "LOOPBCOBPCN";            // MapQueueGame static List<MapOverviewListItem>: the playlist
    public const string MapQueueIndex = "ILOELGKHDJC";           // MapQueueGame static int: position in the playlist
    public const string ResultUpload = "DGJLKOEECEJ";            // MapApi.SendRaceResult(game, RaceResultCallback): POST /RaceResult (season points)
    public const string CamUsePropCam = "AFDBMFGBKIP";           // CamController.UsePropCam(GameObject camera): switch to a track camera
    public const string VehicleGripRoutine = "BMNPGOOPJML";      // Vehicle Coroutine: the running GripFor effect, null when none
    public const string VehicleSaveGrip = "JNGAAMMNJAP";
    public const string CarBoomRoutine = "CPJIMKMAPII";          // Car Coroutine: the running boom (5 s fuse, blast, 4 s stun, recover)
    public const string CarBoomBusy = "FBJPPCIKFAP";             // Car bool property (private set): true while that boom runs
    public const string VehicleOilGrip = "LPBFCIDOACN";          // Vehicle internal (forward, sideways): the oil spill's friction multipliers (1, 1 = off)
    public const string VehicleOilSpin = "HKIEJBPMCCK";          // Vehicle internal (steer -1..1): the oil spill's spin-out steering kick         // Vehicle(): saves every wheel's friction ONCE: the baseline GripFor restores
}

// The game's static singletons (each class has its own obfuscated "Instance" field) and static constants.
static class Instances
{
    public static GameController GameController => GameController.OBJNLHEADBH;
    public static VehicleManager VehicleManager => VehicleManager.OBJNLHEADBH;
    public static WaypointController WaypointController => WaypointController.OBJNLHEADBH;
    public static InGameScreenController InGameScreen => InGameScreenController.OBJNLHEADBH;
    public static FollowCam FollowCam => FollowCam.OBJNLHEADBH;
    public static FreeCam FreeCam => FreeCam.OBJNLHEADBH;
    public static CamController CamController => CamController.OBJNLHEADBH;
    public static TwitchCommandListener TwitchListener => TwitchCommandListener.OBJNLHEADBH;
    public static BackendSession Backend => BackendSession.FBGAIFJJAFP;
    public static MinimapView Minimap => MinimapView.FBGAIFJJAFP;
    public static EventManager Events => EventManager.OBJNLHEADBH;   // the RIP feed
    public static MapApi MapApi => ALOAIBGFNHE.FBGAIFJJAFP;
    // The game's own Twitch app client id (a constant, not an instance; it lives here because it is the same kind of static entry point).
    public static string TwitchClientId => ApplicationController.PJCEJFDNAON;
}

static class VehicleExtensions
{
    public static RacerProfile Profile(this Vehicle vehicle) => vehicle.ILKNIPGLDFH;
    public static GameObject Car(this Vehicle vehicle) => vehicle.HAGKFJDFLEF;          // null until the car is spawned
    public static float Progress(this Vehicle vehicle) => vehicle.FBCBIDACOCO;          // route distance driven
    public static void SetProgress(this Vehicle vehicle, float distance) => vehicle.FBCBIDACOCO = distance;
    public static bool HasFinished(this Vehicle vehicle) => vehicle.LJONLDCDKHA;
    public static VehicleType Type(this Vehicle vehicle) => vehicle.DMFPKPEOLLG;
    public static void Explode(this Vehicle vehicle) => vehicle.DJKPOLHJOOK(null);   // by = who caused it, shown in the event feed; null = nobody named
    public static void Explode(this Vehicle vehicle, string by) => vehicle.DJKPOLHJOOK(by);   // the game takes "by" but (Oct 10 build) no longer shows it
    // The game's timed grip effect: every wheel's forward + sideways friction stiffness x multiplier for `seconds`, then
    // restored from the friction it saved the FIRST time (it never re-reads it). A new call replaces a running one.
    public static void GripFor(this Vehicle vehicle, float multiplier, float seconds) => vehicle.LFPECNADCHH(multiplier, seconds);
    public static bool UseBoost(this Vehicle vehicle) => vehicle.GEOHOLHIKPC();
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
    public static void BoomBlast(this Car car) => car.HNJAIJPHIID();     // engine off, AI stops driving: the "dead" car
    public static void BoomRecover(this Car car) => car.MLLPEBDAPHC();   // back on the road, driving again
    public static Vehicle NewVehicle(RacerProfile profile) => new Vehicle(profile);
}

static class RacerProfileExtensions
{
    public static string TwitchId(this RacerProfile profile) => profile.FDPLKNMBAGH;
    public static string Login(this RacerProfile profile) => profile.NOFAPJHCNNJ;
    public static string DisplayName(this RacerProfile profile) => profile.FDEEJKLFMLN;
    public static Color Color(this RacerProfile profile) => profile.NPDPOOJBKPO;
    public static void SetColor(this RacerProfile profile, Color color) => profile.NPDPOOJBKPO = color;
    public static bool IsSubscriber(this RacerProfile profile) => profile.IFLEHKGFECK;
    public static string CustomTitle(this RacerProfile profile) => profile.GGNBAMDGBKN;  // backend title ("Developer", "Streamer", ...), "" until it arrives
    public static int BackendId(this RacerProfile profile) => profile.EFONILMFLPN;       // the backend's id for this join; 0 until the backend has answered
    // What the lobby row prints under the name: the backend CustomTitle, else Subscriber / Normal Racer.
    public static string Title(this RacerProfile profile) => RacerTitles.PFFJMGEHKCC(profile);

    public static RacerProfile NewProfile(string twitchId, string login, string displayName, Color color, bool subscriber) => new RacerProfile
    {
        FDPLKNMBAGH = twitchId,
        NOFAPJHCNNJ = login,
        FDEEJKLFMLN = displayName,
        NPDPOOJBKPO = color,
        IFLEHKGFECK = subscriber,
        IMLPKDMJEOI = new RacerStats(),
    };
}

static class TwitchUserExtensions
{
    public static string Id(this TwitchUser user) => user.FDPLKNMBAGH;
    public static string Login(this TwitchUser user) => user.KLAKGCNFNOG;
    public static string AccessToken(this TwitchUser user) => user.FPDIBJKKPBP;
}

static class BackendSessionExtensions
{
    public static TwitchUser CurrentUser(this BackendSession session) => session.NFKDLHGCEOA();
}

static class MapApiExtensions
{
    public static void FetchMaps(this MapApi api, System.Action<MapOverviewListResponse> then) => api.DBALKCOMDEH(response => then(response));
}

// The game's static map playlist: what the map list's play button fills and "next map in queue" consumes.
static class MapQueue
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueList);
    static readonly System.Reflection.FieldInfo IndexField = AccessTools.Field(typeof(MapQueueGame), GameNames.MapQueueIndex);

    public static void Set(List<MapOverviewListItem> playlist, int index) { ListField.SetValue(null, playlist); IndexField.SetValue(null, index); }
    public static bool HasNext() => MapQueueGame.AINPFPHPOJA();
    // Builds the lobby from the Play-tab settings for the next map in the playlist and loads the Play scene.
    public static void LoadNext() => MapQueueGame.KBFKLNFGENG();
}

static class GameControllerExtensions
{
    public static GameSettings CurrentGame(this GameController controller) => controller.NDFMPCEDHEF;
}

static class GameSettingsExtensions
{
    public static int MapId(this GameSettings game) => game.AAIFOPHLFPK;
    public static string MapName(this GameSettings game) => game.KFDACDEHCBL;
}

static class CircuitExtensions
{
    public static Transform[] Waypoints(this WaypointCircuit circuit) => circuit.LFAJGAIFHCK;  // the road itself, in order, closed by a hop back to the start
    public static float Length(this WaypointCircuit circuit) => circuit.DPJFGCALOGO;
    public static Vector3 Position(this RoutePoint point) => point.IFBHNCEAPKN;
    public static Vector3 Direction(this RoutePoint point) => point.DLBAMFACCFO;
}

static class CameraExtensions
{
    public static bool IsActive(this FreeCam cam) => cam.AICHGOHGKIM;
    public static void SetActive(this FreeCam cam, bool active) => cam.AICHGOHGKIM = active;
    public static bool IsActive(this FollowCam cam) => cam.AICHGOHGKIM;
    public static void SetActive(this FollowCam cam, bool active) => cam.AICHGOHGKIM = active;
    public static Transform Target(this FollowCam cam) => cam.JDEGGFPGCBM;
    public static float Yaw(this CamController controller) => controller.ICEJNBOGAJC;
    public static void SetYaw(this CamController controller, float degrees) => controller.ICEJNBOGAJC = degrees;
    public static float Pitch(this CamController controller) => controller.OIOKKNEEJNM;
    public static void SetPitch(this CamController controller, float degrees) => controller.OIOKKNEEJNM = degrees;
    // The child the follow cam attaches to: the smooth thing to track.
    public static GameObject CameraAnchor(this Car car) => car.HONFJJIEAHD;
    public static WheelCollider[] Wheels(this Car car) => car.MMDFJMMDLPC;   // the four WheelColliders (order as the prefab lists them)
}

static class CarControllerExtensions
{
    public static float TopSpeedMultiplier(this CarController controller) => controller.LHOOIDDJFMH;
    public static void SetTopSpeedMultiplier(this CarController controller, float multiplier) => controller.LHOOIDDJFMH = multiplier;
}

static class ScreenExtensions
{
    // What the lobby's START button does: begin the start countdown.
    public static void StartCountdown(this InGameScreenController screen) => screen.GameStartClicked();
}

static class UiItemExtensions
{
    public static UnityEngine.UI.Text NameText(this VehicleLabel label) => label.PDMBILOMPOG;
    public static TMPro.TextMeshProUGUI NameText(this VehicleLeaderboardUIItem item) => item.ILJIJCCAPNN;
    public static RacerProfile Profile(this PreGamePlayerListItem row) => row.ILKNIPGLDFH;
    public static TMPro.TextMeshProUGUI NameText(this PreGamePlayerListItem row) => row.ILJIJCCAPNN;
}

static class VehicleManagerExtensions
{
    // The game dropped HasPlayerAlreadyJoined (Oct 2026 build); same check by login.
    public static bool HasPlayerAlreadyJoined(this VehicleManager manager, string login) => manager.GetVehicleByTwitchUsername(login) != null;
}

// Oct 21 2026 build: the game's readable methods got obfuscated as well. These keep their old names for call sites.
static class RenamedMethods
{
    public static void EndCurrentGame(this GameController controller) => controller.INMJMBIDAFE();
    public static void StartCurrentGame(this GameController controller) => controller.LNIFLEEJLKF();
    public static bool IsGameRunning(this GameController controller) => controller.HFAINFCBDIK();
    public static RoutePoint GetRoutePoint(this WaypointCircuit circuit, float distance) => circuit.ADIAKCLNACC(distance);
    public static WaypointCircuit GetCircuit(this WaypointController controller) => controller.BFKBIKIKLMD();
    public static BCLJJNIKPPA.HBPFHAGIBLF GetCurrentGameScreenState(this InGameScreenController screen) => screen.HNEFNGIPBEF();
    public static void InGameSetup(this InGameScreenController screen) => screen.OJBHPECMFJH();
    public static void Boost(this Car car, float a, float b) => car.GEOHOLHIKPC(a, b);
    public static void SetVehicle(this VehicleLeaderboardUIItem item, Vehicle vehicle) => item.CHLKODMMDBA(vehicle);
    public static void BindVehicles(this MinimapView view, IList<Vehicle> vehicles) => view.BJEPFFJFGEA(vehicles);
    public static bool FollowVehicle(this CamController controller, Vehicle vehicle) => controller.PJEAJIFNJNP(vehicle);
    public static void ReleasePropCam(this CamController controller) => controller.AMIHPMBMNDD();
    public static void Remove(this GameEvent line) => line.OPKIPLICOOB();
    public static Vehicle GetVehicleByTwitchUsername(this VehicleManager manager, string login) => manager.IENLBDCJIPP(login);
    public static List<Vehicle> GetVehicles(this VehicleManager manager) => manager.OLBIIKAJIFM();
    public static bool RemoveVehicleByTwitchUsername(this VehicleManager manager, string login) => manager.MMMGAEOIJFA(login);
    public static void AddVehicle(this VehicleManager manager, Vehicle vehicle) => manager.KJMHFOOGGID(vehicle);
    public static GameEvent AddEvent(this EventManager events, string text, FeedClick onClick) => events.IBOBLGAMHHJ(text, onClick);
    public static void Follow(this FollowCam cam, Vehicle vehicle) => cam.GOGGGEBKJEA(vehicle);
    public static void StopFollow(this FollowCam cam) => cam.HMEAPCODOLC();
    public static bool IsDriving(this CarAIControl ai) => ai.JDALEEAHOFL();
    public static bool BeginSafetyCar(string by) => VirtualSafetyCarController.COBOCDPBGCC(by);
}

static class ChaosStatusExtensions
{
    public static ChaosEffectStatus ChaosStatus() => ChaosEffectStatus.MNNHKFGAOJM();   // GetOrCreate: null before the in-game canvas exists
    public static void PlayShrinkSound(this ChaosEffectStatus status) => status.AMFKFIKPMKN();
}

static class MinimapExtensions
{
    public static MinimapCapture Capture(this MinimapView view) => view.AHGOMOAHAHF;          // null until the map is captured
    public static RenderTexture Picture(this MinimapCapture capture) => capture.NOPGKHBGLEG;  // 512x512, transparent background
    public static Vector3 Centre(this MinimapCapture capture) => capture.BIABMJAFNAM.DMNDHKEAPLO;
    public static Vector2 Extent(this MinimapCapture capture) => capture.BIABMJAFNAM.NAHLHOLCDCE;
}
