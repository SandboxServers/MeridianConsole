using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.SignalR;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Agent hub at <see cref="AgentHubProtocol.Path"/>. The node is identified only by the connection's
/// client certificate (the <c>node_id</c> claim), never by the payload (D-S23, D-S24).
/// </summary>
public sealed class AgentHub(
    IHeartbeatService heartbeatService,
    TimeProvider timeProvider,
    ILogger<AgentHub> logger) : Hub<IAgentHubClient>, IAgentHub
{
    /// <summary>
    /// Refuses connections with an unsupported protocol version or without a node identity, and places
    /// accepted connections in the node's group.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var version = Context.GetHttpContext()?.Request.Headers[AgentHubProtocol.VersionHeader].ToString();
        if (!string.IsNullOrEmpty(version) && version != AgentHubProtocol.CurrentVersion)
        {
            logger.LogWarning("Agent connection refused: unsupported protocol {Version}", version);
            Context.Abort();
            return;
        }

        if (!TryGetNodeId(out var nodeId))
        {
            logger.LogWarning("Agent connection refused: no node_id claim");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, AgentHubProtocol.NodeGroup(nodeId));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Records a heartbeat for the node named by the connection's certificate. Business errors are returned
    /// in <see cref="AgentHeartbeatAck.ErrorCode"/>, never as a <see cref="HubException"/>.
    /// </summary>
    public async Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryGetNodeId(out var nodeId))
        {
            return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.HeartbeatFailed);
        }

        if (!AgentHeartbeatValidator.IsValid(heartbeat))
        {
            return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.InvalidHeartbeat);
        }

        var result = await heartbeatService.ProcessHeartbeatAsync(
            nodeId, AgentHeartbeatMapper.ToHeartbeatRequest(heartbeat), Context.ConnectionAborted);

        return result.Success
            ? new AgentHeartbeatAck(true, now)
            : new AgentHeartbeatAck(false, now, result.Error switch
            {
                "node_not_found" => AgentHubErrorCodes.NodeNotFound,
                "node_decommissioned" => AgentHubErrorCodes.NodeDecommissioned,
                _ => AgentHubErrorCodes.HeartbeatFailed
            });
    }

    private bool TryGetNodeId(out Guid nodeId) =>
        Guid.TryParse(Context.User?.FindFirst(AgentHubProtocol.NodeIdClaim)?.Value, out nodeId);
}
