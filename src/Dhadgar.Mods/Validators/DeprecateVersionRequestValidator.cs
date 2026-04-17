using Dhadgar.Contracts.Mods;
using FluentValidation;

namespace Dhadgar.Mods.Validators;

public sealed class DeprecateVersionRequestValidator : AbstractValidator<DeprecateVersionRequest>
{
    public DeprecateVersionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Deprecation reason is required")
            .MaximumLength(1000).WithMessage("Deprecation reason must be 1000 characters or less");
    }
}
