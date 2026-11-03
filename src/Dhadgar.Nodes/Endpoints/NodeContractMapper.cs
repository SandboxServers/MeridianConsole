using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Endpoints;

/// <summary>F356: the user API's wire shape is the Contracts DTOs; the service keeps its own models.</summary>
internal static class NodeContractMapper
{
    internal static Dhadgar.Contracts.FilteredPagedResponse<NodeListItemResponse> ToContract(Dhadgar.Nodes.Models.FilteredPagedResponse<NodeListItem> page) =>
        new()
        {
            Items = page.Items.Select(ToContract).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            Total = page.Total,
            Filters = ToContract(page.Filters),
        };

    internal static NodeListItemResponse ToContract(NodeListItem n) =>
        new(n.Id.ToString(), n.Name, n.DisplayName, n.Status.ToString(), n.Platform,
            Utc(n.LastHeartbeat), Utc(n.CreatedAt), n.HealthScore, n.ActiveServers, n.Tags);

    internal static NodeListFilters ToContract(FilterMetadata f) =>
        new(f.Status?.ToString(), f.Platform, f.MinHealthScore, f.MaxHealthScore, f.HasActiveServers,
            f.Search, f.Tags, f.SortBy, f.SortOrder, f.IncludeDecommissioned);

    internal static NodeDetailResponse ToContract(NodeDetail d) =>
        new(d.Id.ToString(), d.OrganizationId.ToString(), d.Name, d.DisplayName, d.Status.ToString(), d.AgentVersion, d.Platform,
            Utc(d.LastHeartbeat), Utc(d.CreatedAt), d.Tags,
            d.Hardware is { } h ? new NodeHardwareResponse(h.Hostname, h.OsVersion, h.CpuCores, h.MemoryBytes, h.DiskBytes, Utc(h.CollectedAt)) : null,
            d.Health is { } x ? new NodeHealthResponse(x.CpuUsagePercent, x.MemoryUsagePercent, x.DiskUsagePercent, x.ActiveGameServers,
                x.HealthScore, x.HealthTrend, HealthIssues: null, Utc(x.ReportedAt)) : null,
            d.Capacity is { } c ? new NodeCapacityResponse(c.MaxGameServers, c.CurrentGameServers, c.AvailableMemoryBytes, c.AvailableDiskBytes, Utc(c.UpdatedAt)) : null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}
