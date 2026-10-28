using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using static Dhadgar.Lab.Tests.Faults.KillFaultTests;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The <c>disk_fill</c> fault kind with fake docker, registry and inspector (R-ARGS, R-BLAST, R-RESULT).
/// </summary>
public sealed class DiskFillFaultTests
{
    private const string Fill = "/tmp/.lab-fill";

    [Fact]
    public async Task PrepareAsync_RecordsAvailableSpaceAndLeave()
    {
        var docker = new FakeDocker(_ => Ok("Avail\n5000\n"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.PrepareAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var heal = result.Value;
        Assert.Equal(ContainerId, heal.GetProperty("container_id").GetString());
        Assert.Equal(5000, heal.GetProperty("available_kib").GetInt32());
        Assert.Equal(64, heal.GetProperty("leave_kib").GetInt32());
        Assert.Equal(new[] { "exec", ContainerId, "df", "-k", "--output=avail", "/tmp" }, docker.Calls.Single());
    }

    [Theory]
    [InlineData("/var")]
    [InlineData("/")]
    [InlineData(null)]
    public async Task PrepareAsync_UndeclaredMount_IsRefusedBeforeDocker(string? mount)
    {
        var docker = new FakeDocker(_ => Ok("Avail\n5000\n"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.PrepareAsync(Target(), Json(new DiskFillArgs(mount, 64)), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task PrepareAsync_NegativeLeave_IsRefusedBeforeDocker()
    {
        var docker = new FakeDocker(_ => Ok("Avail\n5000\n"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.PrepareAsync(Target(), Json(new DiskFillArgs("/tmp", -1)), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task PrepareAsync_NoSpaceAboveLeave_IsRefused()
    {
        var docker = new FakeDocker(_ => Ok("Avail\n50\n"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.PrepareAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_UnreadableDfOutput_IsRefused()
    {
        var docker = new FakeDocker(_ => Ok("Avail\nlots\n"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.PrepareAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InjectAsync_FallocatesAvailableMinusLeave()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.InjectAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), Json(Heal()), "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "exec", ContainerId, "fallocate", "-l", "4936K", Fill }, docker.Calls.Single());
    }

    [Fact]
    public async Task InjectAsync_FallocateFails_FallsBackToDd()
    {
        var docker = new FakeDocker(args => args[2] == "fallocate" ? new DockerOutput(1, "", "not supported") : Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.InjectAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), Json(Heal()), "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, docker.Calls.Count);
        Assert.Equal(new[] { "exec", ContainerId, "dd", "if=/dev/zero", "of=" + Fill, "bs=1024", "count=4936" }, docker.Calls[1]);
    }

    [Fact]
    public async Task InjectAsync_FallocateAndDdFail_IsFailure()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "no space"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.InjectAsync(Target(), Json(new DiskFillArgs("/tmp", 64)), Json(Heal()), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealAsync_RemovesTheFillFile()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "exec", ContainerId, "rm", "-f", Fill }, docker.Calls.Single());
    }

    [Fact]
    public async Task HealAsync_ContainerNotRunning_IsSuccess()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "container is not running"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task HealAsync_RemoveFails_IsFailure()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "permission denied"));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ReconcileAsync_RunningLabContainer_FillIsRemoved()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("running") : Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value);
        Assert.Contains(docker.Calls, c => c.SequenceEqual(new[] { "exec", ContainerId, "rm", "-f", Fill }));
    }

    [Fact]
    public async Task ReconcileAsync_StoppedContainer_IsLeftAlone()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("exited") : Ok(""));
        var fault = Fault(docker, Registry(), Inspector());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.DoesNotContain(docker.Calls, c => c[0] == "exec");
    }

    [Fact]
    public async Task ReconcileAsync_NonLabContainer_IsNeverTouched()
    {
        var docker = new FakeDocker(_ => Ok("running"));
        var inspection = LabInspection() with { Labels = new Dictionary<string, string>() };
        var fault = Fault(docker, Registry(), Inspector(inspection));

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.Empty(docker.Calls);
    }

    private static DiskFillFault Fault(FakeDocker docker, FakeRegistry registry, FakeInspector inspector) =>
        new(docker, registry, new FaultTargetGuard(registry, inspector, new PassingTripwire()), NullLogger<DiskFillFault>.Instance);

    private static DiskFillHealData Heal() => new(ContainerId, 5000, 64);

    private static FaultJournalEntry Entry() =>
        new("f1", "disk_fill", "c:gateway", Json(new DiskFillArgs("/tmp", 64)), Json(Heal()),
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);
}
