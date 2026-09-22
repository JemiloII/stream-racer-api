using System.Collections.Generic;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Camera director toggles: settings.camera.shots[kind] (Pure.ShotEnabled / Pure.ShotKind).
public class CameraTests
{
    [Theory]
    [InlineData("grid", "grid")]
    [InlineData("high", "high")]
    [InlineData("high2", "high")]          // the mirrored high shot shares the high toggle
    [InlineData("front:shibikox", "front")] // per-car keys carry the login after a colon
    [InlineData("wide:shibikox", "wide")]   // the wide follow has no toggle; it is the director's fallback
    [InlineData(" Pileup ", "pileup")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_shot_key_maps_to_its_toggle_name(string key, string expected)
    {
        Assert.Equal(expected, ShotKind(key));
    }

    [Fact]
    public void Every_documented_shot_has_a_key()
    {
        Assert.Equal(new[] { "grid", "high", "side", "sweep", "pack", "front", "chase", "orbit", "overhead", "prop", "finish", "duel", "pileup", "boom" }, ShotKeys);
    }

    [Fact]
    public void A_shot_switched_off_is_skipped_by_the_director()
    {
        var shots = new Dictionary<string, bool> { ["grid"] = false, ["high"] = true };
        Assert.False(ShotEnabled(shots, "grid"));
        Assert.True(ShotEnabled(shots, "high"));
        Assert.False(ShotEnabled(new Dictionary<string, bool> { ["high"] = false }, "high2"));
        Assert.False(ShotEnabled(new Dictionary<string, bool> { ["front"] = false }, "front:shibikox"));
    }

    [Fact]
    public void A_missing_toggle_means_on()
    {
        Assert.True(ShotEnabled(new Dictionary<string, bool>(), "sweep"));
        Assert.True(ShotEnabled(null, "sweep"));
        Assert.True(ShotEnabled(new Dictionary<string, bool> { ["grid"] = false }, "wide:shibikox"));
    }
}
