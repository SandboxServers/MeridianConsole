using System.Linq.Expressions;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

public sealed class NodeService : INodeService
{
    private readonly NodesDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NodeService> _logger;
    private readonly NodesOptions _options;


    public NodeService(
        NodesDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IAuditService auditService,
        TimeProvider timeProvider,
        IOptions<NodesOptions> options,
        ILogger<NodeService> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FilteredPagedResponse<NodeListItem>> GetNodesAsync(
        Guid organizationId,
        NodeListQuery query,
        CancellationToken ct = default)
    {
        // Start with base query for organization
        IQueryable<Node> dbQuery = _dbContext.Nodes
            .AsNoTracking()
            .Include(n => n.Health)
            .Where(n => n.OrganizationId == organizationId);

        // Include decommissioned nodes if requested (bypass the global query filter)
        if (query.IncludeDecommissioned)
        {
            dbQuery = _dbContext.Nodes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(n => n.Health)
                .Where(n => n.OrganizationId == organizationId);
        }

        // Apply filters
        dbQuery = ApplyFilters(dbQuery, query);

        // Get total count before pagination
        var total = await dbQuery.CountAsync(ct);

        // Apply sorting
        dbQuery = ApplySorting(dbQuery, query);

        // Apply pagination
        var nodes = await dbQuery
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .Select(n => new NodeListItem(
                n.Id,
                n.Name,
                n.DisplayName,
                n.Status,
                n.Platform,
                n.LastHeartbeat,
                n.CreatedAt,
                n.Health != null ? CalculateHealthScore(n.Health) : null,
                n.Health != null ? n.Health.ActiveGameServers : 0,
                n.Tags))
            .ToListAsync(ct);

        _logger.LogDebug(
            "Listed {Count} nodes for organization {OrgId} (total: {Total}, filters applied: {FiltersApplied})",
            nodes.Count, organizationId, total, GetAppliedFiltersDescription(query));

        return FilteredPagedResponse.Create(nodes, total, query);
    }

    public async Task<ServiceResult<NodeDetail>> GetNodeAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .AsNoTracking()
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<NodeDetail>("node_not_found");
        }

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<NodeDetail>> UpdateNodeAsync(
        Guid nodeId,
        UpdateNodeRequest request,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<NodeDetail>("node_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            // Check for duplicate name within organization
            var nameExists = await _dbContext.Nodes
                .AnyAsync(n =>
                    n.OrganizationId == node.OrganizationId &&
                    n.Name == request.Name.Trim() &&
                    n.Id != nodeId,
                    ct);

            if (nameExists)
            {
                return ServiceResult.Fail<NodeDetail>("name_already_exists");
            }

            node.Name = request.Name.Trim();
        }

        if (request.DisplayName is not null)
        {
            node.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? null
                : request.DisplayName.Trim();
        }

        node.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeUpdated,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { Name = request.Name, DisplayName = request.DisplayName },
            resourceName: node.Name,
            organizationId: node.OrganizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} updated", nodeId);

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<NodeDetail>> UpdateNodeTagsAsync(
        Guid nodeId,
        UpdateNodeTagsRequest request,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<NodeDetail>("node_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedTags = request.GetNormalizedTags();

        node.Tags = normalizedTags;
        node.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeUpdated,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { Tags = normalizedTags },
            resourceName: node.Name,
            organizationId: node.OrganizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} tags updated to [{Tags}]",
            nodeId, string.Join(", ", normalizedTags));

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<bool>> DecommissionNodeAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<bool>("node_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        node.Status = NodeStatus.Decommissioned;
        node.DeletedAt = now;
        node.UpdatedAt = now;

        // Revoke all active certificates
        var activeCerts = await _dbContext.AgentCertificates
            .Where(c => c.NodeId == nodeId && !c.IsRevoked)
            .ToListAsync(ct);

        foreach (var cert in activeCerts)
        {
            cert.IsRevoked = true;
            cert.RevokedAt = now;
            cert.RevocationReason = "Node decommissioned";
        }

        await _dbContext.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new NodeDecommissioned(nodeId, now), ct);

