using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Viewer perks: extra boosts on join (Pure.ExtraBoosts), tier rules (Pure.TierAllows) and why a follower boost can be
// missing (Pure.FollowerCheckStatus). Settings -> Perks: boostFollower / boostSubscriber / boostDeveloper / boostHost.
public class PerksTests
{
    [Fact]
    public void A_follower_gets_the_follower_boost()
    {
        var (count, reasons) = ExtraBoosts(1, 1, 1, 0, isFollower: true, isSub: false, isDev: false, isHost: false);
        Assert.Equal(1, count);
        Assert.Equal(new[] { "follower" }, reasons);
    }

    [Fact]
    public void Perks_stack_a_subscribed_follower_gets_both()
    {
        var (count, reasons) = ExtraBoosts(1, 2, 1, 1, isFollower: true, isSub: true, isDev: false, isHost: false);
        Assert.Equal(3, count);
        Assert.Equal(new[] { "follower", "sub" }, reasons);
    }

    [Fact]
    public void Everything_on_stacks_all_four()
    {
        var (count, reasons) = ExtraBoosts(1, 1, 1, 1, isFollower: true, isSub: true, isDev: true, isHost: true);
        Assert.Equal(4, count);
        Assert.Equal(new[] { "follower", "sub", "dev", "host" }, reasons);
    }

    [Fact]
    public void An_unknown_follower_status_counts_as_not_following()
    {
        // This is what happens without a Twitch token: isFollower is never true, so a plain follower gets nothing.
        var (count, reasons) = ExtraBoosts(1, 1, 1, 0, isFollower: false, isSub: false, isDev: false, isHost: false);
        Assert.Equal(0, count);
        Assert.Empty(reasons);
    }

    [Fact]
    public void A_perk_set_to_zero_is_off_even_when_the_viewer_qualifies()
    {
        var (count, reasons) = ExtraBoosts(0, 0, 0, 0, isFollower: true, isSub: true, isDev: true, isHost: true);
        Assert.Equal(0, count);
        Assert.Empty(reasons);
    }

    [Fact]
    public void Negative_perks_take_boosts_away()
    {
        var (count, reasons) = ExtraBoosts(0, 0, 0, -2, isFollower: false, isSub: false, isDev: false, isHost: true);
        Assert.Equal(-2, count);
        Assert.Equal(new[] { "host" }, reasons);
    }

    [Theory]
    [InlineData("everyone", false, false, false, false, true)]
    [InlineData("off", true, true, true, true, false)]
    [InlineData("follower", true, false, false, false, true)]
    [InlineData("follower", false, false, false, false, false)]
    [InlineData("follower", false, true, false, false, true)]  // subs count as followers
    [InlineData("follower", false, false, true, false, true)]  // devs too
    [InlineData("follower", false, false, false, true, true)]  // and the host
    [InlineData("subscriber", true, false, false, false, false)] // a follower is not a sub
    [InlineData("subscriber", false, true, false, false, true)]
    [InlineData("sub", false, false, true, false, true)]
    [InlineData(" Subscriber ", false, false, false, true, true)] // case and padding do not matter
    [InlineData(null, false, false, false, false, true)]          // unset = everyone
    [InlineData("nonsense", false, false, false, false, true)]
    public void Tier_rules(string tier, bool follower, bool sub, bool dev, bool host, bool expected)
    {
        Assert.Equal(expected, TierAllows(tier, follower, sub, dev, host));
    }

    [Theory]
    [InlineData(false, false, true, null, "no token")]
    [InlineData(true, false, true, null, "no token")]   // a token without its client id cannot call Helix
    [InlineData(false, true, true, null, "no token")]
    [InlineData(true, true, false, null, "unknown")]    // streamer not logged in: no broadcaster id to ask about
    [InlineData(true, true, true, "401: token rejected", "unknown")]
    [InlineData(true, true, true, null, "ok")]
    [InlineData(true, true, true, "", "ok")]
    public void Follower_check_status_says_why_the_lookup_cannot_run(bool token, bool clientId, bool streamer, string error, string expected)
    {
        Assert.Equal(expected, FollowerCheckStatus(token, clientId, streamer, error));
    }

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("oauth:abc123", "abc123")]
    [InlineData("OAuth:abc123", "abc123")]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("  oauth:abc123  ", "abc123")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Pasted_tokens_lose_their_oauth_or_bearer_prefix(string pasted, string expected)
    {
        Assert.Equal(expected, CleanToken(pasted));
    }
}
