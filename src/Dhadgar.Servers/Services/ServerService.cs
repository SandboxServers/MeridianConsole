using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Servers.Services;

public sealed class ServerService : IServerService
{
    private readonly ServersDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ServerService> _logger;

    public ServerService(
        ServersDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<ServerService> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<FilteredPagedResponse<ServerListItem>> GetServersAsync(
        Guid organizationId,
        ServerListQuery query,
        CancellationToken ct = default)
    {
        var queryable = _db.Servers
            .Where(s => s.OrganizationId == organizationId);

        // Apply filters
        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse<ServerStatus>(query.Status, true, out var status))
        {
            queryable = queryable.Where(s => s.Status == status);
        }

        if (!string.IsNullOrEmpty(query.PowerState) && Enum.TryParse<ServerPowerState>(query.PowerState, true, out var powerState))
        {
            queryable = queryable.Where(s => s.PowerState == powerState);
        }

        if (!string.IsNullOrEmpty(query.GameType))
        {
            queryable = queryable.Where(s => s.GameType == query.GameType);
        }

        if (query.NodeId.HasValue)
        {
            queryable = queryable.Where(s => s.NodeId == query.NodeId.Value);
        }

        if (!string.IsNullOrEmpty(query.Search))
        {
            var searchPattern = $"%{query.Search}%";
            queryable = queryable.Where(s =>
                EF.Functions.ILike(s.Name, searchPattern) ||
                (s.DisplayName != null && EF.Functions.ILike(s.DisplayName, searchPattern)));
        }

        if (!string.IsNullOrEmpty(query.Tags))
        {
            var tags = query.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tag in tags)
            {
                queryable = queryable.Where(s => s.Tags.Contains(tag));
            }
        }

        var totalCount = await queryable.CountAsync(ct);

        // Apply sorting (handle null sortBy/sortOrder safely)
        var sortBy = query.SortBy?.ToLowerInvariant() ?? "name";
        var sortOrder = query.SortOrder?.ToLowerInvariant() ?? "asc";
        queryable = sortBy switch
        {
            "name" => sortOrder == "desc"
                ? queryable.OrderByDescending(s => s.Name)
                : queryable.OrderBy(s => s.Name),
            "status" => sortOrder == "desc"
                ? queryable.OrderByDescending(s => s.Status)
                : queryable.OrderBy(s => s.Status),
            "gametype" => sortOrder == "desc"
                ? queryable.OrderByDescending(s => s.GameType)
                : queryable.OrderBy(s => s.GameType),
            "createdat" => sortOrder == "desc"
                ? queryable.OrderByDescending(s => s.CreatedAt)
                : queryable.OrderBy(s => s.CreatedAt),
            _ => queryable.OrderBy(s => s.Name)
        };

