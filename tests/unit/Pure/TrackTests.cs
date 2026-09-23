using System.Collections.Generic;
using UnityEngine;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

/// The track never moves, so the racing distance is something we measure off it rather than infer from the cars.
/// These cover the two halves of that: how much of the game's closed loop is real road, and exactly where the road
/// meets the finish line.
public class RoadFractionTests
{
    static List<Vector3> Straight(int count, float spacing = 20f)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < count; i++) points.Add(new Vector3(0, 0, i * spacing));
        return points;
    }

    [Fact]
    public void A_loop_with_even_spacing_is_all_road()
    {
        Assert.Equal(1f, RoadFraction(Straight(40)), 3);
    }

    [Fact]
    public void The_long_hop_home_at_the_tail_is_not_road()
    {
        // 30 waypoints 20 apart (580 of road), then a jump of 2000 back across the map
        var points = Straight(30);
        points.Add(points[points.Count - 1] + new Vector3(2000, 0, 0));
        float fraction = RoadFraction(points);
        Assert.Equal(580f / 2580f, fraction, 3);
    }

    [Fact]
    public void A_long_straight_early_in_the_lap_is_still_road()
    {
        // a wide-open back straight in the first half must never be mistaken for the hop home
        var points = Straight(30);
        points.Insert(5, points[4] + new Vector3(0, 0, 900));
        for (int i = 6; i < points.Count; i++) points[i] += new Vector3(0, 0, 900);
        Assert.Equal(1f, RoadFraction(points), 3);
    }

    [Fact]
    public void Too_few_waypoints_to_judge_means_all_road()
    {
        Assert.Equal(1f, RoadFraction(Straight(4)), 3);
        Assert.Equal(1f, RoadFraction(null), 3);
    }
}

public class FinishCrossingTests
{
    // Road running along +Z, sampled every 2 units, with the route distance alongside it.
    static (List<Vector3> positions, List<float> along) Road(float length, float step = 2f)
    {
        var positions = new List<Vector3>(); var along = new List<float>();
        for (float at = 0; at <= length; at += step) { positions.Add(new Vector3(0, 0, at)); along.Add(at); }
        return (positions, along);
    }

    [Fact]
    public void Finds_the_exact_distance_where_the_road_meets_the_line()
    {
        var (positions, along) = Road(400f);
        float crossing = FinishCrossing(positions, along, new Vector3(0, 0, 301f), Vector3.forward);
        Assert.Equal(301f, crossing, 2);   // between samples, so it interpolates
    }

    [Fact]
    public void A_lap_crosses_its_line_twice_and_the_race_ends_at_the_last_one()
    {
        // an oval: out along +Z, back along -Z offset sideways, returning past the start/finish
        var positions = new List<Vector3>(); var along = new List<float>();
        float at = 0;
        for (float z = 0; z <= 400; z += 2) { positions.Add(new Vector3(0, 0, z)); along.Add(at); at += 2; }
        for (float x = 0; x <= 30; x += 2) { positions.Add(new Vector3(x, 0, 400)); along.Add(at); at += 2; }
        for (float z = 400; z >= -20; z -= 2) { positions.Add(new Vector3(30, 0, z)); along.Add(at); at += 2; }
        for (float x = 30; x >= 0; x -= 2) { positions.Add(new Vector3(x, 0, -20)); along.Add(at); at += 2; }
        for (float z = -20; z <= 40; z += 2) { positions.Add(new Vector3(0, 0, z)); along.Add(at); at += 2; }

        float crossing = FinishCrossing(positions, along, new Vector3(0, 0, 10f), Vector3.forward);
        Assert.True(crossing > 900f, $"took the grid crossing instead of the finish ({crossing})");
        Assert.Equal(at - 2f - 30f, crossing, 0);   // the final pass, near the end of the lap
    }

    [Fact]
    public void Road_that_only_passes_nearby_does_not_count()
    {
        var (positions, along) = Road(400f);
        // the line sits 200 units off to the side: the road crosses its plane but never the line itself
        Assert.Equal(-1f, FinishCrossing(positions, along, new Vector3(200, 0, 300f), Vector3.forward), 2);
    }

    [Fact]
    public void Crossing_backwards_through_the_line_is_not_a_finish()
    {
        var (positions, along) = Road(400f);
        positions.Reverse();   // driving the other way: the line is behind, never approached from the front
        Assert.Equal(-1f, FinishCrossing(positions, along, new Vector3(0, 0, 300f), Vector3.forward), 2);
    }

    [Fact]
    public void Nonsense_input_gives_no_crossing()
    {
        var (positions, along) = Road(100f);
        Assert.Equal(-1f, FinishCrossing(null, along, Vector3.zero, Vector3.forward), 2);
        Assert.Equal(-1f, FinishCrossing(positions, along, Vector3.zero, Vector3.zero), 2);
        Assert.Equal(-1f, FinishCrossing(positions, new List<float> { 0f }, Vector3.zero, Vector3.forward), 2);
    }
}
