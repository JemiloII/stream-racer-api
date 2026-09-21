using UnityEngine;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Boost zones: the straights a bot should spend its boosts on (Pure.Zones / Pure.InZone).
public class ZonesTests
{
    // Sample route: 200 units straight along +x, a 90 degree corner over 40 units, 200 units straight along +z, then a steep hill.
    const float FirstStraightEnd = 200f;
    const float CornerEnd = 240f;
    const float SecondStraightEnd = 440f;
    const float RouteLength = 600f;

    static (Vector3 pos, Vector3 dir) SampleRoute(float routeDistance)
    {
        if (routeDistance < FirstStraightEnd)
            return (new Vector3(routeDistance, 0, 0), Vector3.right);

        if (routeDistance < CornerEnd)
        {
            float cornerFraction = (routeDistance - FirstStraightEnd) / (CornerEnd - FirstStraightEnd);
            float headingAngle = cornerFraction * Mathf.PI / 2;
            var heading = new Vector3(Mathf.Cos(headingAngle), 0, Mathf.Sin(headingAngle));
            return (new Vector3(FirstStraightEnd + cornerFraction * 20, 0, cornerFraction * 20), heading);
        }

        if (routeDistance < SecondStraightEnd)
            return (new Vector3(220, 0, 20 + (routeDistance - CornerEnd)), Vector3.forward);

        float climbDistance = routeDistance - SecondStraightEnd;
        return (new Vector3(220, climbDistance * 0.5f, 220 + climbDistance), Vector3.forward); // slope 0.5, well past the 0.12 limit
    }

    static (Vector3 pos, Vector3 dir) FlatLine(float routeDistance) => (new Vector3(routeDistance, 0, 0), Vector3.right);

    [Fact]
    public void Corner_and_hill_split_the_route_into_two_straights()
    {
        var zones = Zones(SampleRoute, RouteLength);

        Assert.Equal(2, zones.Count);
    }

    [Fact]
    public void First_straight_begins_where_the_route_begins()
    {
        var zones = Zones(SampleRoute, RouteLength);

        Assert.Equal(0f, zones[0][0]);
    }

    [Fact]
    public void First_straight_ends_around_the_corner()
    {
        var zones = Zones(SampleRoute, RouteLength);

        Assert.InRange(zones[0][1], 160f, 220f);
    }

    [Fact]
    public void Second_straight_starts_after_the_corner_and_ends_before_the_hill()
    {
        var zones = Zones(SampleRoute, RouteLength);

        Assert.InRange(zones[1][0], 220f, 260f);
        Assert.InRange(zones[1][1], 400f, 460f);
    }

    [Fact]
    public void A_flat_line_is_one_zone_that_runs_to_the_end_of_the_route()
    {
        var zones = Zones(FlatLine, RouteLength);

        var onlyZone = Assert.Single(zones);
        Assert.Equal(0f, onlyZone[0]);
        Assert.Equal(RouteLength, onlyZone[1]);
    }

    [Fact]
    public void Straights_shorter_than_the_minimum_length_are_ignored()
    {
        var zones = Zones(SampleRoute, RouteLength, minLen: 500f);

        Assert.Empty(zones);
    }

    [Fact]
    public void A_zero_length_route_has_no_zones()
    {
        Assert.Empty(Zones(SampleRoute, 0f));
    }

    [Fact]
    public void A_missing_sampler_has_no_zones()
    {
        Assert.Empty(Zones(null, 100f));
    }

    [Theory]
    [InlineData(100f, true)]   // at the start of the straight
    [InlineData(279f, true)]   // just before the tail
    [InlineData(285f, false)]  // inside the last 20 units: too late to boost
    [InlineData(99f, false)]   // before the straight
    [InlineData(301f, false)]  // after the straight
    public void Only_the_body_of_a_straight_counts_as_in_zone(float progress, bool expectedInZone)
    {
        var singleStraight = new[] { new[] { 100f, 300f } };

        Assert.Equal(expectedInZone, InZone(singleStraight, progress));
    }

    [Fact]
    public void Tail_length_decides_how_early_a_straight_stops_counting()
    {
        var singleStraight = new[] { new[] { 100f, 300f } };

        Assert.True(InZone(singleStraight, 299f, tail: 0f));
        Assert.False(InZone(singleStraight, 251f, tail: 50f));
    }

    [Fact]
    public void No_zones_means_never_in_zone()
    {
        Assert.False(InZone(new float[0][], 50f));
    }
}
