using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Pool;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// L37 <c>pool_acquire</c> CLI install failure: the members the failed acquire took are released. No Docker: the fake
/// Docker client fails the <c>cp</c> step.
/// </summary>
public sealed class PoolAcquireCliInstallTests
{
    private const string BuildId = "0123abcd";
    private static readonly PathValidator Validator = new(NullLogger<PathValidator>.Instance);

    [Fact]
    public async Task InstallFailure_ReleasesTheAcquiredMembers()
    {
        var provider = new FakePoolProvider("linux", FakePoolProvider.LinuxContainer);
        using var harness = new PoolHarness([provider]);
        WriteLatestBuildWithCli(harness.Paths);
        harness.Docker.ExitCodeFor = call => call[0] == "cp" ? 1 : 0;
        var tool = new PoolAcquireTool(harness.Manager, harness.Docker, harness.Paths, Validator);
        var constraints = new PoolConstraints("linux", null, null, null, null, null, null);

        Result<JsonElement> result = await tool.InvokeAsync(
            JsonSerializer.SerializeToElement(new PoolAcquireArgs(constraints, "test"), LabJson.Options),
            new LabCallContext("c1", "lease-1", "test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.Docker] installing the CLI into the member failed", result.Error);
        string member = Assert.Single(provider.Provisioned);
        Assert.Contains(member, provider.Destroyed);
        PoolListing listing = await harness.Manager.ListAsync(CancellationToken.None);
        Assert.DoesNotContain(listing.Members, m => string.Equals(m.MemberId, member, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MemberReset_ReinstallsTheCli()
    {
        var provider = new FakePoolProvider("linux", FakePoolProvider.LinuxContainer);
        using var harness = new PoolHarness([provider]);
        WriteLatestBuildWithCli(harness.Paths);
        var constraints = new PoolConstraints("linux", null, null, null, null, null, null);
        Result<IReadOnlyList<PoolMember>> acquired = await harness.Manager.AcquireAsync(constraints, "test", "lease-1", CancellationToken.None);
        Assert.True(acquired.IsSuccess);
        string memberId = Assert.Single(acquired.Value!).MemberId;
        harness.Docker.Calls.Clear();
        var guard = new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire());
        var tool = new MemberResetTool(harness.Manager, guard, harness.Docker, harness.Paths, Validator);

        Result<JsonElement> result = await tool.InvokeAsync(
            JsonSerializer.SerializeToElement(new MemberResetArgs(memberId), LabJson.Options),
            new LabCallContext("c2", "lease-1", "test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(memberId, Assert.Single(provider.Reset));
        Assert.Contains(harness.Docker.Calls, call => call[0] == "cp" && call[^1].EndsWith(":/opt/lab/cli/", StringComparison.Ordinal));
        Assert.Contains(harness.Docker.Calls, call => call[0] == "cp" && call[^1].EndsWith(":/home/lab/.dhadgar/config.json", StringComparison.Ordinal));
    }

    /// <summary>Writes a latest-build stamp whose id folder has a <c>cli</c> folder (as <c>LastBuildReaderTests</c> does).</summary>
    private static void WriteLatestBuildWithCli(LabPaths paths)
    {
        string dir = Path.Combine(paths.Builds, BuildId);
        _ = Directory.CreateDirectory(Path.Combine(dir, "cli"));
        var json = new JsonObject
        {
            ["build_id"] = BuildId,
            ["commit"] = "0123abcd4567ef89",
            ["dirty"] = true,
            ["checkout"] = Path.Combine(paths.Builds, "..", "repo", "src", "lab"),
        };
        File.WriteAllText(Path.Combine(dir, "build.json"), json.ToJsonString());
        File.WriteAllText(Path.Combine(paths.Builds, "latest.txt"), BuildId);
    }
}
