// Harmony patches: where the game calls into the plugin. Race lifecycle events, booms and pool boosts, joins,
// the free-cam keys, the camera steering hooks, chat messages, the slow-down re-apply, and the lobby/results row
// image + name color swaps. Obfuscated targets are named through GameNames.
using System.Reflection;
using HarmonyLib;

namespace StreamRacerApi;

[HarmonyPatch]
static class Patches
{
    // Free cam vertical movement: the game hardcodes Q/E inside LateUpdate (15/s, 60/s with Shift, y clamped 3..250).
    // Adds the configurable keys on top, at the same speed, from the LateUpdate postfix.
    static void FreeCamVertical(FreeCam freeCam)
    {
        float direction = Input.GetKey(Plugin.CamUp.Value) ? 1f : Input.GetKey(Plugin.CamDown.Value) ? -1f : 0f;
        if (direction == 0f || !freeCam.IsActive()) return;
        float speed = Input.GetKey(KeyCode.LeftShift) ? 60f : 15f;
        var position = freeCam.transform.position;
        position.y = Mathf.Clamp(position.y + direction * speed * Time.deltaTime, 3f, 250f);
        freeCam.transform.position = position;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), GameNames.NewGame)]
    static void OnNewGame() { Weather.EndOfRace(); Cam.OnRaceEnd(); Game.Ended = false; Game.ForgetPerks(); Plugin.Instance.StartCoroutine(Settings.OnLobby()); Game.EnsureAvatars(); }

    // Every boom, whether from bits, the API, or anything else, goes through the vehicle's explode method.
    [HarmonyPostfix, HarmonyPatch(typeof(Vehicle), GameNames.VehicleExplode, typeof(string))]   // Explode() just calls Explode(null)
    // __0 = Explode's `by`: null for the game's own booms (bits, API, chat); our chaos names its cause.
    static void OnBoom(Vehicle __instance, string __0)
    {
        var payload = Newtonsoft.Json.Linq.JObject.FromObject(Game.EventDto(__instance));
        payload["by"] = __0;
        Plugin.Emit("boom", payload);
        Cam.OnBoom(__instance);
    }

    // Pool boosts: chat !boost, the hotkey, /boost/me, bot auto-boost.
    [HarmonyPostfix, HarmonyPatch(typeof(Vehicle), GameNames.VehicleUseBoost)]
    static void OnPoolBoost(Vehicle __instance, bool __result)
    {
        if (!__result)
        {
            return;
        }
        if (Game.Login(__instance) == Game.StreamerLogin)
        {
            Plugin.Log.LogInfo("streamer boost from: " + string.Join(" < ", new System.Diagnostics.StackTrace().GetFrames()
                .Skip(1).Take(6).Select(frame => frame.GetMethod()?.DeclaringType?.Name + "." + frame.GetMethod()?.Name)));
        }
        Plugin.Emit("boost", Game.EventDto(__instance));
        Chaos.OnBoost(__instance);   // in an Uma race: the girl's "Boost" skill pop-up
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), GameNames.StartCurrentGame)]
    static void OnStart()
    { Game.ForgetLearnedFinish(); Game.ForgetFinishOrder();
        Game.ResetBoomTracking(); Game.EnsureAvatars();
        Plugin.Instance.StartCoroutine(Cam.KeepCarsSmooth()); Plugin.Instance.StartCoroutine(Game.AutoBoostRace());
        Plugin.Emit("race_start", Game.Snapshot()); Game.EmitBoostPools();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), GameNames.AddFinisher)]
    static void OnFinisher([HarmonyArgument(GameNames.FinisherArg)] Vehicle finisher)
    {
        Game.LearnFinishDistance(finisher);   // now we know exactly how far the line is: everyone else's percent follows it
        Plugin.Emit("finisher", Game.Dto(finisher, Game.NoteFinished(finisher)));
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), GameNames.EndCurrentGame)]
    static void EndPrefix(out bool __state) => __state = Game.Running;

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), GameNames.EndCurrentGame)]
    static void EndPostfix(bool __state)
    {
        Weather.EndOfRace();   // weather + laid surfaces end with the race
        if (__state)
        {
            Game.Ended = true;
            Game.StopDrifts();
            Cam.OnRaceEnd();
            Game.RecordRace();
            Plugin.Emit("race_end", Game.Snapshot());
        }
    }

    // settings.sendResults = true: racers with no Twitch account (Wa-chan) race but are left out of the upload.
    // settings.sendResults = false: the race result never leaves this PC. The game gets the answer it builds itself for
    // local test maps (the request echoed back: every racer, same race id, same places, no points), so its post-race
    // screen runs and the Grand Prix accepts it (it rejects anything that isn't that echo with a RETRY menu).
    [HarmonyPrefix, HarmonyPatch(typeof(MapApi), GameNames.ResultUpload)]
    static bool KeepResultsLocal(RaceResultRequest __0, RaceResultCallback __2)   // (request, local, onReply, onError)
    {
        if (Settings.Current.sendResults)
        {
            Game.DropIdless(__0);
            return true;
        }
        __2?.Invoke(Game.LocalRaceResult(__0));
        return false;
    }

    // Saved color before the game builds the lobby row and spawns the preview, so the car and its row match from the start.
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleManager), GameNames.AddVehicle)]
    static void BeforeAdd([HarmonyArgument(GameNames.AddVehicleArg)] Vehicle vehicle) { if (vehicle != null) Game.ApplySavedColor(vehicle); }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleManager), GameNames.AddVehicle)]
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

    // Alt-tabbed: the game's follow cam returns early while the window is unfocused (meant to ignore the mouse), so it
    // stopped placing itself and follow / chase shots froze on stream. Place it anyway; input stays ignored.
    static readonly MethodInfo FollowCamPlace = AccessTools.Method(typeof(FollowCam), GameNames.FollowCamPlace);
    [HarmonyPostfix, HarmonyPatch(typeof(FollowCam), nameof(FollowCam.LateUpdate))]
    static void AfterFollowCam(FollowCam __instance)
    {
        if (!Application.isFocused) FollowCamPlace?.Invoke(__instance, null);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FollowCam), nameof(FollowCam.LateUpdate))]
    static void BeforeFollowCam() => Cam.PanFollow();

    [HarmonyPostfix, HarmonyPatch(typeof(FreeCam), nameof(FreeCam.LateUpdate))]
    static void AfterFreeCam(FreeCam __instance)
    {
        FreeCamVertical(__instance);
        Cam.Tick(__instance);
        Cam.RefreshLabels();
    }

    // Every chat message the game sees (the same handler that handles !race and bits): our chat commands.
    [HarmonyPostfix, HarmonyPatch(typeof(TwitchCommandListener), GameNames.ChatMessageReceived)]
    static void OnChat([HarmonyArgument(GameNames.ChatMessageArg)] TwitchLib.Client.Events.OnMessageReceivedArgs args)
    {
        try
        {
            var message = args?.ChatMessage; if (message == null) return;
            Game.NoteTwitchColor(message.Username, message.ColorHex);   // their own chat colour, used when they never picked one
            Game.OnChatMessage(message.Username, message.Message, message.DisplayName);
        }
        catch { }
        Chaos.Guard("chat", () => Chaos.OnChat(args?.ChatMessage));   // Gigantify an Emote (Chaos.Giantify)
    }

    // AI driver's per-frame speed logic overwrites the multiplier; reapply ours after it.
    static readonly AccessTools.FieldRef<CarAIControl, CarController> AiCarController = AccessTools.FieldRefAccess<CarAIControl, CarController>(GameNames.AiCarController);
    [HarmonyPostfix, HarmonyPatch(typeof(CarAIControl), GameNames.AiSpeedUpdate)]
    static void AfterAiSpeed(CarAIControl __instance) => Game.ApplySlow(AiCarController(__instance));

    // Road surfaces (mud, snow, puddles): their top-speed cut and drag, after the AI's own speed logic too.
    [HarmonyPostfix, HarmonyPatch(typeof(CarAIControl), GameNames.AiSpeedUpdate)]
    static void AfterAiSurface(CarAIControl __instance) => Game.ApplySurface(AiCarController(__instance));

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

    // Each join reply's driver portrait, by login (Chaos.UmaDriver tells customised drivers from generated ones by it).
    [HarmonyPatch(typeof(DriverPortraits), GameNames.PortraitStoreAdd)]
    static class RememberPortrait
    {
        static void Postfix(RacerProfile __0, Cage.StreamRacer.Drivers.DriverPortrait __1) => Chaos.NotePortrait(__0, __1);
    }

    // Every "next map in queue" (ours, the Play tab's, the post-race next) skips settings.hiddenMaps.
    [HarmonyPatch(typeof(MapQueueGame), GameNames.MapQueueLoadNext)]
    static class SkipHiddenMaps
    {
        static void Prefix() => MapQueue.SkipHidden();
    }

    // The game's avatar painter (RawImage target, RacerProfile, DriverPortrait, ...; since game 2.7 it shows the driver
    // portrait, else the Twitch picture): skip it for rows we own.
    [HarmonyPatch]
    static class GameAvatarLoader
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            typeof(AvatarLoader).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => { var parameters = method.GetParameters(); return parameters.Length >= 2 && parameters[0].ParameterType == typeof(UnityEngine.UI.RawImage) && parameters[1].ParameterType == typeof(RacerProfile); });

        static bool Prefix(MethodBase __originalMethod, object[] __args)
        {
            var target = __args[0] as UnityEngine.UI.RawImage;
            if (!Game.HasCustomImage(target)) return true;
            Game.RememberPainter(target, __originalMethod, __args);
            return false;
        }
    }
}
