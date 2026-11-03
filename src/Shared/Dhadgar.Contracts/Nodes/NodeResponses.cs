namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Detailed view of a node including hardware, health, and capacity information.
/// </summary>
/// <param name="Id">Unique identifier of the node.</param>
/// <param name="OrganizationId">Organization that owns this node.</param>
/// <param name="Name">Machine-readable node name (lowercase alphanumeric with hyphens).</param>
/// <param name="DisplayName">Human-friendly display name.</param>
/// <param name="Status">Current operational status of the node.</param>
/// <param name="AgentVersion">Version of the agent software running on the node.</param>
/// <param name="Platform">Operating system platform (linux or windows).</param>
/// <param name="LastHeartbeat">Timestamp of the most recent heartbeat, if any.</param>
/// <param name="CreatedAt">When the node was enrolled.</param>
/// <param name="Tags">Tags assigned to the node for filtering and organization.</param>
/// <param name="Hardware">Hardware specifications collected from the node.</param>
/// <param name="Health">Current health metrics from the node.</param>
/// <param name="Capacity">Capacity tracking for game server slots.</param>
public record NodeDetailResponse(
    string Id,
    string OrganizationId,
    string Name,
    string? DisplayName,
    string Status,
    string? AgentVersion,
    string Platform,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? Tags,
    NodeHardwareResponse? Hardware,
    NodeHealthResponse? Health,
    NodeCapacityResponse? Capacity);

/// <summary>
/// Hardware specifications collected from a node.
/// </summary>
/// <param name="Hostname">Network hostname of the machine.</param>
/// <param name="OsVersion">Operating system version string.</param>
/// <param name="CpuCores">Number of CPU cores available.</param>
/// <param name="MemoryBytes">Total physical memory in bytes.</param>
/// <param name="DiskBytes">Total disk space in bytes.</param>
/// <param name="CollectedAt">When the hardware information was last collected.</param>
public record NodeHardwareResponse(
    string Hostname,
    string? OsVersion,
    int CpuCores,
    long MemoryBytes,
    long DiskBytes,
    DateTimeOffset CollectedAt);

/// <summary>
/// Health metrics from a node.
/// </summary>
/// <param name="CpuUsagePercent">Current CPU utilization percentage (0-100).</param>
/// <param name="MemoryUsagePercent">Current memory utilization percentage (0-100).</param>
/// <param name="DiskUsagePercent">Current disk utilization percentage (0-100).</param>
/// <param name="ActiveGameServers">Number of game servers currently running.</param>
/// <param name="HealthScore">Calculated health score (0-100, higher is healthier).</param>
/// <param name="HealthTrend">Trend direction: improving, stable, or degrading.</param>
/// <param name="HealthIssues">List of current health issues, if any.</param>
/// <param name="ReportedAt">When these metrics were reported.</param>
public record NodeHealthResponse(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    int ActiveGameServers,
    int? HealthScore,
    string? HealthTrend,
    IReadOnlyList<string>? HealthIssues,
    DateTimeOffset ReportedAt);

/// <summary>
/// Capacity information for a node.
/// </summary>
/// <param name="MaxGameServers">Maximum number of game servers this node can host.</param>
/// <param name="CurrentGameServers">Number of game servers currently running.</param>
/// <param name="AvailableMemoryBytes">Available memory in bytes.</param>
/// <param name="AvailableDiskBytes">Available disk space in bytes.</param>
/// <param name="UpdatedAt">When capacity information was last updated.</param>
public record NodeCapacityResponse(
    int MaxGameServers,
    int CurrentGameServers,
    long AvailableMemoryBytes,
    long AvailableDiskBytes,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Enhanced node list item with additional fields for list views.
/// </summary>
/// <param name="Id">Unique identifier of the node.</param>
/// <param name="Name">Machine-readable node name.</param>
/// <param name="DisplayName">Human-friendly display name.</param>
/// <param name="Status">Current operational status.</param>
/// <param name="Platform">Operating system platform.</param>
/// <param name="LastHeartbeat">Timestamp of most recent heartbeat.</param>
/// <param name="CreatedAt">When the node was enrolled.</param>
/// <param name="HealthScore">Current health score (0-100).</param>
/// <param name="ActiveServers">Number of active game servers.</param>
/// <param name="Tags">Tags assigned to the node.</param>
public record NodeListItemResponse(
    string Id,
    string Name,
    string? DisplayName,
    string Status,
    string Platform,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset CreatedAt,
    int? HealthScore,
    int ActiveServers,
    IReadOnlyList<string> Tags);

/// <summary>
/// Filters applied to a node list query, echoed back in <see cref="Dhadgar.Contracts.FilteredPagedResponse{T}.Filters"/>.
/// </summary>
public record NodeListFilters(
    string? Status,
    string? Platform,
    int? MinHealthScore,
    int? MaxHealthScore,
    bool? HasActiveServers,
    string? Search,
    IReadOnlyList<string>? Tags,
    string SortBy,
    string SortOrder,
    bool IncludeDecommissioned);
