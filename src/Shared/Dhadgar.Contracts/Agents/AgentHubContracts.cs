namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Hub methods implemented by Nodes' AgentHub and called by the agent.
/// </summary>
public interface IAgentHub
{
    /// <summary>Periodic node and server health report.</summary>
    Task Heartbeat(AgentHeartbeat heartbeat);

    /// <summary>Outcome of a previously received command.</summary>
    Task CommandResult(AgentCommandResult result);

    /// <summary>Periodic metrics report.</summary>
    Task Telemetry(AgentTelemetry telemetry);

    /// <summary>A server process changed state.</summary>
    Task ServerStatusChanged(AgentServerStatus status);

    /// <summary>A batch of server console lines.</summary>
    Task ConsoleOutput(AgentConsoleBatch batch);
}

/// <summary>
/// Hub methods implemented by the agent and called by Nodes through
/// IHubContext&lt;AgentHub, IAgentHubClient&gt;.
/// </summary>
public interface IAgentHubClient
{
    /// <summary>Delivers a signed command to the agent.</summary>
    Task ReceiveCommand(AgentCommandEnvelope command);

    /// <summary>
    /// Liveness check carrying an opaque nonce echoed back in the ping result. The nonce is at most
    /// <see cref="AgentMessageLimits.MaxNonceLength"/> characters.
    /// </summary>
    Task ReceivePing(string nonce);
}
