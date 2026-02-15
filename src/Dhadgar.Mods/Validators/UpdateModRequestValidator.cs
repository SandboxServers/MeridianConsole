using Dhadgar.Contracts.Mods;
using FluentValidation;

namespace Dhadgar.Mods.Validators;

public sealed class UpdateModRequestValidator : AbstractValidator<UpdateModRequest>
{
    public UpdateModRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Mod name must be 100 characters or less")
            .When(x => x.Name != null);

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage("Description must be 4000 characters or less")
            .When(x => x.Description != null);

        RuleFor(x => x.Author)
            .MaximumLength(200).WithMessage("Author must be 200 characters or less")
            .When(x => x.Author != null);

        RuleFor(x => x.ProjectUrl)
            .MaximumLength(500).WithMessage("Project URL must be 500 characters or less")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .WithMessage("Project URL must be a valid HTTP or HTTPS URL")
            .When(x => x.ProjectUrl != null);

        RuleFor(x => x.IconUrl)
            .MaximumLength(500).WithMessage("Icon URL must be 500 characters or less")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .WithMessage("Icon URL must be a valid HTTP or HTTPS URL")
            .When(x => x.IconUrl != null);

        RuleForEach(x => x.Tags)
            .MaximumLength(50).WithMessage("Each tag must be 50 characters or less")
            .When(x => x.Tags != null);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum of 20 tags allowed");
    }
}
