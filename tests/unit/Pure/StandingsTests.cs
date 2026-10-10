using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Local standings: period starts, race points, and the totals (Pure.PeriodStart / RacePoints / Standings).
public class StandingsTests
{
    static readonly DateTime Wednesday = new(2026, 10, 7, 21, 30, 0);

    static RaceRecord Race(DateTime at, params (string login, int place, bool finished)[] racers) => new()
    {
        At = at,
        Racers = racers.Select(racer => new RaceEntry { Login = racer.login, Name = racer.login, Place = racer.place, Finished = racer.finished }).ToList(),
    };

    [Theory]
    [InlineData("day", "2026-10-07")]
    [InlineData("week", "2026-10-05")]
    [InlineData("month", "2026-10-01")]
    public void Periods_start_at_local_midnight_of_the_day_the_monday_and_the_first(string period, string start)
    {
        Assert.Equal(DateTime.Parse(start), PeriodStart(period, Wednesday));
    }

    [Fact]
    public void A_sunday_belongs_to_the_week_that_started_the_monday_before()
    {
        Assert.Equal(new DateTime(2026, 10, 5), PeriodStart("week", new DateTime(2026, 10, 11, 23, 0, 0)));
    }

    [Fact]
    public void All_time_has_no_start()
    {
        Assert.Equal(DateTime.MinValue, PeriodStart("all", Wednesday));
    }

    [Fact]
    public void A_winner_scores_the_field_size_and_a_dnf_scores_nothing()
    {
        Assert.Equal(8, RacePoints(1, true, 8));
        Assert.Equal(1, RacePoints(8, true, 8));
        Assert.Equal(0, RacePoints(0, false, 8));
    }

    [Fact]
    public void Races_before_the_period_are_left_out()
    {
        var races = new List<RaceRecord>
        {
            Race(Wednesday.AddDays(-40), ("old", 1, true), ("other", 2, true)),
            Race(Wednesday.AddHours(-1), ("new", 1, true), ("other", 2, true)),
        };
        Assert.DoesNotContain(Standings(races, "month", Wednesday), standing => standing.Login == "old");
        Assert.Contains(Standings(races, "all", Wednesday), standing => standing.Login == "old");
    }

    [Fact]
    public void Totals_add_up_and_rank_by_points_then_wins()
    {
        var races = new List<RaceRecord>
        {
            Race(Wednesday.AddHours(-3), ("ann", 1, true), ("bob", 2, true), ("cat", 0, false)),
            Race(Wednesday.AddHours(-2), ("bob", 1, true), ("ann", 2, true), ("cat", 3, true)),
        };
        var standings = Standings(races, "day", Wednesday);
        Assert.Equal(new[] { "ann", "bob", "cat" }, standings.Select(standing => standing.Login));   // ann and bob tie on 5 points and 1 win: fewer races, then name
        var cat = standings.Single(standing => standing.Login == "cat");
        Assert.Equal(2, cat.Races);
        Assert.Equal(1, cat.Finishes);
        Assert.Equal(1, cat.Points);
        Assert.Equal(1, cat.Podiums);
        Assert.Equal(3, cat.Best);
    }

    [Fact]
    public void Login_casing_does_not_split_a_racer()
    {
        var races = new List<RaceRecord> { Race(Wednesday, ("Ann", 1, true)), Race(Wednesday, ("ann", 1, true)) };
        Assert.Single(Standings(races, "all", Wednesday));
    }
}
