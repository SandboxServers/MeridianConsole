using Dhadgar.Contracts;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// An audit sink for the UAT token mint (P19). The mint runs outside the Nodes process, so there is no request
/// context to audit against; every member completes at once and records nothing.
/// </summary>
internal sealed class NoopAuditService : IAuditService
{
    /// <inheritdoc />
    public Task LogAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task LogAsync(
        string action,
        string resourceType,
        Guid? resourceId,
        AuditOutcome outcome,
        object? details = null,
        string? resourceName = null,
        Guid? organizationId = null,
        string? failureReason = null,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<PagedResponse<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResponse<AuditLogDto>
        {
            Items = Array.Empty<AuditLogDto>(),
            Page = 1,
            PageSize = 0,
            Total = 0,
        });
}
