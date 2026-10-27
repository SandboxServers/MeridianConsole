using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Hubs;

namespace Dhadgar.Nodes.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="AgentHeartbeatValidator"/>: each limit is checked on its own so that removing any
/// branch of the validator fails at least one test.
/// </summary>
public sealed class AgentHeartbeatValidatorTests
{
    [Fact]
    public void IsValid_Null_ReturnsFalse()
    {
        Assert.False(AgentHeartbeatValidator.IsValid(null));
    }

    [Theory]
    [MemberData(nameof(InvalidHeartbeats))]
    public void IsValid_OutOfLimits_ReturnsFalse(AgentHeartbeat heartbeat)
    {
        Assert.False(AgentHeartbeatValidator.IsValid(heartbeat));
    }

    [Fact]
    public void IsValid_BoundaryValues_ReturnsTrue()
    {
        var atLimits = new AgentHeartbeat(
            0,
            100,
            100,
            AgentHeartbeatLimits.MaxActiveGameServers,
            new string('a', AgentHeartbeatLimits.MaxAgentVersionLength),
            Enumerable.Repeat(new string('x', AgentHeartbeatLimits.MaxHealthIssueLength), AgentHeartbeatLimits.MaxHealthIssues).ToList());

        Assert.True(AgentHeartbeatValidator.IsValid(atLimits));
    }

    [Theory]
    [InlineData("2.0.0-hub")]
    [InlineData(null)]
    public void IsValid_PlainVersion_ReturnsTrue(string? version)
    {
        var heartbeat = new AgentHeartbeat(10, 10, 10, 0, version, ["ok"]);

        Assert.True(AgentHeartbeatValidator.IsValid(heartbeat));
    }

    [Theory]
    [InlineData("1.0\0")]
    [InlineData("1.0\n")]
    [InlineData("1.0\r\nWARN forged")]
    public void IsValid_ControlCharacterInVersion_ReturnsFalse(string version)
    {
        var heartbeat = new AgentHeartbeat(10, 10, 10, 0, version, null);

        Assert.False(AgentHeartbeatValidator.IsValid(heartbeat));
    }

    [Theory]
    [InlineData("ok\r\n2026-10-27 WARN Node recovered")]
    [InlineData("ok\0")]
    public void IsValid_ControlCharacterInHealthIssue_ReturnsFalse(string issue)
    {
        var heartbeat = new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", [issue]);

        Assert.False(AgentHeartbeatValidator.IsValid(heartbeat));
    }

    public static IEnumerable<object[]> InvalidHeartbeats()
    {
        yield return [new AgentHeartbeat(double.NaN, 10, 10, 0, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(10, -1, 10, 0, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(10, 10, 100.01, 0, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(double.PositiveInfinity, 10, 10, 0, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(10, 10, 10, -1, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(10, 10, 10, AgentHeartbeatLimits.MaxActiveGameServers + 1, "2.0.0-hub", null)];
        yield return [new AgentHeartbeat(10, 10, 10, 0, new string('a', AgentHeartbeatLimits.MaxAgentVersionLength + 1), null)];
        yield return [new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", Enumerable.Repeat("x", AgentHeartbeatLimits.MaxHealthIssues + 1).ToList())];
        yield return [new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", new string?[] { null }!)];
        yield return [new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", [new string('a', AgentHeartbeatLimits.MaxHealthIssueLength + 1)])];
    }
}