        NodesMetrics.NodesDecommissioned.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeDecommissioned,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { CertificatesRevoked = activeCerts.Count },
            resourceName: node.Name,
            organizationId: node.OrganizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} decommissioned", nodeId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> EnterMaintenanceAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<bool>("node_not_found");
        }

        if (node.Status == NodeStatus.Maintenance)
        {
            return ServiceResult.Fail<bool>("already_in_maintenance");
        }

        if (node.Status == NodeStatus.Decommissioned)
        {
            return ServiceResult.Fail<bool>("node_decommissioned");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        node.Status = NodeStatus.Maintenance;
        node.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new NodeMaintenanceStarted(nodeId, now), ct);

        NodesMetrics.MaintenanceEntered.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeMaintenanceStarted,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            resourceName: node.Name,
            organizationId: node.OrganizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} entered maintenance mode", nodeId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> ExitMaintenanceAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<bool>("node_not_found");
        }

        if (node.Status != NodeStatus.Maintenance)
        {
            return ServiceResult.Fail<bool>("not_in_maintenance");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Determine new status based on last heartbeat
        var heartbeatThreshold = now.AddMinutes(-_options.HeartbeatThresholdMinutes);
        node.Status = node.LastHeartbeat >= heartbeatThreshold
            ? NodeStatus.Online
            : NodeStatus.Offline;

        node.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new NodeMaintenanceEnded(nodeId, now), ct);

        NodesMetrics.MaintenanceExited.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeMaintenanceEnded,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { NewStatus = node.Status.ToString() },
            resourceName: node.Name,
            organizationId: node.OrganizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} exited maintenance mode, new status: {Status}",
            nodeId, node.Status);

        return ServiceResult.Ok(true);
    }

    #region Private Helper Methods

    private static IQueryable<Node> ApplyFilters(IQueryable<Node> query, NodeListQuery filters)
    {
        // Filter by status
        if (filters.Status.HasValue)
        {
            query = query.Where(n => n.Status == filters.Status.Value);
        }

        // Filter by platform (case-insensitive)
        if (!string.IsNullOrWhiteSpace(filters.Platform))
        {
            var platform = filters.Platform.Trim().ToLowerInvariant();
            query = query.Where(n => n.Platform.ToLower() == platform);
        }

        // Filter by health score range (requires Health navigation)
        if (filters.MinHealthScore.HasValue || filters.MaxHealthScore.HasValue)
        {
            // Health score is calculated from CPU, memory, and disk usage
            // We need to calculate it in the query
            if (filters.MinHealthScore.HasValue)
            {
                var min = filters.MinHealthScore.Value;
                query = query.Where(n =>
                    n.Health != null &&
                    (100 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3) >= min);
            }

            if (filters.MaxHealthScore.HasValue)
            {
                var max = filters.MaxHealthScore.Value;
                query = query.Where(n =>
                    n.Health == null ||
                    (100 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3) <= max);
            }
        }

        // Filter by active servers
        if (filters.HasActiveServers.HasValue)
        {
            if (filters.HasActiveServers.Value)
            {
                query = query.Where(n => n.Health != null && n.Health.ActiveGameServers > 0);
            }
            else
            {
                query = query.Where(n => n.Health == null || n.Health.ActiveGameServers == 0);
            }
        }

        // Full-text search on name and displayName (case-insensitive contains)
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var searchTerm = filters.Search.Trim().ToLowerInvariant();

            query = query.Where(n =>
                n.Name.ToLower().Contains(searchTerm) ||
                (n.DisplayName != null && n.DisplayName.ToLower().Contains(searchTerm)));
        }

        // Filter by tags (any match)
        var requestedTags = filters.ParseTagsFilter();
        if (requestedTags.Count > 0)
        {
            // Build predicate: n.Tags.Contains("tag1") || n.Tags.Contains("tag2") || ...
            // This works with both PostgreSQL and InMemory providers
            var tagPredicate = BuildTagFilterPredicate(requestedTags);
            query = query.Where(tagPredicate);
        }

        return query;
    }

    private static IQueryable<Node> ApplySorting(IQueryable<Node> query, NodeListQuery filters)
    {
        var isAscending = filters.IsAscending;

        return filters.SortBy.ToLowerInvariant() switch
        {
            "name" => isAscending
                ? query.OrderBy(n => n.Name)
                : query.OrderByDescending(n => n.Name),

            "displayname" => isAscending
                ? query.OrderBy(n => n.DisplayName ?? n.Name)
                : query.OrderByDescending(n => n.DisplayName ?? n.Name),

            "status" => isAscending
                ? query.OrderBy(n => n.Status)
                : query.OrderByDescending(n => n.Status),

            "healthscore" => isAscending
                ? query.OrderBy(n => n.Health == null ? 0 :
                    100 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3)
                : query.OrderByDescending(n => n.Health == null ? 0 :
                    100 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3),

            "lastheartbeat" => isAscending
                ? query.OrderBy(n => n.LastHeartbeat)
                : query.OrderByDescending(n => n.LastHeartbeat),

            "createdat" => isAscending
                ? query.OrderBy(n => n.CreatedAt)
                : query.OrderByDescending(n => n.CreatedAt),

            "activeservers" => isAscending
                ? query.OrderBy(n => n.Health == null ? 0 : n.Health.ActiveGameServers)
                : query.OrderByDescending(n => n.Health == null ? 0 : n.Health.ActiveGameServers),

            // Default to name
            _ => isAscending
                ? query.OrderBy(n => n.Name)
                : query.OrderByDescending(n => n.Name)
        };
    }

    /// <summary>
    /// Builds a predicate expression for tag filtering that works with both
    /// PostgreSQL and InMemory providers.
    /// </summary>
    /// <remarks>
    /// The InMemory provider cannot translate `requestedTags.Contains(t)` when
    /// iterating over a collection navigation property. This method builds
    /// individual `Tags.Contains("tag")` calls combined with OR.
    /// </remarks>
    private static Expression<Func<Node, bool>> BuildTagFilterPredicate(IReadOnlyList<string> requestedTags)
    {
        var parameter = Expression.Parameter(typeof(Node), "n");
        var tagsProperty = Expression.Property(parameter, nameof(Node.Tags));
        var containsMethod = typeof(List<string>).GetMethod("Contains", [typeof(string)])!;

        Expression? combinedCondition = null;

        foreach (var tag in requestedTags)
        {
            var tagConstant = Expression.Constant(tag);
            var containsCall = Expression.Call(tagsProperty, containsMethod, tagConstant);

            combinedCondition = combinedCondition == null
                ? containsCall
                : Expression.OrElse(combinedCondition, containsCall);
        }

        // If no tags, return a predicate that always returns false (shouldn't happen due to count check)
        combinedCondition ??= Expression.Constant(false);

        return Expression.Lambda<Func<Node, bool>>(combinedCondition, parameter);
    }

    /// <summary>
    /// Calculates a health score (0-100) based on resource usage.
    /// Higher score = healthier node.
    /// </summary>
    private static int? CalculateHealthScore(NodeHealth? health)
    {
        if (health is null)
        {
            return null;
        }

        // Average of (100 - each usage percentage)
        var avgUsage = (health.CpuUsagePercent + health.MemoryUsagePercent + health.DiskUsagePercent) / 3;
        return (int)Math.Round(100 - avgUsage);
    }

    /// <summary>
    /// Gets a description of applied filters for logging.
    /// </summary>
    private static string GetAppliedFiltersDescription(NodeListQuery query)
    {
        var filters = new List<string>();

        if (query.Status.HasValue)
            filters.Add($"status={query.Status}");
        if (!string.IsNullOrWhiteSpace(query.Platform))
            filters.Add($"platform={query.Platform}");
        if (query.MinHealthScore.HasValue)
            filters.Add($"minHealth={query.MinHealthScore}");
        if (query.MaxHealthScore.HasValue)
            filters.Add($"maxHealth={query.MaxHealthScore}");
        if (query.HasActiveServers.HasValue)
            filters.Add($"hasServers={query.HasActiveServers}");
        if (!string.IsNullOrWhiteSpace(query.Search))
            filters.Add($"search={query.Search}");
        if (!string.IsNullOrWhiteSpace(query.Tags))
            filters.Add($"tags={query.Tags}");

        return filters.Count > 0 ? string.Join(", ", filters) : "none";
    }

    private static NodeDetail MapToDetail(Node node)
    {
        return new NodeDetail(
            node.Id,
            node.OrganizationId,
            node.Name,
            node.DisplayName,
            node.Status,
            node.AgentVersion,
            node.Platform,
            node.LastHeartbeat,
            node.CreatedAt,
            node.Tags,
            node.HardwareInventory is not null
                ? new NodeHardwareDto(
                    node.HardwareInventory.Hostname,
                    node.HardwareInventory.OsVersion,
                    node.HardwareInventory.CpuCores,
                    node.HardwareInventory.MemoryBytes,
                    node.HardwareInventory.DiskBytes,
                    node.HardwareInventory.CollectedAt)
                : null,
            node.Health is not null
                ? new NodeHealthDto(
                    node.Health.CpuUsagePercent,
                    node.Health.MemoryUsagePercent,
                    node.Health.DiskUsagePercent,
                    node.Health.ActiveGameServers,
                    node.Health.HealthScore,
                    node.Health.HealthTrend.ToString(),
                    node.Health.LastScoreChange,
                    node.Health.ReportedAt)
                : null,
            node.Capacity is not null
                ? new NodeCapacityDto(
                    node.Capacity.MaxGameServers,
                    node.Capacity.CurrentGameServers,
                    node.Capacity.AvailableMemoryBytes,
                    node.Capacity.AvailableDiskBytes,
                    node.Capacity.UpdatedAt)
                : null);
    }

    #endregion
}
