using Dhadgar.Identity.Endpoints;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates BulkInviteRequest for bulk member invitations.
/// </summary>
public sealed class BulkInviteRequestValidator : AbstractValidator<BulkInviteRequest>
{
    public BulkInviteRequestValidator()
    {
        RuleFor(x => x.Invites)
            .NotNull()
            .WithMessage("At least one invite is required.")
            .NotEmpty()
            .WithMessage("At least one invite is required.");
    }
}
