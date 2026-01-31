using Dhadgar.Identity.Services;
using Dhadgar.ServiceDefaults.Security;
using FluentValidation;

namespace Dhadgar.Identity.Endpoints;

public static class TokenExchangeEndpoint
{
    public sealed record TokenExchangeRequest(string ExchangeToken);

    public static async Task<IResult> Handle(
        HttpContext context,
        TokenExchangeRequest request,
        TokenExchangeService service,
        ISecurityEventLogger securityLogger,
        IValidator<TokenExchangeRequest> validator,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            // Return specific error code for ExchangeToken validation failures
            var exchangeTokenError = validationResult.Errors.FirstOrDefault(e =>
                e.PropertyName == nameof(TokenExchangeRequest.ExchangeToken));

            if (exchangeTokenError is not null)
            {
                return ProblemDetailsHelper.BadRequest(
                    ErrorCodes.AuthErrors.MissingExchangeToken,
                    exchangeTokenError.ErrorMessage);
            }

            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                validationResult.Errors[0].ErrorMessage);
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString();

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
            return outcome.Error switch
            {
                // Consolidated: all token validation failures return same generic message
                "invalid_exchange_token" or "invalid_purpose" or "missing_jti" or "missing_claims" =>
                    ProblemDetailsHelper.Unauthorized(ErrorCodes.AuthErrors.TokenExpired, "Invalid or expired exchange token."),
                "email_not_verified" => ProblemDetailsHelper.Forbidden(ErrorCodes.AuthErrors.AccessDenied, "Email address must be verified before exchanging tokens."),
                // Safe error codes that can be exposed to clients
                "missing_exchange_token" => ProblemDetailsHelper.BadRequest(ErrorCodes.AuthErrors.MissingExchangeToken, "Exchange token is required."),
                "token_already_used" => ProblemDetailsHelper.BadRequest(ErrorCodes.AuthErrors.TokenAlreadyUsed, "This exchange token has already been used."),
                // Generic fallback for all other errors to prevent information leakage
                _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, "Token exchange failed.")
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
