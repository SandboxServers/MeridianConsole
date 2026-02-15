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
            .GreaterThan(0).WithMessage("File size must be positive for published versions");

        RuleFor(x => x.FilePath)
            .MaximumLength(500).WithMessage("File path must be 500 characters or less")
            .Must(path => !path!.Contains("..", StringComparison.Ordinal))
            .WithMessage("File path must not contain path traversal sequences")
            .Must(path => !path!.Any(char.IsControl))
            .WithMessage("File path must not contain control characters")
            .When(x => x.FilePath != null);

        RuleFor(x => x.MinGameVersion)
            .MaximumLength(50).WithMessage("Min game version must be 50 characters or less")
            .When(x => x.MinGameVersion != null);

        RuleFor(x => x.MaxGameVersion)
            .MaximumLength(50).WithMessage("Max game version must be 50 characters or less")
            .When(x => x.MaxGameVersion != null);

        RuleFor(x => x.Dependencies)
            .Must(deps => deps!.Count <= 100)
            .WithMessage("A mod version may declare at most 100 dependencies")
            .When(x => x.Dependencies != null);

        RuleForEach(x => x.Dependencies)
            .ChildRules(child =>
            {
                child.RuleFor(d => d.ModId)
                    .NotEqual(Guid.Empty)
                    .WithMessage("Dependency ModId must not be empty");
            })
            .When(x => x.Dependencies != null);
    }
}
