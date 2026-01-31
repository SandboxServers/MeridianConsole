using Dhadgar.Identity.Endpoints;
using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Validates UserBatchRequest for batch user lookups.
/// </summary>
public sealed class UserBatchRequestValidator : AbstractValidator<UserBatchRequest>
{
    private const int MaxBatchSize = 100;

    public UserBatchRequestValidator()
    {
        RuleFor(x => x.UserIds)
            .NotNull()
            .WithMessage("No user IDs provided.")
            .NotEmpty()
            .WithMessage("No user IDs provided.");

        RuleFor(x => x.UserIds)
            .Must(ids => ids is null || ids.Count <= MaxBatchSize)
            .WithMessage($"Too many user IDs provided. Maximum allowed is {MaxBatchSize}.");
    }
}
