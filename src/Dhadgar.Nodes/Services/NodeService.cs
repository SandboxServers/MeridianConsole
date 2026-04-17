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

// Alias local models to avoid ambiguity with Contracts types
using LocalUpdateNodeRequest = Dhadgar.Nodes.Models.UpdateNodeRequest;
using LocalUpdateNodeTagsRequest = Dhadgar.Nodes.Models.UpdateNodeTagsRequest;

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

    public async Task<Dhadgar.Nodes.Models.FilteredPagedResponse<NodeListItem>> GetNodesAsync(
        Guid organizationId,
        NodeListQuery query,
        CancellationToken ct = default)
    {
        // Start with base query for organization
        // Use IgnoreQueryFilters when decommissioned nodes are requested
        IQueryable<Node> dbQuery = query.IncludeDecommissioned
            ? _dbContext.Nodes.IgnoreQueryFilters()
            : _dbContext.Nodes;

        dbQuery = dbQuery
            .AsNoTracking()
            .Include(n => n.Health)
            .Where(n => n.OrganizationId == organizationId);

        // Apply filters
        dbQuery = ApplyFilters(dbQuery, query);

        // Get total count before pagination
        var total = await dbQuery.CountAsync(ct);

        // Apply sorting
        dbQuery = ApplySorting(dbQuery, query);

        // Apply pagination
        // Health score calculation inlined for EF Core translation:
        // Score = 100 - avg(cpu, memory, disk) with rounding
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
                n.Health != null
                    ? (int)Math.Round(100.0 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3.0)
                    : null,
                n.Health != null ? n.Health.ActiveGameServers : 0,
                n.Tags))
            .ToListAsync(ct);

        _logger.LogDebug(
            "Listed {Count} nodes for organization {OrgId} (total: {Total}, filters applied: {FiltersApplied})",
            nodes.Count, organizationId, total, GetAppliedFiltersDescription(query));

        return FilteredPagedResponse.Create(nodes, total, query);
    }

    public async Task<ServiceResult<NodeDetail>> GetNodeAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .AsNoTracking()
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<NodeDetail>("node_not_found");
        }

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<NodeDetail>> UpdateNodeAsync(
        Guid organizationId,
        Guid nodeId,
        LocalUpdateNodeRequest request,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

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
                    n.OrganizationId == organizationId &&
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

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ServiceResult.Fail<NodeDetail>("name_already_exists");
        }

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeUpdated,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { Name = request.Name, DisplayName = request.DisplayName },
            resourceName: node.Name,
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} updated", nodeId);

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<NodeDetail>> UpdateNodeTagsAsync(
        Guid organizationId,
        Guid nodeId,
        LocalUpdateNodeTagsRequest request,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .Include(n => n.HardwareInventory)
            .Include(n => n.Health)
            .Include(n => n.Capacity)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<NodeDetail>("node_not_found");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedTags = request.GetNormalizedTags();

        node.Tags = normalizedTags.ToList();
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
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} tags updated to [{Tags}]",
            nodeId, string.Join(", ", normalizedTags));

        var detail = MapToDetail(node);
        return ServiceResult.Ok(detail);
    }

    public async Task<ServiceResult<bool>> DecommissionNodeAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default)
    {
        // Use IgnoreQueryFilters to find node even if already soft-deleted (idempotency)
        var node = await _dbContext.Nodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<bool>("node_not_found");
        }

        // Idempotent: if already decommissioned, return success
        if (node.Status == NodeStatus.Decommissioned)
        {
            return ServiceResult.Ok(true);
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

        // Publish event BEFORE SaveChangesAsync so it's part of the same transaction
        // (MassTransit outbox intercepts and stores in outbox table atomically)
        await _publishEndpoint.Publish(new NodeDecommissioned(nodeId, now), ct);

        await _dbContext.SaveChangesAsync(ct);

        NodesMetrics.NodesDecommissioned.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeDecommissioned,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { CertificatesRevoked = activeCerts.Count },
            resourceName: node.Name,
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} decommissioned", nodeId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> EnterMaintenanceAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default)
    {
        // Use IgnoreQueryFilters to check if node is decommissioned
        var node = await _dbContext.Nodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

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

        // Publish event BEFORE SaveChangesAsync so it's part of the same transaction
        // (MassTransit outbox intercepts and stores in outbox table atomically)
        await _publishEndpoint.Publish(new NodeMaintenanceStarted(nodeId, now), ct);

        await _dbContext.SaveChangesAsync(ct);

        NodesMetrics.MaintenanceEntered.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeMaintenanceStarted,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            resourceName: node.Name,
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} entered maintenance mode", nodeId);

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<bool>> ExitMaintenanceAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.OrganizationId == organizationId, ct);

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
        // Null LastHeartbeat is treated as Offline (node has never checked in)
        var heartbeatThreshold = now.AddMinutes(-_options.HeartbeatThresholdMinutes);
        node.Status = node.LastHeartbeat.HasValue && node.LastHeartbeat >= heartbeatThreshold
            ? NodeStatus.Online
            : NodeStatus.Offline;

        node.UpdatedAt = now;

        // Publish event BEFORE SaveChangesAsync so it's part of the same transaction
        // (MassTransit outbox intercepts and stores in outbox table atomically)
        await _publishEndpoint.Publish(new NodeMaintenanceEnded(nodeId, now), ct);

        await _dbContext.SaveChangesAsync(ct);

        NodesMetrics.MaintenanceExited.Add(1);

        // Audit log
        await _auditService.LogAsync(
            AuditActions.NodeMaintenanceEnded,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { NewStatus = node.Status.ToString() },
            resourceName: node.Name,
            organizationId: organizationId,
            ct: ct);

        _logger.LogInformation("Node {NodeId} exited maintenance mode, new status: {Status}",
            nodeId, node.Status);

        return ServiceResult.Ok(true);
    }

    #region Private Helper Methods

    /// <summary>
    /// Checks if a DbUpdateException is caused by a unique constraint violation.
    /// This handles race conditions where a duplicate name is inserted between
    /// the pre-check and the SaveChangesAsync call.
    /// </summary>
    /// <remarks>
    /// Uses reflection to detect provider-specific exception types and error codes:
    /// <list type="bullet">
    ///   <item><description>PostgreSQL (Npgsql): SqlState "23505" (unique_violation)</description></item>
    ///   <item><description>SQL Server: Error numbers 2601 (unique index) and 2627 (unique constraint)</description></item>
    ///   <item><description>SQLite: Extended error codes 2067 (SQLITE_CONSTRAINT_UNIQUE) and 1555 (SQLITE_CONSTRAINT_PRIMARYKEY)</description></item>
    /// </list>
    /// Falls back to message-based detection if provider-specific checks don't match.
    /// </remarks>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Check for provider-specific exception types and error codes
        var inner = ex.InnerException;

        // PostgreSQL: SqlState "23505" is unique_violation
        if (inner?.GetType().FullName == "Npgsql.PostgresException")
        {
            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (sqlState == "23505") return true;
        }

        // SQL Server: Error numbers 2601 (unique index) and 2627 (unique constraint)
        if (inner?.GetType().FullName == "Microsoft.Data.SqlClient.SqlException")
        {
            var number = inner.GetType().GetProperty("Number")?.GetValue(inner) as int?;
            if (number == 2601 || number == 2627) return true;
        }

        // SQLite: SQLITE_CONSTRAINT_UNIQUE (2067) or SQLITE_CONSTRAINT_PRIMARYKEY (1555)
        if (inner?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException")
        {
            var errorCode = inner.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(inner) as int?;
            if (errorCode == 2067 || errorCode == 1555) return true;
        }

        // Fallback to message-based detection as last resort
        var innerMessage = inner?.Message ?? string.Empty;
        return innerMessage.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || innerMessage.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || innerMessage.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    // IMPORTANT: Health score formula must be consistent across all query expressions.
    // Formula: (int)Math.Round(100.0 - (CpuUsagePercent + MemoryUsagePercent + DiskUsagePercent) / 3.0)
    // This formula is inlined in EF Core LINQ expressions because it must translate to SQL.
    // Changes here must be reflected in: ApplyFilters, ApplySorting, and the LINQ Select in GetNodesAsync.

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
            // CA1862 suppressed: string.Equals with StringComparison doesn't translate reliably
            // across EF Core database providers. Using ToLowerInvariant() translates to SQL LOWER().
#pragma warning disable CA1862 // Use StringComparison method overloads
            query = query.Where(n => n.Platform.ToLowerInvariant() == platform);
#pragma warning restore CA1862
        }

        // Filter by health score range (requires Health navigation)
        // Uses same formula as CalculateHealthScore for consistency
        if (filters.MinHealthScore.HasValue || filters.MaxHealthScore.HasValue)
        {
            if (filters.MinHealthScore.HasValue)
            {
                var min = filters.MinHealthScore.Value;
                query = query.Where(n =>
                    n.Health != null &&
                    (int)Math.Round(100.0 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3.0) >= min);
            }

            if (filters.MaxHealthScore.HasValue)
            {
                var max = filters.MaxHealthScore.Value;
                // Only include nodes with health data when filtering by max score
                query = query.Where(n =>
                    n.Health != null &&
                    (int)Math.Round(100.0 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3.0) <= max);
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

            // CA1862 suppressed: StringComparison overloads don't translate consistently across
            // EF Core database providers (PostgreSQL production vs InMemory tests).
            // Using ToLowerInvariant() on both sides translates to SQL LOWER() reliably.
#pragma warning disable CA1862 // Use StringComparison method overloads
            query = query.Where(n =>
                n.Name.ToLowerInvariant().Contains(searchTerm) ||
                (n.DisplayName != null && n.DisplayName.ToLowerInvariant().Contains(searchTerm)));
#pragma warning restore CA1862
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
        var sortBy = (filters.SortBy ?? "name").ToLowerInvariant();

        return sortBy switch
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
                    (int)Math.Round(100.0 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3.0))
                : query.OrderByDescending(n => n.Health == null ? 0 :
                    (int)Math.Round(100.0 - (n.Health.CpuUsagePercent + n.Health.MemoryUsagePercent + n.Health.DiskUsagePercent) / 3.0)),

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
        var containsMethod = typeof(List<string>).GetMethod("Contains", new[] { typeof(string) })!;

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
