using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Mini map: the aspect setting and the in-game box height (Pure.AspectRatio / Pure.MapHeight).
public class MinimapTests
{
    const float TrackRatio = 2.5f;
    const float ScreenWidth = 1920f;
    const float ScreenHeight = 1080f;

    [Theory]
    [InlineData("16:9", 1.777f)]
    [InlineData("1:1", 1f)]
    [InlineData("4:3", 1.333f)]
    [InlineData(" 21:9 ", 2.333f)]
    public void A_width_to_height_setting_becomes_a_ratio(string aspectSetting, float expectedRatio)
    {
        float ratio = AspectRatio(aspectSetting, TrackRatio);

        Assert.InRange(ratio, expectedRatio - 0.01f, expectedRatio + 0.01f);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0:5")]
    [InlineData("16:-9")]
    [InlineData("x:y")]
    [InlineData("16/9")]
    public void Anything_else_falls_back_to_the_track_shape(string aspectSetting)
    {
        Assert.Equal(TrackRatio, AspectRatio(aspectSetting, TrackRatio));
    }

    [Fact]
    public void A_square_box_on_a_widescreen_takes_more_height_than_width()
    {
        float widthFraction = 0.18f;

        float heightFraction = MapHeight(widthFraction, ScreenWidth, ScreenHeight, aspect: 1f);

        Assert.InRange(heightFraction, 0.31f, 0.33f); // 0.18 * (1920 / 1080)
    }

    [Fact]
    public void A_box_shaped_like_the_screen_keeps_the_same_fraction_on_both_sides()
    {
        float widthFraction = 0.18f;

        float heightFraction = MapHeight(widthFraction, ScreenWidth, ScreenHeight, aspect: 16f / 9f);

        Assert.InRange(heightFraction, 0.175f, 0.185f);
    }

    [Fact]
    public void Height_never_exceeds_the_screen()
    {
        float heightFraction = MapHeight(0.9f, ScreenWidth, ScreenHeight, aspect: 0.2f);

        Assert.Equal(0.95f, heightFraction);
    }

    [Fact]
    public void Height_never_collapses_to_nothing()
    {
        float heightFraction = MapHeight(0.001f, ScreenWidth, ScreenHeight, aspect: 10f);

        Assert.Equal(0.03f, heightFraction);
    }
}
