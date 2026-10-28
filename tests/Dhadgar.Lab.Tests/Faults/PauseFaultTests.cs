using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using static Dhadgar.Lab.Tests.Faults.KillFaultTests;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The <c>pause</c> fault kind with fake docker, registry and inspector (R-ARGS, R-BLAST, R-RESULT).
/// </summary>
public sealed class PauseFaultTests
{
    [Fact]
    public async Task PrepareAsync_WithoutContainer_IsRefused()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector());
        var target = new ResolvedTarget("c:gateway", "container", "gateway", null, "gateway");

        var result = await fault.PrepareAsync(target, Args(), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InjectAsync_RunsDockerPauseOnTheContainer()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector());
        var heal = Json(new ContainerHealData(ContainerId));

        var result = await fault.InjectAsync(Target(), Args(), heal, "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "pause", ContainerId }, docker.Calls.Single());
    }

    [Fact]
    public async Task InjectAsync_DockerRefusesPause_IsFailure()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "container is not running"));
        var fault = Fault(docker, Registry(), Inspector());
        var heal = Json(new ContainerHealData(ContainerId));

        var result = await fault.InjectAsync(Target(), Args(), heal, "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task HealAsync_RunsDockerUnpause()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "unpause", ContainerId }, docker.Calls.Single());
    }

    [Fact]
    public async Task HealAsync_NotPaused_IsSuccess()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "Error response from daemon: Container is not paused"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task HealAsync_UnpauseFailsForAnotherReason_IsFailure()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "Error response from daemon: permission denied"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcileAsync_PausedLabContainer_IsUnpaused()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("true") : Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value);
        Assert.Contains(docker.Calls, c => c.SequenceEqual(new[] { "unpause", ContainerId }));
    }

    [Fact]
    public async Task ReconcileAsync_RunningContainer_IsLeftAlone()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("false") : Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.DoesNotContain(docker.Calls, c => c[0] == "unpause");
    }

    [Fact]
    public async Task ReconcileAsync_NonLabContainer_IsNeverTouched()
    {
        var docker = new FakeDocker(_ => Ok("true"));
        var inspection = LabInspection() with { Labels = new Dictionary<string, string>() };
        var fault = Fault(docker, Registry(), Inspector(inspection));

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.Empty(docker.Calls);
    }

    private static PauseFault Fault(FakeDocker docker, FakeRegistry registry, FakeInspector inspector) =>
        new(docker, registry, new FaultTargetGuard(registry, inspector, new PassingTripwire()), NullLogger<PauseFault>.Instance);

    private static FaultJournalEntry Entry() =>
        new("f1", "pause", "c:gateway", Args(), Json(new ContainerHealData(ContainerId)),
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);
}
