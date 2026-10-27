using Dhadgar.Agent.Core.Health;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Health;

public class HeartbeatPayloadMapperTests
{
    [Fact]
    public void ToContract_NullMetrics_GivesZeroPercentages()
    {
        var payload = CreatePayload(metrics: null);

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(0, heartbeat.CpuUsagePercent);
        Assert.Equal(0, heartbeat.MemoryUsagePercent);
        Assert.Equal(0, heartbeat.DiskUsagePercent);
    }

    [Fact]
    public void ToContract_NaNAndOutOfRangeValues_ClampToZeroAndHundred()
    {
        var payload = CreatePayload(new SystemMetrics
        {
            CpuUsagePercent = double.NaN,
            TotalMemoryBytes = 100,
            AvailableMemoryBytes = -50
        });

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(0, heartbeat.CpuUsagePercent);
        Assert.Equal(100, heartbeat.MemoryUsagePercent);
    }

    [Fact]
    public void ToContract_CpuAboveHundred_ClampsToHundred()
    {
        var payload = CreatePayload(new SystemMetrics { CpuUsagePercent = 150 });

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(100, heartbeat.CpuUsagePercent);
    }

    [Fact]
    public void ToContract_MultipleDisks_UsesMaximumDiskUsage()
    {
        var payload = CreatePayload(new SystemMetrics
        {
            Disks =
            [
                new DiskMetrics { Name = "C:", TotalBytes = 100, AvailableBytes = 60 },
                new DiskMetrics { Name = "D:", TotalBytes = 100, AvailableBytes = 10 },
                new DiskMetrics { Name = "E:", TotalBytes = 100, AvailableBytes = 85 }
            ]
        });

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(90, heartbeat.DiskUsagePercent);
    }

    [Fact]
    public void ToContract_OnlyRunningProcesses_AreCounted()
    {
        var payload = CreatePayload(metrics: null);
        payload.ActiveProcesses.Add(CreateProcess(ProcessState.Running));
        payload.ActiveProcesses.Add(CreateProcess(ProcessState.Running));
        payload.ActiveProcesses.Add(CreateProcess(ProcessState.Starting));
        payload.ActiveProcesses.Add(CreateProcess(ProcessState.Stopped));
        payload.ActiveProcesses.Add(CreateProcess(ProcessState.Failed));

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(2, heartbeat.ActiveGameServers);
    }

    [Fact]
    public void ToContract_LongVersion_IsTruncated()
    {
        var longVersion = new string('v', AgentHeartbeatLimits.MaxAgentVersionLength + 25);
        var payload = CreatePayload(metrics: null, agentVersion: longVersion);

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(AgentHeartbeatLimits.MaxAgentVersionLength, heartbeat.AgentVersion!.Length);
    }

    [Fact]
    public void ToContract_ManyLongWarnings_AreTruncatedAndCapped()
    {
        var payload = CreatePayload(metrics: null);
        for (var i = 0; i < AgentHeartbeatLimits.MaxHealthIssues + 5; i++)
        {
            payload.Warnings.Add(new string('w', AgentHeartbeatLimits.MaxHealthIssueLength + 10));
        }

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.NotNull(heartbeat.HealthIssues);
        Assert.Equal(AgentHeartbeatLimits.MaxHealthIssues, heartbeat.HealthIssues!.Count);
        Assert.All(heartbeat.HealthIssues, issue =>
            Assert.Equal(AgentHeartbeatLimits.MaxHealthIssueLength, issue.Length));
    }

    [Fact]
    public void ToContract_NullWarnings_AreDropped()
    {
        var payload = CreatePayload(metrics: null);
        payload.Warnings.Add("first");
        payload.Warnings.Add(null!);
        payload.Warnings.Add("second");

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(new[] { "first", "second" }, heartbeat.HealthIssues);
    }

    [Fact]
    public void ToContract_ControlCharactersInWarning_AreReplaced()
    {
        var payload = CreatePayload(metrics: null);
        payload.Warnings.Add("line1\r\nline2\0");

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.NotNull(heartbeat.HealthIssues);
        Assert.Equal("line1  line2 ", heartbeat.HealthIssues!.Single());
        Assert.DoesNotContain(heartbeat.HealthIssues!.Single(), char.IsControl);
    }

    [Fact]
    public void ToContract_ControlCharactersInVersion_AreReplaced()
    {
        var payload = CreatePayload(metrics: null, agentVersion: "1.0\n2");

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal("1.0 2", heartbeat.AgentVersion);
    }

    [Fact]
    public void ToContract_SurrogatePairAtCut_IsNotSplit()
    {
        var warning = new string('x', AgentHeartbeatLimits.MaxHealthIssueLength - 1) + "😀";
        var payload = CreatePayload(metrics: null);
        payload.Warnings.Add(warning);

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        var issue = heartbeat.HealthIssues!.Single();
        Assert.Equal(AgentHeartbeatLimits.MaxHealthIssueLength - 1, issue.Length);
        Assert.False(char.IsHighSurrogate(issue[^1]));
    }

    [Fact]
    public void ToContract_ManyRunningProcesses_AreCappedAtMax()
    {
        var payload = CreatePayload(metrics: null);
        for (var i = 0; i < AgentHeartbeatLimits.MaxActiveGameServers + 3; i++)
        {
            payload.ActiveProcesses.Add(CreateProcess(ProcessState.Running));
        }

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(AgentHeartbeatLimits.MaxActiveGameServers, heartbeat.ActiveGameServers);
    }

    [Fact]
    public void ToContract_DiskAboveHundred_ClampsToHundred()
    {
        var payload = CreatePayload(new SystemMetrics
        {
            Disks = [new DiskMetrics { Name = "C:", TotalBytes = 100, AvailableBytes = -50 }]
        });

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.Equal(100, heartbeat.DiskUsagePercent);
    }

    private static HeartbeatPayload CreatePayload(
        SystemMetrics? metrics = null,
        string agentVersion = "1.2.3")
    {
        return new HeartbeatPayload
        {
            NodeId = Guid.NewGuid(),
            AgentVersion = agentVersion,
            Metrics = metrics
        };
    }

    private static ProcessStatus CreateProcess(ProcessState state)
    {
        return new ProcessStatus
        {
            ProcessId = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            State = state
        };
    }
}
