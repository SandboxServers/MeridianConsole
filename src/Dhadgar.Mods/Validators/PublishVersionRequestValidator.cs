using Dhadgar.Contracts.Mods;
using FluentValidation;

namespace Dhadgar.Mods.Validators;

public sealed class PublishVersionRequestValidator : AbstractValidator<PublishVersionRequest>
{
    public PublishVersionRequestValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("Version is required")
            .MaximumLength(50).WithMessage("Version must be 50 characters or less")
            .Matches(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?(\+[a-zA-Z0-9.]+)?$")
            .WithMessage("Version must be valid semantic version (e.g., 1.0.0, 1.0.0-beta.1)");

        RuleFor(x => x.ReleaseNotes)
            .MaximumLength(10000).WithMessage("Release notes must be 10000 characters or less")
            .When(x => x.ReleaseNotes != null);

        RuleFor(x => x.FileHash)
            .MaximumLength(128).WithMessage("File hash must be 128 characters or less")
            .When(x => x.FileHash != null);

        RuleFor(x => x.FileSizeBytes)
            .GreaterThanOrEqualTo(0).WithMessage("File size must be non-negative");

        RuleFor(x => x.MinGameVersion)
            .MaximumLength(50).WithMessage("Min game version must be 50 characters or less")
            .When(x => x.MinGameVersion != null);

        RuleFor(x => x.MaxGameVersion)
            .MaximumLength(50).WithMessage("Max game version must be 50 characters or less")
            .When(x => x.MaxGameVersion != null);
    }
}
