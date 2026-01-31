using Dhadgar.Identity.Services;
using Dhadgar.ServiceDefaults.Security;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// Endpoints for session (refresh token) management.
/// Provides self-service session management for authenticated users.
/// </summary>
public static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/me/sessions")
            .WithTags("Sessions")
            .RequireAuthorization();

        group.MapGet("", GetSessions)
            .WithName("GetSessions")
            .WithDescription("List all active sessions for the current user");

        group.MapDelete("/{sessionId:guid}", RevokeSession)
            .WithName("RevokeSession")
            .WithDescription("Revoke a specific session");

        group.MapPost("/revoke-all", RevokeAllSessions)
            .WithName("RevokeAllSessions")
            .WithDescription("Revoke all sessions (logout from all devices)");

        // Logout endpoint (revokes current session)
        app.MapPost("/logout", Logout)
            .WithTags("Authentication")
            .WithName("Logout")
            .WithDescription("Logout from current session")
            .RequireAuthorization();
    }

    private static async Task<IResult> GetSessions(
        HttpContext context,
        IRefreshTokenService refreshTokenService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var sessions = await refreshTokenService.GetUserSessionsAsync(userId, ct);

        return Results.Ok(new
        {
            sessions = sessions.Select(s => new
            {
                s.Id,
                s.OrganizationId,
                s.IssuedAt,
                s.ExpiresAt,
                s.DeviceInfo
            })
        });
    }

    private static async Task<IResult> RevokeSession(
        HttpContext context,
        Guid sessionId,
        IRefreshTokenService refreshTokenService,
        ISecurityEventLogger securityLogger,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var revoked = await refreshTokenService.RevokeSessionAsync(userId, sessionId, ct);

        if (!revoked)
        {
            return ProblemDetailsHelper.NotFound(ErrorCodes.Auth.SessionExpired, "Session not found.");
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        securityLogger.LogTokenRevocation(userId, "session_revoked", clientIp);

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessions(
        HttpContext context,
        IRefreshTokenService refreshTokenService,
        ISecurityEventLogger securityLogger,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var count = await refreshTokenService.RevokeAllUserTokensAsync(userId, ct);

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        securityLogger.LogTokenRevocation(userId, $"all_sessions_revoked:{count}", clientIp);

        return Results.Ok(new { revokedCount = count });
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        IRefreshTokenService refreshTokenService,
        ISecurityEventLogger securityLogger,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        // Get the organization from the current token to revoke only that org's session
        var orgId = EndpointHelpers.GetOrganizationId(context);

        int count;
        if (orgId.HasValue)
        {
            // Revoke tokens for current organization only
            count = await refreshTokenService.RevokeUserOrgTokensAsync(userId, orgId.Value, ct);
        }
        else
        {
            // No org context - revoke all tokens
            count = await refreshTokenService.RevokeAllUserTokensAsync(userId, ct);
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        securityLogger.LogTokenRevocation(userId, "logout", clientIp);

        return Results.Ok(new { message = "logged_out", sessionsRevoked = count });
    }
}
