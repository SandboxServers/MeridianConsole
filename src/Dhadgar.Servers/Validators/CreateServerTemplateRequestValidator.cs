using Dhadgar.Contracts.Servers;
using FluentValidation;

namespace Dhadgar.Servers.Validators;

public sealed class CreateServerTemplateRequestValidator : AbstractValidator<CreateServerTemplateRequest>
{
    public CreateServerTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Template name is required")
            .MaximumLength(100).WithMessage("Template name must be 100 characters or less");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must be 1000 characters or less")
            .When(x => x.Description != null);

        RuleFor(x => x.GameType)
            .NotEmpty().WithMessage("Game type is required")
            .MaximumLength(50).WithMessage("Game type must be 50 characters or less");

        RuleFor(x => x.DefaultCpuLimitMillicores)
            .GreaterThan(0).WithMessage("Default CPU limit must be positive")
            .LessThanOrEqualTo(64000).WithMessage("Default CPU limit cannot exceed 64 cores");

        RuleFor(x => x.DefaultMemoryLimitMb)
            .GreaterThan(0).WithMessage("Default memory limit must be positive")
            .LessThanOrEqualTo(1024 * 1024).WithMessage("Default memory limit cannot exceed 1TB");

        RuleFor(x => x.DefaultDiskLimitMb)
            .GreaterThan(0).WithMessage("Default disk limit must be positive")
            .LessThanOrEqualTo(10 * 1024 * 1024).WithMessage("Default disk limit cannot exceed 10TB");

        RuleFor(x => x.DefaultStartupCommand)
            .MaximumLength(2000).WithMessage("Default startup command must be 2000 characters or less")
            .When(x => x.DefaultStartupCommand != null);

        RuleFor(x => x.DefaultJavaFlags)
            .MaximumLength(500).WithMessage("Default Java flags must be 500 characters or less")
            .When(x => x.DefaultJavaFlags != null);

        RuleForEach(x => x.DefaultPorts)
            .SetValidator(new CreateServerPortRequestValidator())
            .When(x => x.DefaultPorts != null);
    }
}
