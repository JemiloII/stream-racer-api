// Internal state as enums. Each converts to the lowercase string the HTTP/SSE contract uses in exactly one place
// (ToApiString), so JSON stays byte-identical while the code compares enum values instead of strings.
namespace StreamRacerApi;

// What the plugin's camera director is doing. None = nothing of ours is active (the game's own camera, or no race).
enum CameraMode { None, Follow, FollowWide, Free, Chase, Front, Pack, Sweep, Side, High, Orbit, Overhead, Prop, Finish, Grid, Manual }

static class CameraModes
{
    // "mode" in GET /camera and the camera event: null when nothing is active.
    public static string ToApiString(this CameraMode mode) => mode switch
    {
        CameraMode.None => null,
        CameraMode.FollowWide => "followwide",
        _ => mode.ToString().ToLowerInvariant(),
    };

    // Custom shots the plugin places every frame from the FreeCam.LateUpdate postfix (as opposed to the game's own follow/free/prop cams).
    public static bool IsSteered(this CameraMode mode) => mode switch
    {
        CameraMode.Chase or CameraMode.Front or CameraMode.Orbit or CameraMode.Pack or CameraMode.Sweep or CameraMode.Side
            or CameraMode.High or CameraMode.Overhead or CameraMode.Finish or CameraMode.Grid => true,
        _ => false,
    };
}

// What a car is doing right now.
enum VehicleState { Driving, Stunned, Air, Flipped, Offroad, Stuck, Finished, Spawning }

static class VehicleStates
{
    public static string ToApiString(this VehicleState state) => state.ToString().ToLowerInvariant();
    // A car that left the road on its own: flipped, off in the grass or stalled (not a boom, not a jump).
    public static bool IsCrashed(this VehicleState state) => state is VehicleState.Flipped or VehicleState.Offroad or VehicleState.Stuck;
}

// Where the game is. Other = a panel/scene/screen-state name the plugin does not know; the raw name is passed through then.
enum GameScreen { Home, Main, Play, Settings, Lobby, Racing, Postgame, TrackBuilder, Loading, Other }

static class GameScreens
{
    public static string ToApiString(this GameScreen screen, string rawName = null) => screen switch
    {
        GameScreen.Other => rawName,
        GameScreen.TrackBuilder => "trackbuilder",
        _ => screen.ToString().ToLowerInvariant(),
    };

    // A lowercase panel / scene / screen-state name from the game -> the enum, or Other (keep the raw name for the API).
    public static GameScreen Parse(string rawName) => rawName switch
    {
        "home" => GameScreen.Home,
        "main" => GameScreen.Main,
        "play" => GameScreen.Play,
        "settings" => GameScreen.Settings,
        "lobby" => GameScreen.Lobby,
        "racing" => GameScreen.Racing,
        "postgame" => GameScreen.Postgame,
        "trackbuilder" => GameScreen.TrackBuilder,
        "loading" => GameScreen.Loading,
        _ => GameScreen.Other,
    };
}
