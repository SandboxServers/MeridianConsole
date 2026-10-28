using Dhadgar.Contracts.Servers;
using FluentValidation;

namespace Dhadgar.Servers.Validators;

public sealed class UpdateServerRequestValidator : AbstractValidator<UpdateServerRequest>
{
    public UpdateServerRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Server name must be 100 characters or less")
            .Matches("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$")
            .WithMessage("Server name must be lowercase alphanumeric with optional hyphens (not at start or end)")
            .When(x => x.Name != null);

        RuleFor(x => x.DisplayName)
            .MaximumLength(200).WithMessage("Display name must be 200 characters or less")
            .When(x => x.DisplayName != null);

        RuleForEach(x => x.Tags)
            .MaximumLength(50).WithMessage("Each tag must be 50 characters or less")
            .When(x => x.Tags != null);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum of 20 tags allowed");
    }
}
