using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// Endpoints for viewing activity/audit logs.
/// </summary>
public static class ActivityEndpoints
{
    public static void Map(WebApplication app)
    {
        // User's own activity log
        app.MapGet("/me/activity", GetMyActivity)
            .WithName("GetMyActivity")
            .WithTags("Activity")
            .WithDescription("Get activity log for the current user")
            .RequireAuthorization();

        // Organization activity log (requires permission)
        app.MapGet("/organizations/{organizationId:guid}/activity", GetOrgActivity)
            .WithName("GetOrgActivity")
            .WithTags("Activity")
            .WithDescription("Get activity log for an organization")
            .RequireAuthorization();
    }

    private static async Task<IResult> GetMyActivity(
        HttpContext context,
        IAuditService auditService,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var actualTake = Math.Clamp(take ?? 50, 1, 100);
        var actualSkip = Math.Max(skip ?? 0, 0);

        var events = await auditService.GetUserActivityAsync(
            userId,
            skip: actualSkip,
            take: actualTake,
            ct: ct);

        return Results.Ok(new
        {
            events,
            pagination = new
            {
                take = actualTake,
                skip = actualSkip,
                count = events.Count
            }
        });
    }

    private static async Task<IResult> GetOrgActivity(
        HttpContext context,
        Guid organizationId,
        IAuditService auditService,
        IPermissionService permissionService,
        int? take,
        int? skip,
        string? eventType,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            userId,
            organizationId,
            "org:audit",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var actualTake = Math.Clamp(take ?? 50, 1, 100);
        var actualSkip = Math.Max(skip ?? 0, 0);

        var events = await auditService.GetOrganizationActivityAsync(
            organizationId,
            eventType: eventType,
            skip: actualSkip,
            take: actualTake,
            ct: ct);

        return Results.Ok(new
        {
            events,
            pagination = new
            {
                take = actualTake,
                skip = actualSkip,
                count = events.Count
            }
        });
    }
}
