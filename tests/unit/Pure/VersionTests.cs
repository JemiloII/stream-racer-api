using System;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Update check: dotted version strings compared part by part (Pure.CompareVersions).
public class VersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1", "1.0.0")]
    [InlineData("", "0")]
    [InlineData(null, "0.0")]
    public void Versions_that_differ_only_by_trailing_zeros_are_equal(string left, string right)
    {
        Assert.Equal(0, CompareVersions(left, right));
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9")]   // numeric, not lexical: 10 > 9
    [InlineData("1.25.0", "1.3.0")]   // 25 > 3
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.0.1", null)]
    [InlineData("2", "1.99.99")]
    public void Newer_version_compares_greater_in_both_directions(string newer, string older)
    {
        Assert.Equal(1, Math.Sign(CompareVersions(newer, older)));
        Assert.Equal(-1, Math.Sign(CompareVersions(older, newer)));
    }

    [Theory]
    [InlineData("1.x.0", "1.0.0")]
    [InlineData("1.beta", "1")]
    public void Non_numeric_parts_count_as_zero(string oddVersion, string plainVersion)
    {
        Assert.Equal(0, CompareVersions(oddVersion, plainVersion));
    }
}
