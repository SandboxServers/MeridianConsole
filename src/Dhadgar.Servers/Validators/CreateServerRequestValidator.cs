using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data.Entities;
using FluentValidation;

namespace Dhadgar.Servers.Validators;

public sealed class CreateServerRequestValidator : AbstractValidator<CreateServerRequest>
{
    public CreateServerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Server name is required")
            .MaximumLength(100).WithMessage("Server name must be 100 characters or less")
            .Matches("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$")
            .WithMessage("Server name must be lowercase alphanumeric with optional hyphens (not at start or end)");

        RuleFor(x => x.DisplayName)
            .MaximumLength(200).WithMessage("Display name must be 200 characters or less")
            .When(x => x.DisplayName != null);

        RuleFor(x => x.GameType)
            .NotEmpty().WithMessage("Game type is required")
            .MaximumLength(50).WithMessage("Game type must be 50 characters or less");

        RuleFor(x => x.CpuLimitMillicores)
            .GreaterThan(0).WithMessage("CPU limit must be positive")
            .LessThanOrEqualTo(64000).WithMessage("CPU limit cannot exceed 64 cores (64000 millicores)");

        RuleFor(x => x.MemoryLimitMb)
            .GreaterThan(0).WithMessage("Memory limit must be positive")
            .LessThanOrEqualTo(1024 * 1024).WithMessage("Memory limit cannot exceed 1TB");

        RuleFor(x => x.DiskLimitMb)
            .GreaterThan(0).WithMessage("Disk limit must be positive")
            .LessThanOrEqualTo(10 * 1024 * 1024).WithMessage("Disk limit cannot exceed 10TB");

        RuleFor(x => x.StartupCommand)
            .MaximumLength(2000).WithMessage("Startup command must be 2000 characters or less")
            .When(x => x.StartupCommand != null);

        RuleForEach(x => x.Ports)
            .SetValidator(new CreateServerPortRequestValidator())
            .When(x => x.Ports != null);

        RuleForEach(x => x.Tags)
            .MaximumLength(50).WithMessage("Each tag must be 50 characters or less")
            .When(x => x.Tags != null);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum of 20 tags allowed");
    }
}

public sealed class CreateServerPortRequestValidator : AbstractValidator<CreateServerPortRequest>
{
    public CreateServerPortRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Port name is required")
            .MaximumLength(50).WithMessage("Port name must be 50 characters or less");

        RuleFor(x => x.Protocol)
            .NotEmpty().WithMessage("Protocol is required")
            .Must(p => p.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
                       p.Equals("udp", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Protocol must be 'tcp' or 'udp'");

        RuleFor(x => x.InternalPort)
            .InclusiveBetween(ServerPort.MinPort, ServerPort.MaxPort)
            .WithMessage($"Internal port must be between {ServerPort.MinPort} and {ServerPort.MaxPort}");

        RuleFor(x => x.ExternalPort)
            .InclusiveBetween(ServerPort.MinPort, ServerPort.MaxPort)
            .WithMessage($"External port must be between {ServerPort.MinPort} and {ServerPort.MaxPort}")
            .When(x => x.ExternalPort.HasValue);
    }
}
