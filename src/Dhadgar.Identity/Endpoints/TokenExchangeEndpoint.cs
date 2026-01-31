using Dhadgar.Identity.Services;
using Dhadgar.ServiceDefaults.Security;

namespace Dhadgar.Identity.Endpoints;

public static class TokenExchangeEndpoint
{
    public sealed record TokenExchangeRequest(string ExchangeToken);

    // Error codes that should be exposed vs hidden
    // SECURITY: These are the only error codes returned to clients
    // All other errors are mapped to generic "exchange_failed" to prevent information leakage
    private static readonly HashSet<string> SafeErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "missing_exchange_token",
        "token_already_used",  // Safe: doesn't reveal user existence
        "email_not_verified"   // Safe: only after successful token validation
    };

    public static async Task<IResult> Handle(
        HttpContext context,
        TokenExchangeRequest request,
        TokenExchangeService service,
        ISecurityEventLogger securityLogger,
        CancellationToken ct)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(request.ExchangeToken))
        {
            return ProblemDetailsHelper.BadRequest(ErrorCodes.Common.ValidationFailed, "Exchange token is required.");
        }

        var outcome = await service.ExchangeAsync(request.ExchangeToken, ct);

        if (!outcome.Success)
        {
            // SECURITY: Log failed exchange attempts for monitoring
            securityLogger.LogAuthenticationFailure(
                outcome.Email,
                $"token_exchange_failed:{outcome.Error}",
                clientIp,
                context.Request.Headers.UserAgent);

            // SECURITY: Return generic errors for sensitive failures
            // to prevent information leakage about user existence
            var safeError = SafeErrorCodes.Contains(outcome.Error ?? "")
                ? outcome.Error
                : "exchange_failed";

            return outcome.Error switch
            {
                // Consolidated: all token validation failures return same generic message
                "invalid_exchange_token" or "invalid_purpose" or "missing_jti" or "missing_claims" =>
                    ProblemDetailsHelper.Unauthorized(ErrorCodes.Auth.TokenExpired, "Invalid or expired exchange token."),
                "email_not_verified" => ProblemDetailsHelper.Forbidden(ErrorCodes.Auth.AccessDenied, "Email address must be verified before exchanging tokens."),
                _ => ProblemDetailsHelper.BadRequest(ErrorCodes.Common.ValidationFailed, safeError == "exchange_failed" ? "Token exchange failed." : safeError)
            };
        }

        // Log successful authentication
        if (outcome.UserId.HasValue)
        {
            securityLogger.LogAuthenticationSuccess(
                outcome.UserId.Value,
                outcome.Email,
                clientIp,
                context.Request.Headers.UserAgent,
                outcome.OrganizationId?.ToString());
        }

        return Results.Ok(new
        {
            accessToken = outcome.AccessToken,
            refreshToken = outcome.RefreshToken,
            expiresIn = outcome.ExpiresIn,
            userId = outcome.UserId,
            organizationId = outcome.OrganizationId
        });
    }
}
