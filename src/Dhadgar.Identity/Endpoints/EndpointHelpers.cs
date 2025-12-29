using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class EndpointHelpers
{
    public static bool TryGetUserId(HttpContext context, out Guid userId)
    {
        if (context.Request.Headers.TryGetValue("X-User-Id", out var header)
            && Guid.TryParse(header.ToString(), out userId))
        {
            return true;
        }

        var claim = context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out userId);
    }

    public static async Task<IResult?> RequirePermissionAsync(
        Guid userId,
        Guid organizationId,
        string permission,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var permissions = await permissionService.CalculatePermissionsAsync(userId, organizationId, ct);
        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
            ? null
            : Results.Forbid();
    }
}
