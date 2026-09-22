// Harmony patches: where the game calls into the plugin. Race lifecycle events, booms and pool boosts, joins,
// the free-cam keys, the camera steering hooks, chat messages, the slow-down re-apply, and the lobby/results row
// image + name color swaps. Obfuscated targets are named through GameNames.
using System.Reflection;
using HarmonyLib;
using UnityStandardAssets.Vehicles.Car;

namespace StreamRacerApi;

[HarmonyPatch]
static class Patches
{
    // Free cam vertical movement: game hardcodes Q/E. Adds configurable keys on top.
    static readonly AccessTools.FieldRef<FreeCam, bool> FreeCamFast = AccessTools.FieldRefAccess<FreeCam, bool>(GameNames.FreeCamFastFlag);
    [HarmonyPrefix, HarmonyPatch(typeof(FreeCam), GameNames.FreeCamVerticalMove)]
    static bool FreeCamVertical(FreeCam __instance)
    {
        float step = FreeCamFast(__instance) ? 1f : 0.25f;
        if (Input.GetKey(Plugin.CamUp.Value)) step = +step;
        else if (Input.GetKey(Plugin.CamDown.Value)) step = -step;
        else return true;
        var position = __instance.transform.position;
        position.y += step;
        Traverse.Create(__instance).Method(GameNames.FreeCamSetPosition, position).GetValue();
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.NewGame))]
    static void OnNewGame() { Game.Ended = false; Game.ForgetPerks(); Plugin.Instance.StartCoroutine(Settings.OnLobby()); Game.EnsureAvatars(); }

    // Every boom, whether from bits, the API, or anything else, goes through the vehicle's explode method.
    [HarmonyPostfix, HarmonyPatch(typeof(Vehicle), GameNames.VehicleExplode)]
    static void OnBoom(Vehicle __instance) { Plugin.Emit("boom", Game.EventDto(__instance)); Cam.OnBoom(__instance); }

    // Pool boosts: chat !boost, the hotkey, /boost/me, bot auto-boost.
    [HarmonyPostfix, HarmonyPatch(typeof(Vehicle), GameNames.VehicleUseBoost)]
    static void OnPoolBoost(Vehicle __instance, bool __result) { if (__result) Plugin.Emit("boost", Game.EventDto(__instance)); }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.StartCurrentGame))]
    static void OnStart()
    {
        Game.ResetBoomTracking(); Game.EnsureAvatars();
        Plugin.Instance.StartCoroutine(Minimap.WhenRunning()); Plugin.Instance.StartCoroutine(Game.AutoBoostRace());
        Plugin.Emit("race_start", Game.Snapshot()); Game.EmitBoostPools();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.AddFinisherToCurrentGame))]
    static void OnFinisher([HarmonyArgument(GameNames.FinisherArg)] Vehicle finisher)
    {
        var ranked = Game.Ranked();
        Plugin.Emit("finisher", Game.Dto(finisher, ranked.IndexOf(finisher) + 1));
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), nameof(GameController.EndCurrentGame))]
    static void EndPrefix(out bool __state) => __state = Game.Running;

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.EndCurrentGame))]
    static void EndPostfix(bool __state)
    {
        if (__state) { Game.Ended = true; Cam.OnRaceEnd(); Minimap.Destroy(); Plugin.Emit("race_end", Game.Snapshot()); }
    }

    // Saved color before the game builds the lobby row and spawns the preview, so the car and its row match from the start.
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleManager), nameof(VehicleManager.AddVehicle))]
    static void BeforeAdd([HarmonyArgument(GameNames.AddVehicleArg)] Vehicle vehicle) { if (vehicle != null) Game.ApplySavedColor(vehicle); }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleManager), nameof(VehicleManager.AddVehicle))]
    static void OnAdd([HarmonyArgument(GameNames.AddVehicleArg)] Vehicle vehicle)
    {
        if (Instances.VehicleManager.HasPlayerAlreadyJoined(Game.Login(vehicle)))
        {
            Game.EnsureAvatars();
            Game.ApplySavedColor(vehicle);
            Plugin.Instance.StartCoroutine(Game.AnnounceTitle(vehicle));
            Plugin.Emit("joined", Game.Dto(vehicle, 0));
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FollowCam), nameof(FollowCam.Update))]
    static void BeforeFollowCam() => Cam.PanFollow();

    [HarmonyPostfix, HarmonyPatch(typeof(FreeCam), nameof(FreeCam.LateUpdate))]
    static void AfterFreeCam(FreeCam __instance) { Cam.Tick(__instance); Cam.RefreshLabels(); }

    // Every chat message the game sees (the same handler that handles !race and bits): our chat commands.
    [HarmonyPostfix, HarmonyPatch(typeof(TwitchCommandListener), GameNames.ChatMessageReceived)]
    static void OnChat([HarmonyArgument(GameNames.ChatMessageArg)] TwitchLib.Client.Events.OnMessageReceivedArgs args)
    {
        try { var message = args?.ChatMessage; if (message != null) Game.OnChatMessage(message.Username, message.Message, message.DisplayName); } catch { }
    }

    // AI driver's per-frame speed logic overwrites the multiplier; reapply ours after it.
    static readonly AccessTools.FieldRef<CarAIControl, CarController> AiCarController = AccessTools.FieldRefAccess<CarAIControl, CarController>(GameNames.AiCarController);
    [HarmonyPostfix, HarmonyPatch(typeof(CarAIControl), GameNames.AiSpeedUpdate)]
    static void AfterAiSpeed(CarAIControl __instance) => Game.ApplySlow(AiCarController(__instance));

    // Lobby / results list rows: after the game fills a row, swap in our image if one was registered.
    // The obfuscator emits several look-alike setup methods; patch every public one taking the racer profile type.
    [HarmonyPatch]
    static class ListItemImage
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            new[] { typeof(PreGamePlayerListItem), typeof(PostGamePlayerListItem) }
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(method => method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(RacerProfile));

        static void Prefix(MonoBehaviour __instance, object[] __args)
        {
            var avatar = __instance.GetType().GetField(GameNames.ListItemAvatar)?.GetValue(__instance) as UnityEngine.UI.RawImage;
            Game.RegisterRowImage(__args[0] as RacerProfile, avatar);
        }

        static void Postfix(MonoBehaviour __instance)
        {
            var rowType = __instance.GetType();
            var profile = rowType.GetField(GameNames.ListItemProfile)?.GetValue(__instance) as RacerProfile;
            var avatar = rowType.GetField(GameNames.ListItemAvatar)?.GetValue(__instance) as UnityEngine.UI.RawImage;
            Game.ApplyImage(avatar, __instance);
            Game.ColorRowName(profile, rowType.GetField(GameNames.ListItemName)?.GetValue(__instance) as TMPro.TextMeshProUGUI); // lobby/results name in the car's color
        }
    }

    // The game's Twitch-avatar loader (string id, RawImage target): skip it for rows we own.
    [HarmonyPatch]
    static class GameAvatarLoader
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            typeof(AvatarLoader).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => { var parameters = method.GetParameters(); return parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(UnityEngine.UI.RawImage); });

        static bool Prefix(object[] __args) => !Game.HasCustomImage(__args[1] as UnityEngine.UI.RawImage);
    }
}
