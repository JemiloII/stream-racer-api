// Game.Effects: the game's own chaos effects (Oct 10 2026 build), fired through each effect's own controller, the
// same entry its Twitch extension uses: bananas, oil, cones, nuke, vsc (virtual safety car). The controllers do the
// visuals, banners and rules; `by` is who gets the credit on the banner. POST /effect/:name.

namespace StreamRacerApi;

static partial class Game
{
    public static readonly string[] EffectNames = { "bananas", "oil", "cones", "nuke", "vsc" };

    // Null = started; otherwise why not.
    public static string StartEffect(string name, string by)
    {
        if (!Running)
        {
            return "no race running";
        }
        bool started;
        switch ((name ?? "").ToLowerInvariant())
        {
            case "bananas":
                started = BananaPeelController.Begin(by);
                break;
            case "oil":
                started = OilSpillController.Begin(by);
                break;
            case "cones":
                started = TrafficConeController.Begin(by);
                break;
            case "nuke":
                started = NukeController.Begin(by);
                break;
            case "vsc":
                started = VirtualSafetyCarController.Begin(by);
                break;
            default:
                return "unknown effect (bananas, oil, cones, nuke, vsc)";
        }
        if (!started)
        {
            return "the game refused it (not racing, or that effect is already running)";
        }
        Plugin.Emit("effect", new { effect = name.ToLowerInvariant(), by });
        return null;
    }
}
