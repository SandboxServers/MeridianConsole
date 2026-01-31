using Dhadgar.Identity.Services;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates UserUpdateRequest for user updates.
/// </summary>
public sealed class UserUpdateRequestValidator : AbstractValidator<UserUpdateRequest>
{
    public UserUpdateRequestValidator()
    {
        RuleFor(x => x)
            .Must(r => !string.IsNullOrWhiteSpace(r.Email) || !string.IsNullOrWhiteSpace(r.DisplayName))
            .WithMessage("No updates provided.");
    }
}
