using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Cli;
using Xunit;

namespace Dhadgar.Lab.Tests.Cli;

/// <summary>
/// The C10 CLI verb allowlist and the member rules of <c>cli_run</c> (L37). Pure: no Docker.
/// </summary>
public sealed class CliAllowlistTests
{
    private static PoolMember LinuxMember(string? container = "meridian-lab-member-lin-01") =>
        new("lin-01", "m:lin-01", container,
            new PoolDimensions("linux", null, "debian", "none", "container", "x64", "host", false),
            MemberState.Ready, DateTimeOffset.UnixEpoch);

    private static PoolMember WindowsMember() =>
        new("win-01", "m:win-01", "meridian-lab-member-win-01",
            new PoolDimensions("windows", 26100, "servercore", "process", "container", "x64", "host", false),
            MemberState.Ready, DateTimeOffset.UnixEpoch);

    [Fact]
    public void CliRun_Host_IsRefused()
    {
        var host = CliRunTool.CheckMember("host", null);
        Assert.True(host.IsFailure);
        Assert.Contains("never runs on the host", host.Error);

        var targetHost = CliRunTool.CheckMember("m:host", null);
        Assert.True(targetHost.IsFailure);
        Assert.Contains("never runs on the host", targetHost.Error);
    }

    [Fact]
    public void CliRun_WindowsMember_IsRefused()
    {
        var result = CliRunTool.CheckMember("win-01", WindowsMember());

        Assert.True(result.IsFailure);
        Assert.Contains("Linux container member", result.Error);
    }

    [Fact]
    public void CliRun_LinuxMemberWithoutContainer_IsRefused()
    {
        var result = CliRunTool.CheckMember("lin-01", LinuxMember(container: null));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void CliRun_UnknownMember_IsNotFound()
    {
        var result = CliRunTool.CheckMember("lin-09", null);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.NotFound]", result.Error);
    }

    [Fact]
    public void CliRun_LinuxContainerMember_IsAccepted()
    {
        Assert.True(CliRunTool.CheckMember("m:lin-01", LinuxMember()).IsSuccess);
        Assert.True(CliRunTool.CheckMember("lin-01", LinuxMember()).IsSuccess);
    }

    [Fact]
    public void Allowlist_UnknownVerb_IsRefused()
    {
        Assert.True(CliAllowlist.Check(["rm", "-rf", "/"]).IsFailure);
        Assert.True(CliAllowlist.Check(["enrollment", "tokens", "create"]).IsFailure);
        Assert.True(CliAllowlist.Check(["secret", "get", "x"]).IsFailure);
    }

    [Fact]
    public void Allowlist_SubcommandThatPrintsSecret_IsRefused()
    {
        Assert.True(CliAllowlist.Check(["nodes", "tokens", "list"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "create-token"]).IsFailure);
        Assert.True(CliAllowlist.Check(["auth", "token"]).IsFailure);
    }

    [Fact]
    public void Allowlist_SafeSubcommands_AreAccepted()
    {
        Assert.True(CliAllowlist.Check(["auth", "login", "--client-id", "dev-client", "--client-secret", "dev-secret"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["nodes", "list", "--org", "o1"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["auth", "status"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["nodes", "list"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["notifications", "logs"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["discord", "status"]).IsSuccess);
    }

    [Fact]
    public void Allowlist_Flags_OnlyAloneAreAccepted()
    {
        Assert.True(CliAllowlist.Check(["--version"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["--help"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["--help", "nodes"]).IsFailure);
    }

    [Theory]
    [InlineData("--identity-url", "http://x")]
    [InlineData("--identity-url=http://x", null)]
    [InlineData("--identity-url:http://x", null)]
    public void Login_IdentityUrl_IsRefused(string token, string? value)
    {
        var args = new List<string> { "auth", "login", "--client-id", "dev-client", "--client-secret", "dev-secret", token };
        if (value is not null)
        {
            args.Add(value);
        }

        Assert.True(CliAllowlist.Check(args).IsFailure);
    }

    public static TheoryData<string[]> NotThePinnedLogin => new()
    {
        new[] { "auth", "login", "--client-id", "dev-client", "--client-secret", "real" },
        new[] { "auth", "login", "--client-id", "dev-client", "--client-secret=dev-secret" },
        new[] { "auth", "login", "--client-id", "dev-client" },
        new[] { "auth", "login", "--client-secret", "dev-secret" },
        new[] { "auth", "login", "--client-id", "dev-client", "--client-id", "dev-client", "--client-secret", "dev-secret", "--client-secret", "dev-secret" },
    };

    [Theory]
    [MemberData(nameof(NotThePinnedLogin))]
    public void Login_OnlyThePinnedDevClient_IsAccepted(string[] args)
    {
        Assert.True(CliAllowlist.Check(args).IsFailure);
    }

    [Fact]
    public void AtArguments_AreRefused()
    {
        Assert.True(CliAllowlist.Check(["nodes", "list", "@/etc/passwd"]).IsFailure);
        Assert.True(CliAllowlist.Check(["notifications", "test", "--title", "@x"]).IsFailure);
    }

    [Fact]
    public void UnknownOptions_AreRefused()
    {
        Assert.True(CliAllowlist.Check(["nodes", "list", "--reveal"]).IsFailure);
        Assert.True(CliAllowlist.Check(["discord", "channels", "--url", "x"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "list", "--"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "list", "-o", "x"]).IsFailure);
    }

    [Fact]
    public void Maintenance_NeedsStartOrStop()
    {
        Assert.True(CliAllowlist.Check(["nodes", "maintenance", "n1"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "maintenance", "start", "n1"]).IsSuccess);
    }

    [Fact]
    public void Positionals_AreBounded()
    {
        Assert.True(CliAllowlist.Check(["nodes", "get", "a", "b"]).IsFailure);
        Assert.True(CliAllowlist.Check(["nodes", "get", "a"]).IsSuccess);
        Assert.True(CliAllowlist.Check(["nodes", "list", "x"]).IsFailure);
    }

    [Fact]
    public void Refusals_NeverEchoTheValue()
    {
        var refused = CliAllowlist.Check(["auth", "login", "--client-id", "dev-client", "--client-secret", "hunter2xyz"]);
        Assert.True(refused.IsFailure);
        Assert.DoesNotContain("hunter2xyz", refused.Error);
    }
}
