using UnityEngine;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// !drift: spotting a corner on the route ahead, holding the slide angle, when the slide ends and how big the
// mini-turbo is (Pure.FlatTurn / TurnAhead / DriftYawRate / DriftOver / DriftTurbo).
public class DriftTests
{
    // 100 units straight along +z, a right-hand 90 degree corner over the next 40 units, then straight along +x.
    const float StraightEnd = 100f;
    const float CornerEnd = 140f;

    static (Vector3 pos, Vector3 dir) RightCorner(float routeDistance)
    {
        if (routeDistance < StraightEnd)
        {
            return (new Vector3(0, 0, routeDistance), Vector3.forward);
        }
        float cornerFraction = Mathf.Clamp01((routeDistance - StraightEnd) / (CornerEnd - StraightEnd));
        float headingAngle = cornerFraction * Mathf.PI / 2;
        return (Vector3.zero, new Vector3(Mathf.Sin(headingAngle), 0, Mathf.Cos(headingAngle)));
    }

    static (Vector3 pos, Vector3 dir) MirroredCorner(float routeDistance)
    {
        var sample = RightCorner(routeDistance);
        return (sample.pos, new Vector3(-sample.dir.x, 0, sample.dir.z));
    }

    // A full U-turn: heading swings 200 degrees over 50 units.
    static (Vector3 pos, Vector3 dir) Hairpin(float routeDistance)
    {
        float headingAngle = Mathf.Clamp01(routeDistance / 50f) * 200f * Mathf.Deg2Rad;
        return (Vector3.zero, new Vector3(Mathf.Sin(headingAngle), 0, Mathf.Cos(headingAngle)));
    }

    [Fact]
    public void A_right_bend_is_positive_and_a_left_bend_negative()
    {
        Assert.Equal(90f, FlatTurn(Vector3.forward, Vector3.right), 2);
        Assert.Equal(-90f, FlatTurn(Vector3.forward, Vector3.left), 2);
    }

    [Fact]
    public void Height_does_not_count_as_turning()
    {
        Assert.Equal(0f, FlatTurn(Vector3.forward, new Vector3(0, 0.5f, 1)), 2);
    }

    [Fact]
    public void A_straight_ahead_has_no_turn()
    {
        Assert.Equal(0f, TurnAhead(RightCorner, 10f, 30f), 2);
    }

    [Fact]
    public void Entering_the_corner_reads_as_a_big_right_turn()
    {
        Assert.InRange(TurnAhead(RightCorner, 100f, 30f), 60f, 70f);
    }

    [Fact]
    public void A_left_corner_reads_negative()
    {
        Assert.InRange(TurnAhead(MirroredCorner, 100f, 30f), -70f, -60f);
    }

    [Fact]
    public void Just_before_the_corner_the_look_ahead_already_sees_part_of_it()
    {
        float turn = TurnAhead(RightCorner, 85f, 30f);

        Assert.InRange(turn, 30f, 40f);
    }

    [Fact]
    public void Past_the_corner_the_turn_ahead_drops_to_nothing()
    {
        Assert.Equal(0f, TurnAhead(RightCorner, 145f, 20f), 2);
    }

    [Fact]
    public void A_hairpin_past_180_degrees_is_not_wrapped_around()
    {
        Assert.InRange(TurnAhead(Hairpin, 0f, 52f), 195f, 205f);
    }

    [Fact]
    public void A_missing_route_has_no_turn()
    {
        Assert.Equal(0f, TurnAhead(null, 0f, 30f));
    }

    [Fact]
    public void Below_the_target_slip_the_nose_is_pushed_in()
    {
        Assert.Equal(1.6f, DriftYawRate(5f, 0.2f, 22f, 35f, 1.6f));
    }

    [Fact]
    public void A_faster_natural_rotation_below_the_target_is_kept()
    {
        Assert.Equal(2f, DriftYawRate(5f, 2f, 22f, 35f, 1.6f));
    }

    [Fact]
    public void Between_target_and_max_the_rotation_is_capped_at_the_push_rate()
    {
        Assert.Equal(1.6f, DriftYawRate(28f, 3f, 22f, 35f, 1.6f));
        Assert.Equal(0.5f, DriftYawRate(28f, 0.5f, 22f, 35f, 1.6f));
    }

    [Fact]
    public void Past_the_max_slip_the_car_may_not_rotate_further_in()
    {
        Assert.Equal(0f, DriftYawRate(40f, 1.2f, 22f, 35f, 1.6f));
        Assert.Equal(-0.4f, DriftYawRate(40f, -0.4f, 22f, 35f, 1.6f));
    }

    [Theory]
    [InlineData(0.2f, 0f, 0f, false)]    // too early to look at the exit, whatever the road does
    [InlineData(1f, 40f, 20f, false)]    // mid-corner, still sliding
    [InlineData(1f, 5f, 20f, true)]      // the road ahead has straightened
    [InlineData(1f, -30f, 20f, true)]    // the road bends the other way (S-bend)
    [InlineData(1f, 40f, 3f, true)]      // the car has straightened back up
    [InlineData(2.5f, 40f, 20f, true)]   // the hard cap
    public void A_slide_ends_on_exit_realignment_or_the_time_cap(float elapsed, float turnAhead, float slip,
        bool expectedOver)
    {
        Assert.Equal(expectedOver, DriftOver(elapsed, turnAhead, slip, 0.5f, 2.5f, 10f, 6f));
    }

    [Fact]
    public void A_very_short_drift_earns_no_turbo()
    {
        Assert.Equal(0f, DriftTurbo(0.3f, 0.5f, 2f, 3f, 9f));
    }

    [Fact]
    public void A_longer_drift_earns_a_bigger_turbo_up_to_the_cap()
    {
        float shortDrift = DriftTurbo(0.5f, 0.5f, 2f, 3f, 9f);
        float middleDrift = DriftTurbo(1.25f, 0.5f, 2f, 3f, 9f);
        float longDrift = DriftTurbo(5f, 0.5f, 2f, 3f, 9f);

        Assert.Equal(3f, shortDrift, 3);
        Assert.Equal(6f, middleDrift, 3);
        Assert.Equal(9f, longDrift, 3);
    }

    [Fact]
    public void The_no_boosts_reply_names_the_viewer()
    {
        Assert.Equal("@Shibiko no boosts left for a drift", DriftNoBoostsReply("Shibiko"));
    }
}
