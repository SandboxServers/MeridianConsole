using Dhadgar.Contracts.Mods;
using FluentValidation;

namespace Dhadgar.Mods.Validators;

public sealed class CreateModRequestValidator : AbstractValidator<CreateModRequest>
{
    public CreateModRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Mod name is required")
            .MaximumLength(100).WithMessage("Mod name must be 100 characters or less");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("Slug is required")
            .MaximumLength(100).WithMessage("Slug must be 100 characters or less")
            .Matches("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$")
            .WithMessage("Slug must be lowercase alphanumeric with optional hyphens (not at start or end)");

        RuleFor(x => x.GameType)
            .NotEmpty().WithMessage("Game type is required")
            .MaximumLength(50).WithMessage("Game type must be 50 characters or less");

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
            .NotEmpty().WithMessage("Tags must not be empty or whitespace")
            .MaximumLength(50).WithMessage("Each tag must be 50 characters or less")
            .When(x => x.Tags != null);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum of 20 tags allowed");
    }
}
