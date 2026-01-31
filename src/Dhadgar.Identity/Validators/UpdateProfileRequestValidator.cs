using Dhadgar.Identity.Endpoints;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates UpdateProfileRequest for self-service profile updates.
/// </summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x)
            .Must(r => !string.IsNullOrWhiteSpace(r.DisplayName) || r.PreferredOrganizationId.HasValue)
            .WithMessage("No updates provided.");
    }
}
