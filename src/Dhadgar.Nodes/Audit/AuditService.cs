using System.Text.Json;
using Dhadgar.Contracts;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Audit;

/// <summary>
/// Implementation of audit logging service.
/// Logs all operations to both the database and structured logging for SIEM integration.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly NodesDbContext _dbContext;
    private readonly IAuditContextAccessor _contextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AuditService(
        NodesDbContext dbContext,
        IAuditContextAccessor contextAccessor,
        TimeProvider timeProvider,
        ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _contextAccessor = contextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var actorId = entry.ActorIdOverride ?? _contextAccessor.GetActorId();
        var actorType = entry.ActorTypeOverride ?? _contextAccessor.GetActorType();

        var auditLog = new NodeAuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            ActorId = actorId,
            ActorType = actorType,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            ResourceName = entry.ResourceName,
            OrganizationId = entry.OrganizationId,
            Outcome = entry.Outcome,
            FailureReason = entry.FailureReason,
            Details = entry.Details is not null
                ? JsonSerializer.Serialize(entry.Details, JsonOptions)
                : null,
            CorrelationId = _contextAccessor.GetCorrelationId(),
            RequestId = _contextAccessor.GetRequestId(),
            IpAddress = _contextAccessor.GetIpAddress(),
            UserAgent = _contextAccessor.GetUserAgent()
        };

        try
        {
            // Log to database
            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Log database failure separately - audit entry will still be emitted to SIEM
            _logger.LogError(ex, "Failed to persist audit log to database for action {Action}", auditLog.Action);
            throw;
        }
        finally
        {
            // Always log to structured logging for SIEM integration, even if DB save fails
            LogToStructuredLogging(auditLog);
        }
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
            ResourceName = resourceName,
            OrganizationId = organizationId,
            Outcome = outcome,
            FailureReason = failureReason,
            Details = details
        };

        return LogAsync(entry, ct);
    }

    public async Task<PagedResponse<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var dbQuery = _dbContext.AuditLogs.AsNoTracking();

        // Apply filters
        if (query.OrganizationId.HasValue)
        {
            dbQuery = dbQuery.Where(a => a.OrganizationId == query.OrganizationId.Value);
        }

        if (query.StartDate.HasValue)
        {
            var startUtc = query.StartDate.Value.UtcDateTime;
            dbQuery = dbQuery.Where(a => a.Timestamp >= startUtc);
        }

        if (query.EndDate.HasValue)
        {
            // If EndDate has a non-midnight time component, use it verbatim.
            // If it's midnight (00:00:00), normalize to end of that day while preserving the offset.
            var endDate = query.EndDate.Value;
            DateTime effectiveEnd;
            if (endDate.TimeOfDay == TimeSpan.Zero)
            {
                // Build end-of-day DateTimeOffset preserving the original offset
                var endOfDay = new DateTimeOffset(endDate.Date.AddDays(1).AddTicks(-1), endDate.Offset);
                effectiveEnd = endOfDay.UtcDateTime;
            }
            else
            {
                effectiveEnd = endDate.UtcDateTime;
            }
            dbQuery = dbQuery.Where(a => a.Timestamp <= effectiveEnd);
        }

        if (!string.IsNullOrEmpty(query.ActorId))
        {
            dbQuery = dbQuery.Where(a => a.ActorId == query.ActorId);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            if (query.Action.EndsWith(".*"))
            {
                // Wildcard support for action prefixes (e.g., "node.*")
                var prefix = query.Action[..^2];
                dbQuery = dbQuery.Where(a => a.Action.StartsWith(prefix));
            }
            else
            {
                dbQuery = dbQuery.Where(a => a.Action == query.Action);
            }
        }

        if (!string.IsNullOrEmpty(query.ResourceType))
        {
            dbQuery = dbQuery.Where(a => a.ResourceType == query.ResourceType);
        }

        if (query.ResourceId.HasValue)
        {
            dbQuery = dbQuery.Where(a => a.ResourceId == query.ResourceId.Value);
        }

        if (query.Outcome.HasValue)
        {
            dbQuery = dbQuery.Where(a => a.Outcome == query.Outcome.Value);
        }

        if (!string.IsNullOrEmpty(query.CorrelationId))
        {
            dbQuery = dbQuery.Where(a => a.CorrelationId == query.CorrelationId);
        }

        // Get total count
        var total = await dbQuery.CountAsync(ct);

        // Apply pagination (always order by timestamp descending for audit logs)
        var page = Math.Max(1, query.Page);
        var limit = query.EffectiveLimit;
        var skip = (page - 1) * limit;

        var items = await dbQuery
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(limit)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                ActorId = a.ActorId,
                ActorType = a.ActorType.ToString(),
                Action = a.Action,
                ResourceType = a.ResourceType,
                ResourceId = a.ResourceId,
                ResourceName = a.ResourceName,
                OrganizationId = a.OrganizationId,
                Outcome = a.Outcome.ToString(),
                FailureReason = a.FailureReason,
                Details = a.Details,
                CorrelationId = a.CorrelationId,
                RequestId = a.RequestId,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent
            })
            .ToListAsync(ct);

        return new PagedResponse<AuditLogDto>
        {
            Items = items,
            Page = page,
            Limit = limit,
            Total = total
        };
    }

    /// <summary>
    /// Emit structured log entry for SIEM integration.
    /// Uses consistent field naming for log aggregation systems.
    /// </summary>
    private void LogToStructuredLogging(NodeAuditLog auditLog)
    {
        var logLevel = auditLog.Outcome switch
        {
            AuditOutcome.Success => LogLevel.Information,
            AuditOutcome.Failure => LogLevel.Error,
            AuditOutcome.Denied => LogLevel.Warning,
            _ => LogLevel.Information
        };

        // Use specific prefixes for SIEM parsing
        var prefix = auditLog.Outcome switch
        {
            AuditOutcome.Denied => "AUDIT:NODES:DENIED",
            AuditOutcome.Failure => "AUDIT:NODES:FAILED",
            _ => "AUDIT:NODES"
        };

        // Structured logging with all relevant fields for SIEM
        _logger.Log(
            logLevel,
            "{Prefix} Action={Action} ResourceType={ResourceType} ResourceId={ResourceId} ResourceName={ResourceName} " +
            "ActorId={ActorId} ActorType={ActorType} OrgId={OrganizationId} Outcome={Outcome} " +
            "FailureReason={FailureReason} CorrelationId={CorrelationId} RequestId={RequestId} " +
            "IpAddress={IpAddress}",
            prefix,
            auditLog.Action,
            auditLog.ResourceType,
            auditLog.ResourceId,
            auditLog.ResourceName,
            auditLog.ActorId,
            auditLog.ActorType,
            auditLog.OrganizationId,
            auditLog.Outcome,
            auditLog.FailureReason,
            auditLog.CorrelationId,
            auditLog.RequestId,
            auditLog.IpAddress);
    }
}
