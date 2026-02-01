using Dhadgar.Identity.Endpoints;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates BulkRemoveRequest for bulk member removals.
/// </summary>
public sealed class BulkRemoveRequestValidator : AbstractValidator<BulkRemoveRequest>
{
    public BulkRemoveRequestValidator()
    {
        RuleFor(x => x.MemberIds)
            .NotNull()
            .WithMessage("At least one member ID is required.")
            .NotEmpty()
            .WithMessage("At least one member ID is required.");
    }
}
