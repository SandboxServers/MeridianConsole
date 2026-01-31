using FluentValidation;
using static Dhadgar.Identity.Endpoints.TokenExchangeEndpoint;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates TokenExchangeRequest for Better Auth token exchange.
/// </summary>
public sealed class TokenExchangeRequestValidator : AbstractValidator<TokenExchangeRequest>
{
    public TokenExchangeRequestValidator()
    {
        RuleFor(x => x.ExchangeToken)
            .NotEmpty()
            .WithMessage("Exchange token is required.");
    }
}
