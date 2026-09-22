using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

// Chat commands: pulling the argument out of "!race color red" (Pure.CommandArg).
public class ChatTests
{
    [Theory]
    [InlineData("!race color red", "!race color", "red")]
    [InlineData("  !RACE COLOR #ff8800 ", "!race color", "#ff8800")]   // case and padding do not matter
    [InlineData("!race color   sky blue  ", "!race color", "sky blue")] // inner spaces survive, outer ones are trimmed
    [InlineData("!race color", "!race color", "")]                    // bare command: empty argument, not null
    [InlineData("!race respawn", "!race respawn", "")]
    public void The_text_after_the_command_is_the_argument(string message, string command, string expectedArgument)
    {
        Assert.Equal(expectedArgument, CommandArg(message, command));
    }

    [Theory]
    [InlineData("!race colorful", "!race color")] // a longer word is a different command
    [InlineData("!race", "!race color")]
    [InlineData("hello", "!race color")]
    [InlineData("say !race color red", "!race color")] // must be at the start
    [InlineData("!race color red", "")]
    [InlineData("!race color red", null)]
    [InlineData("", "!race color")]
    [InlineData(null, "!race color")]
    public void Anything_that_is_not_the_command_gives_null(string message, string command)
    {
        Assert.Null(CommandArg(message, command));
    }

    // settings.respawnCommand / colorCommand hold aliases: "!race respawn|!respawn" (or comma-separated).
    [Theory]
    [InlineData("!race respawn|!respawn", new[] { "!race respawn", "!respawn" })]
    [InlineData("!race color, !color", new[] { "!race color", "!color" })]
    [InlineData(" !color ", new[] { "!color" })]
    [InlineData("!a||!b,,", new[] { "!a", "!b" })] // empty parts are dropped
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void An_alias_list_splits_on_pipes_and_commas(string list, string[] expected)
    {
        Assert.Equal(expected, CommandAliases(list));
    }

    [Theory]
    [InlineData("!respawn", "")]                 // the short alias works on its own
    [InlineData("!race respawn", "")]
    [InlineData("!RESPAWN  ", "")]
    [InlineData("!race color red", "red")]
    [InlineData("!color #ff8800", "#ff8800")]
    public void Any_alias_in_the_list_matches(string message, string expectedArgument)
    {
        var aliases = CommandAliases("!race respawn|!respawn|!race color|!color");
        Assert.Equal(expectedArgument, CommandArg(message, aliases));
    }

    [Theory]
    [InlineData("!respawnnow")]  // longer word: not the command
    [InlineData("!colorful red")]
    [InlineData("respawn")]      // no prefix at all
    [InlineData("!race")]
    public void A_message_that_matches_no_alias_gives_null(string message)
    {
        Assert.Null(CommandArg(message, CommandAliases("!race respawn|!respawn|!race color|!color")));
        Assert.Null(CommandArg(message, (System.Collections.Generic.IEnumerable<string>)null));
        Assert.Null(CommandArg(message, new string[0]));
    }

    [Fact]
    public void The_single_command_overload_is_unchanged_by_the_alias_one()
    {
        Assert.Equal("red", CommandArg("!race color red", "!race color"));
        Assert.Null(CommandArg("!color red", "!race color")); // a bare "!color" only works once it is in the alias list
    }

    [Fact]
    public void Chat_replies_name_the_viewer_and_the_result()
    {
        Assert.Equal("@ShibikoX color set to #ff8800", ColorSetReply("ShibikoX", "#ff8800"));
        Assert.Equal("@ShibikoX color is for follower+", ColorDeniedReply("ShibikoX", "follower"));
    }
}
