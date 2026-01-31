using Dhadgar.Identity.Endpoints;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates ClientAssertionRequest for Microsoft federated credential assertions.
/// </summary>
public sealed class ClientAssertionRequestValidator : AbstractValidator<ClientAssertionRequest>
{
    public ClientAssertionRequestValidator()
    {
        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("Subject is required.")
            .Must(subject => !string.IsNullOrWhiteSpace(subject))
            .WithMessage("Subject cannot be whitespace only.");
    }
}
