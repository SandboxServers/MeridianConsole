using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Servers.Services;

public sealed class ServerLifecycleService : IServerLifecycleService
{
    private readonly ServersDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ServerLifecycleService> _logger;

    // Valid state transitions
    private static readonly Dictionary<ServerStatus, ServerStatus[]> ValidTransitions = new()
    {
        [ServerStatus.Created] = [ServerStatus.Provisioning, ServerStatus.Deleted],
        [ServerStatus.Provisioning] = [ServerStatus.Installing, ServerStatus.Error, ServerStatus.Deleted],
        [ServerStatus.Installing] = [ServerStatus.Ready, ServerStatus.Error, ServerStatus.Deleted],
        [ServerStatus.Ready] = [ServerStatus.Starting, ServerStatus.Maintenance, ServerStatus.Suspended, ServerStatus.Deleted],
        [ServerStatus.Starting] = [ServerStatus.Running, ServerStatus.Crashed, ServerStatus.Error, ServerStatus.Stopped],
        [ServerStatus.Running] = [ServerStatus.Stopping, ServerStatus.Crashed, ServerStatus.Maintenance, ServerStatus.Suspended],
        [ServerStatus.Stopping] = [ServerStatus.Stopped, ServerStatus.Crashed, ServerStatus.Error],
        [ServerStatus.Stopped] = [ServerStatus.Starting, ServerStatus.Ready, ServerStatus.Maintenance, ServerStatus.Suspended, ServerStatus.Deleted],
        [ServerStatus.Crashed] = [ServerStatus.Starting, ServerStatus.Stopped, ServerStatus.Error, ServerStatus.Maintenance],
        [ServerStatus.Error] = [ServerStatus.Ready, ServerStatus.Maintenance, ServerStatus.Deleted],
        [ServerStatus.Suspended] = [ServerStatus.Ready, ServerStatus.Stopped, ServerStatus.Deleted],
        [ServerStatus.Maintenance] = [ServerStatus.Ready, ServerStatus.Stopped, ServerStatus.Deleted]
    };

    public ServerLifecycleService(
        ServersDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ServerLifecycleService> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<ServiceResult<bool>> StartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        if (server.Status != ServerStatus.Ready && server.Status != ServerStatus.Stopped && server.Status != ServerStatus.Crashed)
        {
            return ServiceResult.Fail<bool>($"cannot_start_from_status_{server.Status}");
        }

        if (!server.NodeId.HasValue)
        {
            return ServiceResult.Fail<bool>("server_not_placed");
        }

        var oldStatus = server.Status.ToString();
        server.Status = ServerStatus.Starting;
        server.PowerState = ServerPowerState.Starting;
        server.LastStartedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ServerStatusChanged(
            server.Id,
            organizationId,
            server.Name,
            oldStatus,
            server.Status.ToString(),
            "User requested start",
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Starting server {ServerId}", serverId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> StopServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        if (server.Status != ServerStatus.Running && server.Status != ServerStatus.Starting)
        {
            return ServiceResult.Fail<bool>($"cannot_stop_from_status_{server.Status}");
        }

        var oldStatus = server.Status.ToString();
        server.Status = ServerStatus.Stopping;
        server.PowerState = ServerPowerState.Stopping;

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ServerStatusChanged(
            server.Id,
            organizationId,
            server.Name,
            oldStatus,
            server.Status.ToString(),
            "User requested stop",
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Stopping server {ServerId}", serverId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> RestartServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        if (server.Status != ServerStatus.Running)
        {
            return ServiceResult.Fail<bool>($"cannot_restart_from_status_{server.Status}");
        }

        // Publish restart event - agent handles the actual restart
        await _publishEndpoint.Publish(new ServerRestarted(
            server.Id,
            organizationId,
            server.Name,
            "User requested restart",
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Restarting server {ServerId}", serverId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> KillServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        if (server.PowerState == ServerPowerState.Off)
        {
            return ServiceResult.Fail<bool>("server_already_off");
        }

        var oldStatus = server.Status.ToString();
        server.Status = ServerStatus.Stopped;
        server.PowerState = ServerPowerState.Off;
        server.LastStoppedAt = DateTime.UtcNow;

        // Calculate uptime
        if (server.LastStartedAt.HasValue)
        {
            var uptime = (DateTime.UtcNow - server.LastStartedAt.Value).TotalSeconds;
            server.TotalUptimeSeconds += (long)uptime;
        }

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ServerStopped(
            server.Id,
            organizationId,
            server.Name,
            "Force killed by user",
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Force killed server {ServerId}", serverId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> TransitionStatusAsync(
        Guid serverId,
        ServerStatus newStatus,
        string? reason = null,
        CancellationToken ct = default)
    {
        var server = await _db.Servers.FindAsync([serverId], ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        if (!IsValidTransition(server.Status, newStatus))
        {
            return ServiceResult.Fail<bool>($"invalid_transition_{server.Status}_to_{newStatus}");
        }

        var oldStatus = server.Status.ToString();
        server.Status = newStatus;

        // Update power state based on status
        server.PowerState = newStatus switch
        {
            ServerStatus.Starting => ServerPowerState.Starting,
            ServerStatus.Running => ServerPowerState.On,
            ServerStatus.Stopping => ServerPowerState.Stopping,
            ServerStatus.Crashed => ServerPowerState.Crashed,
            _ => ServerPowerState.Off
        };

        // Update timestamps
        if (newStatus == ServerStatus.Running && !server.LastStartedAt.HasValue)
        {
            server.LastStartedAt = DateTime.UtcNow;
        }
        else if (newStatus == ServerStatus.Stopped || newStatus == ServerStatus.Crashed)
        {
            server.LastStoppedAt = DateTime.UtcNow;
            if (server.LastStartedAt.HasValue)
            {
                var uptime = (DateTime.UtcNow - server.LastStartedAt.Value).TotalSeconds;
                server.TotalUptimeSeconds += (long)uptime;
            }
        }

        if (newStatus == ServerStatus.Crashed)
        {
            server.CrashCount++;
        }

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ServerStatusChanged(
            server.Id,
            server.OrganizationId,
            server.Name,
            oldStatus,
            newStatus.ToString(),
            reason,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Server {ServerId} transitioned from {OldStatus} to {NewStatus}: {Reason}",
            serverId, oldStatus, newStatus, reason);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> TransitionPowerStateAsync(
        Guid serverId,
        ServerPowerState newPowerState,
        CancellationToken ct = default)
    {
        var server = await _db.Servers.FindAsync([serverId], ct);

        if (server is null)
        {
            return ServiceResult.Fail<bool>("server_not_found");
        }

        var oldPowerState = server.PowerState.ToString();
        server.PowerState = newPowerState;

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ServerPowerStateChanged(
            server.Id,
            server.OrganizationId,
            oldPowerState,
            newPowerState.ToString(),
            DateTimeOffset.UtcNow), ct);

        return ServiceResult.Ok(true);
    }

    private static bool IsValidTransition(ServerStatus current, ServerStatus target)
    {
        if (!ValidTransitions.TryGetValue(current, out var validTargets))
        {
            return false;
        }

        return validTargets.Contains(target);
    }
}
