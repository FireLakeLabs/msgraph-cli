using FluentAssertions;
using MsGraphCli.Core.Exceptions;
using MsGraphCli.Middleware;
using Xunit;

namespace MsGraphCli.Tests.Unit;

public class CommandGuardTests
{
    [Theory]
    [InlineData("mail send")]
    [InlineData("mail reply")]
    [InlineData("mail forward")]
    [InlineData("mail move")]
    [InlineData("mail mark-read")]
    [InlineData("mail mark-unread")]
    [InlineData("calendar create")]
    [InlineData("calendar update")]
    [InlineData("calendar delete")]
    [InlineData("calendar respond")]
    [InlineData("drive upload")]
    [InlineData("drive mkdir")]
    [InlineData("drive move")]
    [InlineData("drive rename")]
    [InlineData("drive delete")]
    [InlineData("todo lists create")]
    [InlineData("todo add")]
    [InlineData("todo update")]
    [InlineData("todo done")]
    [InlineData("todo undo")]
    [InlineData("todo delete")]
    public void EnforceReadOnly_WriteCommand_WhenReadOnly_Throws(string commandPath)
    {
        Action act = () => CommandGuard.EnforceReadOnly(commandPath, readOnlyFlag: true);

        act.Should().Throw<ReadOnlyViolationException>()
            .Which.ExitCode.Should().Be(10);
    }

    [Theory]
    [InlineData("mail list")]
    [InlineData("mail search")]
    [InlineData("mail get")]
    [InlineData("mail folders list")]
    [InlineData("calendar events")]
    [InlineData("calendar get")]
    [InlineData("auth status")]
    [InlineData("drive ls")]
    [InlineData("drive search")]
    [InlineData("drive get")]
    [InlineData("drive download")]
    [InlineData("todo lists")]
    [InlineData("todo list")]
    [InlineData("todo get")]
    public void EnforceReadOnly_ReadCommand_WhenReadOnly_DoesNotThrow(string commandPath)
    {
        Action act = () => CommandGuard.EnforceReadOnly(commandPath, readOnlyFlag: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnforceReadOnly_WriteCommand_WhenNotReadOnly_DoesNotThrow()
    {
        Action act = () => CommandGuard.EnforceReadOnly("mail send", readOnlyFlag: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnforceAllowList_CommandInList_DoesNotThrow()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mail list", "mail get" };

        Action act = () => CommandGuard.EnforceAllowList("mail list", allowed);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnforceAllowList_CommandNotInList_Throws()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mail list", "mail get" };

        Action act = () => CommandGuard.EnforceAllowList("mail send", allowed);

        act.Should().Throw<CommandNotAllowedException>()
            .Which.CommandName.Should().Be("mail send");
    }

    [Fact]
    public void EnforceAllowList_NullAllowList_DoesNotThrow()
    {
        Action act = () => CommandGuard.EnforceAllowList("mail send", allowedCommands: null);

        act.Should().NotThrow();
    }
}
