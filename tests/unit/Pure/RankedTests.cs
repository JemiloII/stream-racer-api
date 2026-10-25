using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Ranked on: which API calls the mod refuses (Pure.RankedBlocks).
public class RankedTests
{
    [Theory]
    [InlineData("grow", "sr_test_a", null)]
    [InlineData("speed", "all", null)]
    [InlineData("drift", "me", null)]
    [InlineData("surface", "ice", null)]
    [InlineData("weather", "snow", null)]
    [InlineData("finish", "sr_test_a", null)]
    [InlineData("boom", "3", null)]
    [InlineData("respawn", "all", null)]
    [InlineData("respawn", "me", null)]
    [InlineData("effect", "vsc", null)]
    [InlineData("boost", "all", null)]
    [InlineData("boost", "sr_test_a", "add")]
    public void The_mods_extras_are_refused(string root, string target, string action)
    {
        Assert.True(RankedBlocks(root, target, action));
    }

    [Fact]
    public void Chaos_runs_in_ranked_only_when_switched_on()
    {
        Assert.True(RankedBlocks("chaos", "meteors", null));
        Assert.False(RankedBlocks("chaos", "meteors", null, chaosInRanked: true));
        Assert.True(RankedBlocks("respawn", "all", null, chaosInRanked: true));
        Assert.True(RankedBlocks("boost", "all", "add", chaosInRanked: true));
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
