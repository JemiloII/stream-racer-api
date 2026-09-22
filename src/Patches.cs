using Cage.StreamRacer;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Vehicles.Car;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StreamRacerApi;

[HarmonyPatch]
static class Patches
{
    // Free cam vertical movement: game hardcodes Q/E. Adds configurable keys on top.
    [HarmonyPrefix, HarmonyPatch(typeof(FreeCam), "PKFKLIPEBJP")]
    static bool FreeCamVertical(FreeCam __instance, bool ___ICHHONAIDDJ)
    {
        float dy = ___ICHHONAIDDJ ? 1f : 0.25f;
        if (Input.GetKey(Plugin.CamUp.Value)) dy = +dy;
        else if (Input.GetKey(Plugin.CamDown.Value)) dy = -dy;
        else return true;
        var pos = __instance.transform.position;
        pos.y += dy;
        Traverse.Create(__instance).Method("CONGPDEJHFM", pos).GetValue();
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.NewGame))]
    static void OnNewGame() { Game.Ended = false; Game._perked.Clear(); Plugin.Instance.StartCoroutine(Settings.OnLobby()); Game.EnsureAvatars(); }

    // Every boom, whether from bits, the API, or anything else, goes through the vehicle's explode method.
    [HarmonyPostfix, HarmonyPatch(typeof(CFBJLEBOFHJ), "NPGMKJHIGNO")]
    static void OnBoom(CFBJLEBOFHJ __instance) { Plugin.Emit("boom", Game.EventDto(__instance)); Cam.OnBoom(__instance); }

    // Pool boosts: chat !boost, the hotkey, /boost/me, bot auto-boost.
    [HarmonyPostfix, HarmonyPatch(typeof(CFBJLEBOFHJ), "MOEACENIMLO")]
    static void OnPoolBoost(CFBJLEBOFHJ __instance, bool __result) { if (__result) Plugin.Emit("boost", Game.EventDto(__instance)); }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.StartCurrentGame))]
    static void OnStart() { Game.ResetBoomTracking(); Game.EnsureAvatars(); Cam.OnRaceStart(); Game.NoteRaceStart(); Plugin.Instance.StartCoroutine(Minimap.WhenRunning()); Plugin.Instance.StartCoroutine(Game.AutoBoostRace()); Plugin.Emit("race_start", Game.Snapshot()); Game.EmitBoostPools(); }

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.AddFinisherToCurrentGame))]
    static void OnFinisher(CFBJLEBOFHJ KGEKACHOBHK)
    {
        var ranked = Game.Ranked();
        Plugin.Emit("finisher", Game.Dto(KGEKACHOBHK, ranked.IndexOf(KGEKACHOBHK) + 1));
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
    static void BeforeAdd(CFBJLEBOFHJ LOHGEJIOPJL) { if (LOHGEJIOPJL != null) Game.ApplySavedColor(LOHGEJIOPJL); }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleManager), nameof(VehicleManager.AddVehicle))]
    static void OnAdd(CFBJLEBOFHJ LOHGEJIOPJL)
    {
        if (VehicleManager.NEPFAEJAMGI.HasPlayerAlreadyJoined(Game.Login(LOHGEJIOPJL)))
        {
            Game.EnsureAvatars();
            Game.ApplySavedColor(LOHGEJIOPJL);
            Plugin.Instance.StartCoroutine(Game.AnnounceTitle(LOHGEJIOPJL));
            Plugin.Emit("joined", Game.Dto(LOHGEJIOPJL, 0));
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FollowCam), nameof(FollowCam.Update))]
    static void BeforeFollowCam() => Cam.PanFollow();

    [HarmonyPostfix, HarmonyPatch(typeof(FreeCam), nameof(FreeCam.LateUpdate))]
    static void AfterFreeCam(FreeCam __instance) { Cam.Tick(__instance); Cam.RefreshLabels(); }

    // Every chat message the game sees (the same handler that handles !race and bits): our chat commands.
    [HarmonyPostfix, HarmonyPatch(typeof(TwitchCommandListener), "CHOJMKCDOGG")]
    static void OnChat(TwitchLib.Client.Events.OnMessageReceivedArgs KGCAKNNGBDA)
    {
        try { var m = KGCAKNNGBDA?.ChatMessage; if (m != null) Game.OnChatMessage(m.Username, m.Message, m.DisplayName); } catch { }
    }

    // AI driver's per-frame speed logic overwrites the multiplier; reapply ours after it.
    [HarmonyPostfix, HarmonyPatch(typeof(CarAIControl), "AEPCFOLDGFE")]
    static void AfterAiSpeed(CarController ___GBHAMILJILE) => Game.ApplySlow(___GBHAMILJILE);

    // Lobby / results list rows: after the game fills a row, swap in our image if one was registered.
    // The obfuscator emits several look-alike setup methods; patch every public one taking the viewer-info type.
    [HarmonyPatch]
    static class ListItemImage
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            new[] { typeof(PreGamePlayerListItem), typeof(PostGamePlayerListItem) }
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(CKINOOFAKJL));

        static void Prefix(MonoBehaviour __instance, object[] __args)
        {
            var img = __instance.GetType().GetField("LJLJGFLLMOF")?.GetValue(__instance) as UnityEngine.UI.RawImage;
            Game.RegisterRowImage(__args[0] as CKINOOFAKJL, img);
        }

        static void Postfix(MonoBehaviour __instance)
        {
            var t = __instance.GetType();
            var who = t.GetField("JDDOIMHIFHK")?.GetValue(__instance) as CKINOOFAKJL;
            var img = t.GetField("LJLJGFLLMOF")?.GetValue(__instance) as UnityEngine.UI.RawImage;
            Game.ApplyImage(who, img, __instance);
            Game.ColorRowName(who, t.GetField("AKAMDOKJAIE")?.GetValue(__instance) as TMPro.TextMeshProUGUI); // lobby/results name in the car's color
        }
    }

    // The game's Twitch-avatar loader (string id, RawImage target): skip it for rows we own.
    [HarmonyPatch]
    static class GameAvatarLoader
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            typeof(KHECLDCFJHH).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => { var p = m.GetParameters(); return p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(UnityEngine.UI.RawImage); });

        static bool Prefix(object[] __args) => !Game.HasCustomImage(__args[1] as UnityEngine.UI.RawImage);
    }
}
