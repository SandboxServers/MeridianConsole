using Dhadgar.Contracts;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Endpoints;

public static class AuditEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/audit-logs")
            .WithTags("Audit")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", QueryAuditLogs)
            .WithName("QueryAuditLogs")
            .WithDescription("Query audit logs for an organization with filtering and pagination")
            .Produces<PagedResponse<AuditLogDto>>();
    }

    private static async Task<IResult> QueryAuditLogs(
        Guid organizationId,
        IAuditService auditService,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? actorId = null,
        string? action = null,
        string? resourceType = null,
        Guid? resourceId = null,
        string? outcome = null,
        string? correlationId = null,
        int page = 1,
        int limit = 50,
        CancellationToken ct = default)
    {
        // Parse outcome if provided
        AuditOutcome? parsedOutcome = null;
        if (!string.IsNullOrEmpty(outcome))
        {
            if (Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out var parsed))
            {
                parsedOutcome = parsed;
            }
            else
            {
                return ProblemDetailsHelper.BadRequest(
                    "invalid_outcome",
                    "Outcome must be one of: Success, Failure, Denied");
            }
        }

        var query = new AuditQuery
        {
            OrganizationId = organizationId,
            StartDate = startDate,
            EndDate = endDate,
            ActorId = actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = parsedOutcome,
            CorrelationId = correlationId,
            Page = page,
            Limit = limit
        };

        var result = await auditService.QueryAsync(query, ct);
        return Results.Ok(result);
    }
}
