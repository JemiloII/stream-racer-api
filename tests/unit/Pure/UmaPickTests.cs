using System.Collections.Generic;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// !race uma <name>: which exported girl a viewer means (Pure.MatchUma).
public class UmaPickTests
{
    static readonly Dictionary<string, string> Names = new()
    {
        ["1007"] = "Gold Ship",
        ["1013"] = "Mejiro McQueen",
        ["1025"] = "Mejiro Ryan",
        ["1001"] = "Special Week",
    };

    [Theory]
    [InlineData("Gold Ship", "1007")]
    [InlineData("goldship", "1007")]
    [InlineData("mcqueen", "1013")]
    [InlineData("SPECIAL-WEEK", "1001")]
    public void A_name_or_a_unique_part_of_one_picks_her(string query, string id)
    {
        Assert.Equal(id, MatchUma(query, Names, out _));
    }

    [Fact]
    public void Several_matches_pick_nobody_and_list_them()
    {
        Assert.Null(MatchUma("mejiro", Names, out var close));
        Assert.Equal(new[] { "Mejiro McQueen", "Mejiro Ryan" }, close);
    }

    [Fact]
    public void No_match_picks_nobody()
    {
        Assert.Null(MatchUma("haru urara", Names, out var close));
        Assert.Empty(close);
    }
}
