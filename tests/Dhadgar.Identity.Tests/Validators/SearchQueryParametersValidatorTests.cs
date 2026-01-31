using Dhadgar.Identity.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Dhadgar.Identity.Tests.Validators;

public sealed class SearchQueryParametersValidatorTests
{
    private readonly SearchQueryParametersValidator _validator = new();

    #region Valid Parameters

    [Fact]
    public void Validate_AllNullParameters_Succeeds()
    {
        var parameters = new SearchQueryParameters();

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Validate_ValidPage_Succeeds(int page)
    {
        var parameters = new SearchQueryParameters { Page = page };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_ValidPageSize_Succeeds(int pageSize)
    {
        var parameters = new SearchQueryParameters { PageSize = pageSize };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("test query")]
    [InlineData("search term with special chars: @#$%")]
    public void Validate_AnyQuery_Succeeds(string? query)
    {
        var parameters = new SearchQueryParameters { Query = query };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AllValidParameters_Succeeds()
    {
        var parameters = new SearchQueryParameters
        {
            Query = "test",
            Page = 5,
            PageSize = 25
        };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region Invalid Page

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Validate_InvalidPage_ReturnsError(int page)
    {
        var parameters = new SearchQueryParameters { Page = page };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.Page)
            .WithErrorMessage("Page must be greater than or equal to 1.");
    }

    #endregion

    #region Invalid PageSize

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Validate_PageSizeTooSmall_ReturnsError(int pageSize)
    {
        var parameters = new SearchQueryParameters { PageSize = pageSize };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.PageSize)
            .WithErrorMessage("PageSize must be between 1 and 100.");
    }

    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Validate_PageSizeTooLarge_ReturnsError(int pageSize)
    {
        var parameters = new SearchQueryParameters { PageSize = pageSize };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.PageSize)
            .WithErrorMessage("PageSize must be between 1 and 100.");
    }

    #endregion

    #region Combined Invalid Parameters

    [Fact]
    public void Validate_InvalidPageAndPageSize_ReturnsBothErrors()
    {
        var parameters = new SearchQueryParameters
        {
            Page = 0,
            PageSize = 200
        };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.Page);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_InvalidPageWithValidQuery_ReturnsPageError()
    {
        var parameters = new SearchQueryParameters
        {
            Query = "valid search",
            Page = -5
        };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.Page);
        result.ShouldNotHaveValidationErrorFor(x => x.Query);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    #endregion

    #region Boundary Values

    [Fact]
    public void Validate_PageAtLowerBoundary_Succeeds()
    {
        var parameters = new SearchQueryParameters { Page = 1 };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveValidationErrorFor(x => x.Page);
    }

    [Fact]
    public void Validate_PageSizeAtLowerBoundary_Succeeds()
    {
        var parameters = new SearchQueryParameters { PageSize = 1 };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_PageSizeAtUpperBoundary_Succeeds()
    {
        var parameters = new SearchQueryParameters { PageSize = 100 };

        var result = _validator.TestValidate(parameters);

        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_PageSizeJustBelowLowerBoundary_ReturnsError()
    {
        var parameters = new SearchQueryParameters { PageSize = 0 };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_PageSizeJustAboveUpperBoundary_ReturnsError()
    {
        var parameters = new SearchQueryParameters { PageSize = 101 };

        var result = _validator.TestValidate(parameters);

        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    #endregion
}
