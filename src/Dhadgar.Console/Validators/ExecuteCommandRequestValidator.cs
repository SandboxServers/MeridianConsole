using Dhadgar.Contracts.Console;
using FluentValidation;

namespace Dhadgar.Console.Validators;

public sealed class ExecuteCommandRequestValidator : AbstractValidator<ExecuteCommandRequest>
{
    public ExecuteCommandRequestValidator()
    {
        RuleFor(x => x.ServerId)
            .NotEmpty().WithMessage("Server ID is required");

        RuleFor(x => x.Command)
            .NotEmpty().WithMessage("Command is required")
            .MaximumLength(2000).WithMessage("Command must be 2000 characters or less");
    }
}
