using Dhadgar.Contracts.Nodes;
using Refit;
using System.Collections.ObjectModel;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Nodes Service API calls.
/// DTOs are defined in Dhadgar.Contracts.Nodes namespace.
/// </summary>
public interface INodesApi
{
    // ========================================================================
    // Node Management (User-facing)
    // ========================================================================

    [Get("/organizations/{orgId}/nodes")]
    Task<Dhadgar.Contracts.FilteredPagedResponse<NodeListItemResponse>> GetNodesAsync(
        string orgId,
        [AliasAs("page")] int? page = null,
        [AliasAs("pageSize")] int? pageSize = null,
        [AliasAs("status")] string? status = null,
        CancellationToken ct = default);

    [Get("/organizations/{orgId}/nodes/{nodeId}")]
    Task<NodeDetailResponse> GetNodeAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Patch("/organizations/{orgId}/nodes/{nodeId}")]
    Task<NodeDetailResponse> UpdateNodeAsync(
        string orgId,
        string nodeId,
        [Body] UpdateNodeRequest request,
        CancellationToken ct = default);

    [Delete("/organizations/{orgId}/nodes/{nodeId}")]
    Task DecommissionNodeAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Post("/organizations/{orgId}/nodes/{nodeId}/maintenance")]
    Task EnterMaintenanceAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Delete("/organizations/{orgId}/nodes/{nodeId}/maintenance")]
    Task ExitMaintenanceAsync(string orgId, string nodeId, CancellationToken ct = default);

    // ========================================================================
    // Enrollment Token Management (User-facing)
    // ========================================================================

    [Post("/organizations/{orgId}/enrollment/tokens")]
    Task<CreateEnrollmentTokenResponse> CreateEnrollmentTokenAsync(
        string orgId,
        [Body] CreateEnrollmentTokenRequest request,
        CancellationToken ct = default);

    [Get("/organizations/{orgId}/enrollment/tokens")]
    Task<Collection<EnrollmentTokenResponse>> GetEnrollmentTokensAsync(
        string orgId,
        CancellationToken ct = default);

    [Delete("/organizations/{orgId}/enrollment/tokens/{tokenId}")]
    Task RevokeEnrollmentTokenAsync(string orgId, string tokenId, CancellationToken ct = default);
}
