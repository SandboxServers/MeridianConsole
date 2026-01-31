using FluentValidation;

namespace Dhadgar.Identity.Validators;

/// <summary>
/// Query parameters for search endpoints with pagination support.
/// </summary>
public sealed record SearchQueryParameters
{
    /// <summary>Search query string (optional)</summary>
    public string? Query { get; init; }

    /// <summary>Page number (1-based, optional - defaults to 1)</summary>
    public int? Page { get; init; }

    /// <summary>Items per page (optional - defaults to 50, max 100)</summary>
    public int? PageSize { get; init; }
}

/// <summary>
/// Validates search query parameters for pagination endpoints.
/// </summary>
public sealed class SearchQueryParametersValidator : AbstractValidator<SearchQueryParameters>
{
    public SearchQueryParametersValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Page.HasValue)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .When(x => x.PageSize.HasValue)
            .WithMessage("PageSize must be between 1 and 100.");
    }
}
