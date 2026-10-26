using FluentValidation;
using static Dhadgar.Identity.Endpoints.TokenRefreshEndpoint;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates TokenRefreshRequest for refresh-token redemption.
/// </summary>
public sealed class TokenRefreshRequestValidator : AbstractValidator<TokenRefreshRequest>
{
    public TokenRefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token is required.")
            .MaximumLength(512)
            .WithMessage("Refresh token is too long.");
    }
}
