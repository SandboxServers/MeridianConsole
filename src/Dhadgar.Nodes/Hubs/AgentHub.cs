using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Data;
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
    ILogger<AgentHub> logger) : Hub<IAgentHubClient>, IAgentHub
{
    private const string CertificateThumbprintClaim = "certificate_thumbprint";
    private const string CertificateNotAfterClaim = "certificate_not_after";

    /// <summary>
    /// Refuses connections with an unsupported protocol version, without a node identity, or with a
    /// certificate that is revoked or expired, and places accepted connections in the node's group.
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

        await Groups.AddToGroupAsync(Context.ConnectionId, AgentHubProtocol.NodeGroup(nodeId));
        await base.OnConnectedAsync();
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
