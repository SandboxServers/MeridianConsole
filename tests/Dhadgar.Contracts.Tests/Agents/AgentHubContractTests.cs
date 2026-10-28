using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Contracts.Tests.Agents;

/// <summary>
/// Pins the agent hub surface after VS-03R. The hub interfaces live in <c>Contracts.Nodes</c> (D-26), payloads the
/// agent sends carry no node id (D-27), and <c>ReceiveCommand</c> is typed (D-29). Each test fails on the
/// pre-VS-03R head, where the hub had a parallel copy in <c>Contracts.Agents</c>.
/// </summary>
public class AgentHubContractTests
{
    [Fact]
    public void IAgentHub_HasExactlyTheFiveMethods()
    {
        var actual = typeof(IAgentHub)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(m => (m.Name, Parameter: m.GetParameters().Single().ParameterType, Returns: m.ReturnType))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var expected = new (string Name, Type Parameter, Type Returns)[]
        {
            (nameof(IAgentHub.CommandResult), typeof(AgentCommandResult), typeof(Task<AgentHubAck>)),
            (nameof(IAgentHub.ConsoleOutput), typeof(AgentConsoleBatch), typeof(Task<AgentHubAck>)),
            (nameof(IAgentHub.Heartbeat), typeof(AgentHeartbeat), typeof(Task<AgentHeartbeatAck>)),
            (nameof(IAgentHub.ServerStatusChanged), typeof(AgentServerStatus), typeof(Task<AgentHubAck>)),
            (nameof(IAgentHub.Telemetry), typeof(AgentTelemetry), typeof(Task<AgentHubAck>)),
        }.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    [Fact]
    public void IAgentHubClient_ReceiveCommand_IsTyped()
    {
        var method = typeof(IAgentHubClient).GetMethod(nameof(IAgentHubClient.ReceiveCommand));

        Assert.NotNull(method);
        var parameter = Assert.Single(method!.GetParameters());
        Assert.Equal(typeof(AgentCommandEnvelope), parameter.ParameterType);
    }

    [Theory]
    [InlineData(typeof(AgentCommandResult))]
    [InlineData(typeof(AgentServerStatus))]
    [InlineData(typeof(AgentConsoleBatch))]
    [InlineData(typeof(AgentTelemetry))]
    public void AgentSentPayloads_CarryNoNodeId(Type payload)
    {
        Assert.Null(payload.GetProperty("NodeId", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void AgentCommandEnvelope_KeepsNodeId()
    {
        // The control plane addresses commands to a node, so the envelope is the one payload that names it.
        Assert.NotNull(typeof(AgentCommandEnvelope).GetProperty("NodeId", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void AgentHubTypes_HaveOneHome()
    {
        var agentNamespaceTypes = typeof(AgentMessageLimits).Assembly.GetTypes()
            .Where(t => t.Namespace == "Dhadgar.Contracts.Agents" && t.IsPublic)
            .Select(t => t.Name)
            .ToList();

        Assert.DoesNotContain("IAgentHub", agentNamespaceTypes);
        Assert.DoesNotContain("IAgentHubClient", agentNamespaceTypes);
        Assert.DoesNotContain("AgentHeartbeat", agentNamespaceTypes);

        // The hub surface is in Contracts.Nodes.
        Assert.Equal("Dhadgar.Contracts.Nodes", typeof(IAgentHub).Namespace);
        Assert.Equal("Dhadgar.Contracts.Nodes", typeof(IAgentHubClient).Namespace);
    }
}
