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
}
