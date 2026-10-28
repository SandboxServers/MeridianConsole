using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Agent hub at <see cref="AgentHubProtocol.Path"/>. The node is identified only by the connection's
/// client certificate (the <c>node_id</c> claim), never by the payload (D-S23, D-S24).
/// </summary>
/// <remarks>
/// The certificate is validated when the connection is accepted, but a WebSocket can outlive it. Each
/// heartbeat therefore re-checks usability (revocation with grace, against <c>AgentCertificates</c>) and expiry (the
/// <c>certificate_not_after</c> claim set by <see cref="MtlsMiddleware"/>).
/// </remarks>
public sealed class AgentHub(
    IHeartbeatService heartbeatService,
    NodesDbContext dbContext,
    IOptions<MtlsOptions> mtlsOptions,
    TimeProvider timeProvider,
    ILogger<AgentHub> logger,
    IAgentConnectionRegistry registry) : Hub<IAgentHubClient>, IAgentHub
{
    private const string CertificateThumbprintClaim = "certificate_thumbprint";
    private const string CertificateNotAfterClaim = "certificate_not_after";

    /// <summary>
    /// Refuses connections with an unsupported protocol version, without a node identity, with a
    /// certificate that is revoked or expired, or for a node that is unknown or decommissioned. Accepted
    /// connections join the node's group and are recorded in <see cref="IAgentConnectionRegistry"/>.
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

        if (!await IsCertificateCurrentAsync(Context.ConnectionAborted))
        {
            logger.LogWarning("Agent connection refused: certificate revoked or expired for node {NodeId}", nodeId);
            Context.Abort();
            return;
        }

        var node = await dbContext.Nodes
            .AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new { n.OrganizationId, n.Status })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (node is null || node.Status == NodeStatus.Decommissioned)
        {
            logger.LogWarning("Agent connection refused: node {NodeId} is unknown or decommissioned", nodeId);
            Context.Abort();
            return;
        }

        Context.Items["OrganizationId"] = node.OrganizationId;

        await Groups.AddToGroupAsync(Context.ConnectionId, AgentHubProtocol.NodeGroup(nodeId));
        await base.OnConnectedAsync();

        // Last: a connection is reported as connected only once it is accepted and grouped.
        registry.Add(nodeId, Context.ConnectionId);
    }

    /// <summary>
    /// Removes the connection from <see cref="IAgentConnectionRegistry"/> when it closes.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetNodeId(out var nodeId))
        {
            registry.Remove(nodeId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Records a heartbeat for the node named by the connection's certificate. Business errors are returned
    /// in <see cref="AgentHeartbeatAck.ErrorCode"/>, never as a <see cref="HubException"/>. A revoked or
    /// expired certificate, or a decommissioned or unknown node, also closes the connection.
    /// </summary>
    public async Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryGetNodeId(out var nodeId))
        {
            return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.HeartbeatFailed);
        }

        if (!await IsCertificateCurrentAsync(Context.ConnectionAborted))
        {
            logger.LogWarning("Agent heartbeat refused: certificate revoked or expired for node {NodeId}", nodeId);
            Context.Abort();
            return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.HeartbeatFailed);
        }

        if (!AgentHeartbeatValidator.IsValid(heartbeat))
        {
            return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.InvalidHeartbeat);
        }

        var result = await heartbeatService.ProcessHeartbeatAsync(
            nodeId, AgentHeartbeatMapper.ToHeartbeatRequest(heartbeat), Context.ConnectionAborted);

        if (result.Success)
        {
            return new AgentHeartbeatAck(true, now);
        }

        return new AgentHeartbeatAck(false, now, result.Error switch
        {
            "node_not_found" => AgentHubErrorCodes.NodeNotFound,
            "node_decommissioned" => AgentHubErrorCodes.NodeDecommissioned,
            _ => AgentHubErrorCodes.HeartbeatFailed
        });
    }

    /// <summary>
    /// Validates the telemetry against the C-2 limits and acknowledges it. Nothing is written to the database;
    /// only the node id and metric count are logged.
    /// </summary>
    public Task<AgentHubAck> Telemetry(AgentTelemetry telemetry)
    {
        if (!AgentTelemetryValidator.IsValid(telemetry))
        {
            return Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.InvalidTelemetry));
        }

        if (!TryGetNodeId(out var nodeId))
        {
            return Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.HeartbeatFailed));
        }

        logger.LogDebug("Telemetry from node {NodeId}: {MetricCount} metrics", nodeId, telemetry.Metrics.Count);
        return Task.FromResult(new AgentHubAck(true));
    }

    /// <summary>Stub until VS-33: answers <see cref="AgentHubErrorCodes.NotSupported"/>.</summary>
    public Task<AgentHubAck> CommandResult(AgentCommandResult result) =>
        Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.NotSupported));

    /// <summary>Stub until VS-33: answers <see cref="AgentHubErrorCodes.NotSupported"/>.</summary>
    public Task<AgentHubAck> ServerStatusChanged(AgentServerStatus status) =>
        Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.NotSupported));

    /// <summary>Stub until VS-33: answers <see cref="AgentHubErrorCodes.NotSupported"/>.</summary>
    public Task<AgentHubAck> ConsoleOutput(AgentConsoleBatch batch) =>
        Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.NotSupported));

    private bool TryGetNodeId(out Guid nodeId) =>
        Guid.TryParse(Context.User?.FindFirst(AgentHubProtocol.NodeIdClaim)?.Value, out nodeId);

    /// <summary>
    /// False when the connection's certificate is no longer usable (revoked and outside its renewal grace, D-S212),
    /// or has passed its NotAfter time and expired certificates are not allowed.
    /// </summary>
    private async Task<bool> IsCertificateCurrentAsync(CancellationToken ct)
    {
        if (!mtlsOptions.Value.AllowExpiredCertificates)
        {
            var notAfterValue = Context.User?.FindFirst(CertificateNotAfterClaim)?.Value;
            if (!long.TryParse(notAfterValue, out var notAfterUnix) ||
                timeProvider.GetUtcNow() > DateTimeOffset.FromUnixTimeSeconds(notAfterUnix))
            {
                return false;
            }
        }

        var thumbprint = Context.User?.FindFirst(CertificateThumbprintClaim)?.Value;
        if (string.IsNullOrEmpty(thumbprint))
        {
            return false;
        }

        var row = await dbContext.AgentCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Thumbprint == thumbprint, ct);

        // No row: same as mTLS validation (Trusted). A row: the one grace-aware predicate (D-S212).
        return row is null || AgentCertificateState.IsUsable(row, timeProvider.GetUtcNow().UtcDateTime);
    }
}
