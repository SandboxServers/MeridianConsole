using System.Text.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Service for recording and querying audit events.
/// Provides persistent audit trail for compliance and security analysis.
/// </summary>
public interface IAuditService
{
    /// <summary>Record a new audit event</summary>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>Record an audit event with common parameters</summary>
    Task RecordAsync(
        string eventType,
        Guid? userId = null,
        Guid? organizationId = null,
        Guid? actorUserId = null,
        string? targetType = null,
        Guid? targetId = null,
        object? details = null,
        string? clientIp = null,
        string? userAgent = null,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>Get audit events for a user</summary>
    Task<List<AuditEvent>> GetUserActivityAsync(
        Guid userId,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>Get audit events for an organization</summary>
    Task<List<AuditEvent>> GetOrganizationActivityAsync(
        Guid organizationId,
        DateTime? from = null,
        DateTime? to = null,
        string? eventType = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>Get count of audit events for retention management</summary>
    Task<int> GetEventCountAsync(
        DateTime? before = null,
        CancellationToken ct = default);

    /// <summary>Delete old audit events for retention management</summary>
    Task<int> DeleteEventsBeforeAsync(
        DateTime before,
        CancellationToken ct = default);
}

public sealed class AuditService : IAuditService
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AuditService(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        if (auditEvent.OccurredAtUtc == default)
        {
            auditEvent.OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        _dbContext.AuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RecordAsync(
        string eventType,
        Guid? userId = null,
        Guid? organizationId = null,
        Guid? actorUserId = null,
        string? targetType = null,
        Guid? targetId = null,
        object? details = null,
        string? clientIp = null,
        string? userAgent = null,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var auditEvent = new AuditEvent
        {
            EventType = eventType,
            UserId = userId,
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            TargetType = targetType,
            TargetId = targetId,
            Details = details is not null ? JsonSerializer.Serialize(details) : null,
            ClientIp = clientIp,
            UserAgent = userAgent,
            CorrelationId = correlationId,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        await RecordAsync(auditEvent, ct);
    }

    public async Task<List<AuditEvent>> GetUserActivityAsync(
        Guid userId,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        var query = _dbContext.AuditEvents
            .Where(e => e.UserId == userId || e.ActorUserId == userId);

        if (from.HasValue)
        {
            query = query.Where(e => e.OccurredAtUtc >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.OccurredAtUtc <= to.Value);
        }

        return await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<AuditEvent>> GetOrganizationActivityAsync(
        Guid organizationId,
        DateTime? from = null,
        DateTime? to = null,
        string? eventType = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        var query = _dbContext.AuditEvents
            .Where(e => e.OrganizationId == organizationId);

        if (from.HasValue)
        {
            query = query.Where(e => e.OccurredAtUtc >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.OccurredAtUtc <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        return await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<int> GetEventCountAsync(
        DateTime? before = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.AuditEvents.AsQueryable();

        if (before.HasValue)
        {
            query = query.Where(e => e.OccurredAtUtc < before.Value);
        }

        return await query.CountAsync(ct);
    }

    public async Task<int> DeleteEventsBeforeAsync(
        DateTime before,
        CancellationToken ct = default)
    {
        return await _dbContext.AuditEvents
            .Where(e => e.OccurredAtUtc < before)
            .ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// Standard event types for audit logging.
/// Use these constants to ensure consistency across the application.
/// </summary>
public static class AuditEventTypes
{
    // User events
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeleted = "user.deleted";
    public const string UserDeletionRequested = "user.deletion_requested";
    public const string UserAuthenticated = "user.authenticated";
    public const string UserAuthenticationFailed = "user.authentication_failed";

    // Organization events
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationDeleted = "organization.deleted";
    public const string OrganizationOwnershipTransferred = "organization.ownership_transferred";

    // Membership events
    public const string MembershipInvited = "membership.invited";
    public const string MembershipAccepted = "membership.accepted";
    public const string MembershipRejected = "membership.rejected";
    public const string MembershipWithdrawn = "membership.withdrawn";
    public const string MembershipRemoved = "membership.removed";
    public const string MembershipExpired = "membership.expired";

    // Role events
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string RoleAssigned = "role.assigned";
    public const string RoleRevoked = "role.revoked";

    // Claim events
    public const string ClaimGranted = "claim.granted";
    public const string ClaimRevoked = "claim.revoked";

    // OAuth events
    public const string OAuthAccountLinked = "oauth.account_linked";
    public const string OAuthAccountUnlinked = "oauth.account_unlinked";

    // Token events
    public const string TokenIssued = "token.issued";
    public const string TokenRefreshed = "token.refreshed";
    public const string TokenRevoked = "token.revoked";
    public const string SessionRevoked = "session.revoked";
    public const string AllSessionsRevoked = "session.all_revoked";

    // Security events
    public const string PrivilegeEscalationAttempt = "security.privilege_escalation_attempt";
    public const string AuthorizationDenied = "security.authorization_denied";
    public const string SuspiciousActivity = "security.suspicious_activity";
    public const string RateLimitExceeded = "security.rate_limit_exceeded";
}
