using Dhadgar.Contracts.Console;
using FluentValidation;

namespace Dhadgar.Console.Validators;

public sealed class SearchConsoleHistoryRequestValidator : AbstractValidator<SearchConsoleHistoryRequest>
{
    public SearchConsoleHistoryRequestValidator()
    {
        RuleFor(x => x.ServerId)
            .NotEmpty().WithMessage("Server ID is required");

        RuleFor(x => x.Query)
            .MaximumLength(500).WithMessage("Search query must be 500 characters or less")
            .When(x => x.Query != null);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100");

        RuleFor(x => x.EndTime)
            .GreaterThan(x => x.StartTime)
            .WithMessage("End time must be after start time")
            .When(x => x.StartTime.HasValue && x.EndTime.HasValue);
    }
}
