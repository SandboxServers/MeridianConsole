using Dhadgar.Identity.Services;
using Dhadgar.ServiceDefaults.Security;
using FluentValidation;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// <c>POST /refresh</c> — redeems a refresh token issued by <c>/exchange</c> for a new
/// access/refresh pair. Contract matches what <c>Dhadgar.SharedAuth</c> already calls:
/// request <c>{ "refreshToken": "..." }</c>, response
/// <c>{ "accessToken", "refreshToken", "expiresIn" }</c>.
/// </summary>
public static class TokenRefreshEndpoint
{
    public sealed record TokenRefreshRequest(string RefreshToken);

    public static async Task<IResult> Handle(
        HttpContext context,
        TokenRefreshRequest request,
        TokenRefreshService service,
        ISecurityEventLogger securityLogger,
        IValidator<TokenRefreshRequest> validator,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                "Refresh token is required.");
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var outcome = await service.RefreshAsync(request.RefreshToken, ct);

        if (!outcome.Success)
        {
            securityLogger.LogAuthenticationFailure(
                outcome.UserId?.ToString(),
                $"token_refresh_failed:{outcome.Error}",
                clientIp,
                context.Request.Headers.UserAgent);

            // SECURITY: one generic response for every redemption failure so callers
            // can't distinguish unknown, expired, reused or deactivated tokens.
            return outcome.Error == "email_not_verified"
                ? ProblemDetailsHelper.Forbidden(
                    ErrorCodes.AuthErrors.AccessDenied,
                    "Email address must be verified.")
                : ProblemDetailsHelper.Unauthorized(
                    ErrorCodes.AuthErrors.SessionExpired,
                    "Invalid or expired refresh token.");
        }

        return Results.Ok(new
        {
            accessToken = outcome.AccessToken,
            refreshToken = outcome.RefreshToken,
            expiresIn = outcome.ExpiresIn
        });
    }
}
