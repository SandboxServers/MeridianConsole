using FluentValidation;
using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Validators;

public sealed class MemberRoleRequestValidator : AbstractValidator<MemberRoleRequest>
{
    public MemberRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessage("Role is required.");
    }
}
