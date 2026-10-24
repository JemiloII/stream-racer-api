// Game.Effects: the game's own chaos effects fired through their own controllers (the same entry the game's Twitch
// extension uses). Only the virtual safety car is offered; bananas, oil, cones, nuke and shrink were taken out on
// purpose. POST /effect/vsc.

namespace StreamRacerApi;

static partial class Game
{
    public static readonly string[] EffectNames = { "vsc" };

    // Null = started; otherwise why not.
    public static string StartEffect(string name, string by)
    {
        if (!Running)
        {
            return "no race running";
        }
        if ((name ?? "").ToLowerInvariant() != "vsc")
        {
            return "unknown effect (vsc)";
        }
        if (!RenamedMethods.BeginSafetyCar(by))
        {
            return "the game refused it (not racing, or the safety car is already out)";
        }
        Plugin.Emit("effect", new { effect = "vsc", by });
        return null;
    }
}
