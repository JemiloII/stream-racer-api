using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

public class RespawnTests
{
    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 5, 0)]
    public void Counts_down_from_the_limit_and_stops_at_zero(int limit, int used, int expectedLeft) => Assert.Equal(expectedLeft, RespawnsLeft(limit, used));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 99)]
    [InlineData(-3, 1)]
    public void A_limit_of_zero_means_unlimited(int limit, int used) => Assert.Equal(-1, RespawnsLeft(limit, used));
}
