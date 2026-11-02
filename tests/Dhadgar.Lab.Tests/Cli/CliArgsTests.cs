using Dhadgar.Lab.Tools.Cli;
using Xunit;

namespace Dhadgar.Lab.Tests.Cli;

/// <summary>
/// The shape rules for <c>cli_run</c> arguments (L37, R-ARGS): each item is its own argument, short, with no control
/// characters, and the list is bounded. Pure: no Docker.
/// </summary>
public sealed class CliArgsTests
{
    [Fact]
    public void Check_NullOrEmptyList_IsRefused()
    {
        Assert.True(CliAllowlist.Check(null).IsFailure);
        Assert.True(CliAllowlist.Check([]).IsFailure);
    }

    [Fact]
    public void Check_EmptyItem_IsRefused()
    {
        Assert.True(CliAllowlist.Check(["nodes", ""]).IsFailure);
    }

    [Fact]
    public void Check_ControlCharacter_IsRefused()
    {
        Assert.True(CliAllowlist.Check(["nodes", "list\n--all"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "list\t"]).IsFailure);
    }

    [Fact]
    public void Check_OversizedItem_IsRefused()
    {
        string big = new string('a', CliAllowlist.MaxArgLength + 1);

        Assert.True(CliAllowlist.Check(["nodes", "get", big]).IsFailure);
    }

    [Fact]
    public void Check_TooManyItems_IsRefused()
    {
        var args = new List<string> { "nodes", "list" };
        for (int i = 0; i < CliAllowlist.MaxArgCount; i++)
        {
            args.Add("--x");
        }

        Assert.True(CliAllowlist.Check(args).IsFailure);
    }

    [Fact]
    public void Check_ItemWithSpaces_IsOneArgument()
    {
        // A value with a space is one list item; the tool never joins items into a command string.
        Assert.True(CliAllowlist.Check(["nodes", "get", "node one"]).IsSuccess);
    }

    [Fact]
    public void Verbs_MatchTheC10Allowlist()
    {
        var verbs = CliAllowlist.Verbs.OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "auth", "config", "discord", "nodes", "notifications" }, verbs);
        Assert.Equal(new[] { "--help", "--version" }, CliAllowlist.Flags.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }
}
