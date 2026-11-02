using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tests.Pool;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Cli;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Cli;

/// <summary>
/// L37 <c>cli_run</c> tests over the pool and Docker fakes. No Docker: <see cref="FakeDockerCli"/> records every call, and
/// the admin key is a fake value.
/// </summary>
public sealed class CliRunToolTests
{
    private const string AdminKey = "adm-key-0123456789";
    private static readonly PoolConstraints AnyLinux = new("linux", null, null, null, null, null, null);
    private static readonly PathValidator Validator = new(NullLogger<PathValidator>.Instance);

    [Fact]
    public async Task Version_RunsInTheResolvedContainer()
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        string memberId = await AcquireMemberAsync(harness);
        using var vault = new SecretVault();
        CliRunTool tool = NewTool(harness, new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire()), new LabCredentialRefs(), vault);

        Result<JsonElement> result = await InvokeAsync(tool, "m:" + memberId, "--version");

        Assert.True(result.IsSuccess, result.Error);
        string[] exec = Assert.Single(harness.Docker.Calls);
        Assert.Equal(["exec", "-e", "HOME=/home/lab", "live-" + memberId, "/opt/lab/cli/dhadgar", "--version"], exec);
    }

    [Theory]
    [InlineData("unlabelled")]
    [InlineData("no-member-label")]
    [InlineData("other-context")]
    public async Task UnlabelledMember_IsBlastRadius_AndNeverExecs(string mode)
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        string memberId = await AcquireMemberAsync(harness);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1", ["meridian-lab-member"] = memberId };
        string context = "lab-ctx";
        switch (mode)
        {
            case "unlabelled":
                labels.Remove("meridian-lab");
                break;
            case "no-member-label":
                labels.Remove("meridian-lab-member");
                break;
            default:
                context = "other-ctx";
                break;
        }

        using var vault = new SecretVault();
        var guard = new FaultTargetGuard(harness.Registry, new FixedInspector("/meridian-lab-m-" + memberId, labels, context), new PassingTripwire());
        CliRunTool tool = NewTool(harness, guard, new LabCredentialRefs(), vault);

        Result<JsonElement> result = await InvokeAsync(tool, "m:" + memberId, "--version");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Docker.Calls, call => call[0] == "exec");
    }

    [Fact]
    public async Task HostMember_IsRefused_BeforeAnyDocker()
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        using var vault = new SecretVault();
        CliRunTool tool = NewTool(harness, new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire()), new LabCredentialRefs(), vault);
        int before = harness.Docker.Calls.Count;

        Result<JsonElement> result = await InvokeAsync(tool, "m:host", "--version");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Refused]", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, harness.Docker.Calls.Count);
    }

    [Fact]
    public async Task AdminVerb_SeedsTheKeyForTheCall_ThenScrubsIt()
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        string memberId = await AcquireMemberAsync(harness);
        using var vault = new SecretVault();
        string reference = vault.Add(SecretKind.Password, AdminKey);
        var refs = new LabCredentialRefs();
        refs.Set(LabCredentialRefs.NotificationsAdmin, reference);
        string staging = CliMemberInstall.StagingDirectory(harness.Paths);
        var staged = new List<string>();
        harness.Docker.OnCall = call =>
        {
            if (call[0] == "cp" && File.Exists(call[1]))
            {
                staged.Add(File.ReadAllText(call[1]));
            }
        };
        CliRunTool tool = NewTool(harness, new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire()), refs, vault);

        Result<JsonElement> result = await InvokeAsync(tool, "m:" + memberId, "notifications", "logs");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, staged.Count);
        Assert.Contains(AdminKey, staged[0], StringComparison.Ordinal);
        Assert.DoesNotContain(AdminKey, staged[1], StringComparison.Ordinal);
        int firstCopy = harness.Docker.Calls.FindIndex(call => call[0] == "cp");
        int run = harness.Docker.Calls.FindIndex(call => call[0] == "exec" && call.Contains("/opt/lab/cli/dhadgar", StringComparer.Ordinal));
        int secondCopy = harness.Docker.Calls.FindLastIndex(call => call[0] == "cp");
        Assert.True(firstCopy < run && run < secondCopy);
        Assert.DoesNotContain(harness.Docker.Calls.SelectMany(call => call), item => item.Contains(AdminKey, StringComparison.Ordinal));
        Assert.Empty(Directory.Exists(staging) ? Directory.GetFiles(staging, "cli-config-*.json") : []);
    }

    [Fact]
    public async Task AdminVerb_WithoutStack_IsNotFound_AndNeverExecs()
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        string memberId = await AcquireMemberAsync(harness);
        using var vault = new SecretVault();
        CliRunTool tool = NewTool(harness, new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire()), new LabCredentialRefs(), vault);

        Result<JsonElement> result = await InvokeAsync(tool, "m:" + memberId, "notifications", "logs");

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.NotFound] the stack has no admin keys; run stack_up first", result.Error);
        Assert.DoesNotContain(harness.Docker.Calls, call => call[0] == "exec");
    }

    [Fact]
    public async Task AdminVerb_ScrubFailure_FailsTheCall()
    {
        using var harness = new PoolHarness([new FakePoolProvider("linux", FakePoolProvider.LinuxContainer)]);
        string memberId = await AcquireMemberAsync(harness);
        using var vault = new SecretVault();
        var refs = new LabCredentialRefs();
        refs.Set(LabCredentialRefs.NotificationsAdmin, vault.Add(SecretKind.Password, AdminKey));
        int copies = 0;
        harness.Docker.ExitCodeFor = call => call[0] == "cp" && ++copies == 2 ? 1 : 0;
        CliRunTool tool = NewTool(harness, new FaultTargetGuard(harness.Registry, new LabMemberInspector(), new PassingTripwire()), refs, vault);

        Result<JsonElement> result = await InvokeAsync(tool, "m:" + memberId, "notifications", "logs");

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.Docker] could not remove the admin key from the member's CLI config", result.Error);
    }

    private static CliRunTool NewTool(PoolHarness harness, FaultTargetGuard guard, LabCredentialRefs refs, SecretVault vault) =>
        new(harness.Manager, guard, harness.Docker, new Redactor(vault), refs, vault, harness.Paths, Validator);

    private static async Task<string> AcquireMemberAsync(PoolHarness harness)
    {
        Result<IReadOnlyList<PoolMember>> acquired = await harness.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);
        Assert.True(acquired.IsSuccess, acquired.Error);
        return acquired.Value![0].MemberId;
    }

    private static Task<Result<JsonElement>> InvokeAsync(CliRunTool tool, string member, params string[] args) =>
        tool.InvokeAsync(
            JsonSerializer.SerializeToElement(new { member, args }),
            new LabCallContext("c1", "lease-1", "test"),
            CancellationToken.None);
}