        // Apply pagination
        var servers = await queryable
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new ServerListItem(
                s.Id,
                s.Name,
                s.DisplayName,
                s.GameType,
                s.Status.ToString(),
                s.PowerState.ToString(),
                s.NodeId,
                s.CpuLimitMillicores,
                s.MemoryLimitMb,
                s.DiskLimitMb,
                s.LastStartedAt,
                s.LastStoppedAt,
                s.CrashCount,
                s.Tags,
                s.CreatedAt,
                s.UpdatedAt))
            .ToListAsync(ct);

        return FilteredPagedResponse<ServerListItem>.Create(
            servers,
            totalCount,
            query.Page,
            query.PageSize);
    }

    public async Task<ServiceResult<ServerDetail>> GetServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .Include(s => s.Configuration)
            .Include(s => s.Ports)
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<ServerDetail>("server_not_found");
        }

        return ServiceResult.Ok(MapToDetail(server));
    }

    public async Task<ServiceResult<ServerDetail>> CreateServerAsync(
        Guid organizationId,
        CreateServerRequest request,
        CancellationToken ct = default)
    {
        // Check for duplicate name
        var exists = await _db.Servers.AnyAsync(
            s => s.OrganizationId == organizationId && s.Name == request.Name, ct);

        if (exists)
        {
            return ServiceResult.Fail<ServerDetail>("server_name_exists");
        }

        var server = new Server
        {
            OrganizationId = organizationId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            GameType = request.GameType,
            CpuLimitMillicores = request.CpuLimitMillicores,
            MemoryLimitMb = request.MemoryLimitMb,
            DiskLimitMb = request.DiskLimitMb,
            TemplateId = request.TemplateId,
            Tags = request.Tags?.ToList() ?? [],
            Status = ServerStatus.Created,
            PowerState = ServerPowerState.Off
        };

        // Create configuration
        server.Configuration = new Data.Entities.ServerConfiguration
        {
            ServerId = server.Id,
            StartupCommand = request.StartupCommand,
            AutoStart = request.AutoStart,
            AutoRestartOnCrash = request.AutoRestartOnCrash
        };

        // Create ports with validation
        if (request.Ports != null)
        {
            foreach (var port in request.Ports)
            {
                var serverPort = new ServerPort
                {
                    ServerId = server.Id,
                    Name = port.Name,
                    Protocol = port.Protocol,
                    InternalPort = port.InternalPort,
                    ExternalPort = port.ExternalPort ?? port.InternalPort,
                    IsPrimary = port.IsPrimary
                };

                if (!serverPort.HasValidPorts())
                {
                    return ServiceResult.Fail<ServerDetail>(
                        $"invalid_port_range: port '{port.Name}' has invalid port numbers (must be {ServerPort.MinPort}-{ServerPort.MaxPort})");
                }

                server.Ports.Add(serverPort);
            }
        }

        _db.Servers.Add(server);
        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ServerCreated(
            server.Id,
            organizationId,
            server.Name,
            server.GameType,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Created server {ServerId} '{ServerName}' for org {OrgId}",
            server.Id, server.Name, organizationId);

        return ServiceResult.Ok(MapToDetail(server));
    }

    public async Task<ServiceResult<ServerDetail>> UpdateServerAsync(
        Guid organizationId,
        Guid serverId,
        UpdateServerRequest request,
        CancellationToken ct = default)
    {
        var server = await _db.Servers
            .Include(s => s.Configuration)
            .Include(s => s.Ports)
            .FirstOrDefaultAsync(s => s.Id == serverId && s.OrganizationId == organizationId, ct);

        if (server is null)
        {
            return ServiceResult.Fail<ServerDetail>("server_not_found");
        }

        if (request.Name != null && request.Name != server.Name)
        {
            // Check for duplicate name
            var exists = await _db.Servers.AnyAsync(
                s => s.OrganizationId == organizationId && s.Name == request.Name && s.Id != serverId, ct);

            if (exists)
            {
                return ServiceResult.Fail<ServerDetail>("server_name_exists");
            }

            server.Name = request.Name;
        }

        if (request.DisplayName != null)
        {
            server.DisplayName = request.DisplayName;
        }

        if (request.Tags != null)
        {
            server.Tags = request.Tags.ToList();
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated server {ServerId} for org {OrgId}", serverId, organizationId);

        return ServiceResult.Ok(MapToDetail(server));
    }

    public async Task<ServiceResult<bool>> DeleteServerAsync(
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

        // Soft delete - Status tracks state machine; DeletedAt triggers query filter exclusion
        server.Status = ServerStatus.Deleted;
        server.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new ServerDeleted(
            server.Id,
            organizationId,
            server.Name,
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Deleted server {ServerId} for org {OrgId}", serverId, organizationId);

        return ServiceResult.Ok(true);
    }

    private static ServerDetail MapToDetail(Server server)
    {
        ServerConfigurationDto? configDto = null;
        if (server.Configuration != null)
        {
            configDto = new ServerConfigurationDto(
                server.Configuration.StartupCommand,
                null, // GameSettings would need JSON parsing
                null, // EnvironmentVariables would need JSON parsing
                server.Configuration.AutoStart,
                server.Configuration.AutoRestartOnCrash,
                server.Configuration.MaxAutoRestartAttempts,
                server.Configuration.AutoRestartCooldownSeconds,
                server.Configuration.AutoRestartDelaySeconds,
                server.Configuration.ShutdownTimeoutSeconds,
                server.Configuration.JavaFlags);
        }

        var ports = server.Ports.Select(p => new ServerPortDto(
            p.Id,
            p.Name,
            p.Protocol,
            p.InternalPort,
            p.ExternalPort,
            p.IsPrimary)).ToList();

        return new ServerDetail(
            server.Id,
            server.OrganizationId,
            server.NodeId,
            server.Name,
            server.DisplayName,
            server.GameType,
            server.Status.ToString(),
            server.PowerState.ToString(),
            server.CpuLimitMillicores,
            server.MemoryLimitMb,
            server.DiskLimitMb,
            server.ReservationToken,
            server.LastStartedAt,
            server.LastStoppedAt,
            server.TotalUptimeSeconds,
            server.CrashCount,
            server.Tags,
            configDto,
            server.TemplateId,
            ports,
            server.CreatedAt,
            server.UpdatedAt);
    }
}
