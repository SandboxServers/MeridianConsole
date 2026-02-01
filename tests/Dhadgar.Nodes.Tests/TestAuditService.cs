using System.Text.Json;
using Dhadgar.Contracts;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Test implementation of IAuditService that captures logged entries for verification.
/// </summary>
public sealed class TestAuditService : IAuditService
{
    private readonly List<AuditEntry> _entries = [];

    /// <summary>
    /// Gets all logged audit entries.
    /// </summary>
    public IReadOnlyList<AuditEntry> Entries => _entries;

    /// <summary>
    /// Clears all captured entries.
    /// </summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Gets the last entry matching a specific action.
    /// </summary>
    public AuditEntry? GetLastEntryForAction(string action) =>
        _entries.LastOrDefault(e => e.Action == action);

    /// <summary>
    /// Checks if any entry with the specified action exists.
    /// </summary>
    public bool HasEntry(string action) =>
        _entries.Any(e => e.Action == action);

    /// <summary>
    /// Gets all entries with the specified action.
    /// </summary>
    public IReadOnlyList<AuditEntry> GetEntriesForAction(string action) =>
        _entries.Where(e => e.Action == action).ToList();

    public Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task LogAsync(
        string action,
        string resourceType,
        Guid? resourceId,
        AuditOutcome outcome,
        object? details = null,
        string? resourceName = null,
        Guid? organizationId = null,
        string? failureReason = null,
        CancellationToken ct = default)
    {
        var entry = new AuditEntry
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = outcome,
            Details = details,
            ResourceName = resourceName,
            OrganizationId = organizationId,
            FailureReason = failureReason
        };

        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<PagedResponse<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        // Simple in-memory query for testing
        var filtered = _entries.AsEnumerable();

        if (query.OrganizationId.HasValue)
        {
            filtered = filtered.Where(e => e.OrganizationId == query.OrganizationId.Value);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            filtered = filtered.Where(e => e.Action == query.Action);
        }

        if (query.ResourceId.HasValue)
        {
            filtered = filtered.Where(e => e.ResourceId == query.ResourceId.Value);
        }

        if (query.Outcome.HasValue)
        {
            filtered = filtered.Where(e => e.Outcome == query.Outcome.Value);
        }

        // Materialize to list to avoid multiple enumeration (CA1851)
        var filteredList = filtered.ToList();

        var total = filteredList.Count;
        var page = Math.Max(1, query.Page);
        var pageSize = query.EffectivePageSize;
        var skip = (page - 1) * pageSize;

        var items = filteredList
            .Skip(skip)
            .Take(pageSize)
            .Select(e => new AuditLogDto
            {
                // Preserve fidelity: use actual entry fields, not synthetic values
                Id = e.Id,
                Timestamp = e.Timestamp,
                ActorId = e.ActorIdOverride ?? "test-actor",
                ActorType = (e.ActorTypeOverride ?? ActorType.User).ToString(),
                Action = e.Action,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                ResourceName = e.ResourceName,
                OrganizationId = e.OrganizationId,
                Outcome = e.Outcome.ToString(),
                FailureReason = e.FailureReason,
                Details = e.Details is not null ? JsonSerializer.Serialize(e.Details) : null
            })
            .ToList();

        return Task.FromResult(new PagedResponse<AuditLogDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        });
    }
}
