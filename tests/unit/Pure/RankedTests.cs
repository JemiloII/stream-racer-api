using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Ranked on: which API calls the mod refuses (Pure.RankedBlocks).
public class RankedTests
{
    [Theory]
    [InlineData("shrink", null, null)]
    [InlineData("grow", "sr_test_a", null)]
    [InlineData("speed", "all", null)]
    [InlineData("drift", "me", null)]
    [InlineData("surface", "ice", null)]
    [InlineData("weather", "snow", null)]
    [InlineData("finish", "sr_test_a", null)]
    [InlineData("boom", "3", null)]
    [InlineData("respawn", "all", null)]
    [InlineData("respawn", "me", null)]
    [InlineData("effect", "nuke", null)]
    [InlineData("boost", "all", null)]
    [InlineData("boost", "sr_test_a", "add")]
    public void The_mods_extras_are_refused(string root, string target, string action)
    {
        Assert.True(RankedBlocks(root, target, action));
    }

    [Theory]
    [InlineData("join", null, null)]
    [InlineData("lobby", null, null)]
    [InlineData("camera", "chase", "sr_test_a")]
    [InlineData("color", "sr_test_a", null)]
    [InlineData("boost", "me", null)]
    [InlineData("boost", "sr_test_a", "use")]
    [InlineData("race", "start", null)]
    public void The_game_itself_and_the_presentation_still_work(string root, string target, string action)
    {
        Assert.False(RankedBlocks(root, target, action));
    }
}
