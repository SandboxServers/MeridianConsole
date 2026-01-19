using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// MFA (Multi-Factor Authentication) policy endpoints.
/// Currently scaffolded - returns 501 Not Implemented.
/// </summary>
public static class MfaPolicyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/security/mfa")
            .WithTags("MFA Policy")
            .RequireAuthorization();

        group.MapGet("", GetMfaPolicy)
            .WithName("GetMfaPolicy")
            .WithDescription("Get MFA policy settings for an organization");

        group.MapPut("", UpdateMfaPolicy)
            .WithName("UpdateMfaPolicy")
            .WithDescription("Update MFA policy settings for an organization");
    }

    private static async Task<IResult> GetMfaPolicy(
        HttpContext context,
        Guid organizationId,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            userId,
            organizationId,
            "org:security",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        // TODO: Implement MFA policy retrieval
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    private static async Task<IResult> UpdateMfaPolicy(
        HttpContext context,
        Guid organizationId,
        MfaPolicyRequest request,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            userId,
            organizationId,
            "org:security",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        // TODO: Implement MFA policy update
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }
}

/// <summary>
/// Request model for MFA policy updates.
/// Placeholder - will be expanded when MFA is implemented.
/// </summary>
public sealed record MfaPolicyRequest(
    bool? RequireMfa,
    IReadOnlyCollection<string>? AllowedMethods,
    int? GracePeriodDays);
