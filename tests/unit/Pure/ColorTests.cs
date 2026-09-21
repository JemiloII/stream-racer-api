using System.Linq;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Auto colors for bots that did not pick one (Pure.AutoColorHex).
public class ColorTests
{
    [Fact]
    public void The_same_login_always_gets_the_same_color()
    {
        Assert.Equal(AutoColorHex("ttschan"), AutoColorHex("ttschan"));
    }

    [Fact]
    public void Login_case_does_not_change_the_color()
    {
        Assert.Equal(AutoColorHex("TTSchan"), AutoColorHex("ttschan"));
    }

    [Theory]
    [InlineData("funtoon")]
    [InlineData("")]
    [InlineData(null)]
    public void Every_color_comes_from_the_palette(string login)
    {
        Assert.Contains(AutoColorHex(login), Palette);
    }

    [Fact]
    public void Different_logins_spread_across_the_palette()
    {
        var logins = new[]
        {
            "elonmusk", "mrbeast6000", "snoopdogg", "ishowspeed", "terrycrews",
            "jimmyfallon", "zackrawrr", "pewdiepie", "markiplier", "jacksepticeye",
        };

        int distinctColorCount = logins.Select(AutoColorHex).Distinct().Count();

        Assert.True(distinctColorCount >= 5, $"only {distinctColorCount} distinct colors for {logins.Length} logins");
    }
}
