using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class TokenExchangeEndpoint
{
    public sealed record TokenExchangeRequest(string ExchangeToken);

    public static async Task<IResult> Handle(
        TokenExchangeRequest request,
        TokenExchangeService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExchangeToken))
        {
            return Results.BadRequest(new { error = "missing_exchange_token" });
        }

        var outcome = await service.ExchangeAsync(request.ExchangeToken, ct);

        if (!outcome.Success)
        {
            return outcome.Error switch
            {
                "invalid_exchange_token" => Results.Unauthorized(),
                "invalid_purpose" => Results.Unauthorized(),
                "token_already_used" => Results.BadRequest(new { error = outcome.Error }),
                "missing_jti" => Results.BadRequest(new { error = outcome.Error }),
                "missing_claims" => Results.BadRequest(new { error = outcome.Error }),
                _ => Results.BadRequest(new { error = "exchange_failed" })
            };
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
