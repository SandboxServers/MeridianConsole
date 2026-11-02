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
        Assert.True(CliAllowlist.Check(["auth", "login", "--identity-url", "http://meridian-lab-gateway:8080"]).IsSuccess);
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
}
