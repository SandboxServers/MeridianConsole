using Dhadgar.Console.Validators;
using Dhadgar.Contracts.Console;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Console.Tests.Validators;

public class ExecuteCommandRequestValidatorTests
{
    private readonly ExecuteCommandRequestValidator _validator = new();

    [Fact]
    public async Task Validate_ValidRequest_PassesValidation()
    {
        var request = new ExecuteCommandRequest(Guid.NewGuid(), "say hello");

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyServerId_FailsValidation()
    {
        var request = new ExecuteCommandRequest(Guid.Empty, "say hello");

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ServerId");
    }

    [Fact]
    public async Task Validate_EmptyCommand_FailsValidation()
    {
        var request = new ExecuteCommandRequest(Guid.NewGuid(), string.Empty);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Command");
    }

    [Fact]
    public async Task Validate_CommandTooLong_FailsValidation()
    {
        var longCommand = new string('a', 2001);
        var request = new ExecuteCommandRequest(Guid.NewGuid(), longCommand);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Command");
    }
}

public class SearchConsoleHistoryRequestValidatorTests
{
    private readonly SearchConsoleHistoryRequestValidator _validator = new();

    [Fact]
    public async Task Validate_ValidRequest_PassesValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid());

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyServerId_FailsValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.Empty);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ServerId");
    }

    [Fact]
    public async Task Validate_PageZero_FailsValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid(), Page: 0);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Validate_PageOne_PassesValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid(), Page: 1);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PageSizeZero_FailsValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid(), PageSize: 0);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_PageSize101_FailsValidation()
    {
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid(), PageSize: 101);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Validate_EndTimeBeforeStartTime_FailsValidation()
    {
        var request = new SearchConsoleHistoryRequest(
            Guid.NewGuid(),
            StartTime: new DateTime(2026, 2, 14, 12, 0, 0, DateTimeKind.Utc),
            EndTime: new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc));

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "EndTime");
    }

    [Fact]
    public async Task Validate_ValidDateRange_PassesValidation()
    {
        var request = new SearchConsoleHistoryRequest(
            Guid.NewGuid(),
            StartTime: new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc),
            EndTime: new DateTime(2026, 2, 14, 12, 0, 0, DateTimeKind.Utc));

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_NullDates_PassesValidation()
    {
        var request = new SearchConsoleHistoryRequest(
            Guid.NewGuid(),
            StartTime: null,
            EndTime: null);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_QueryTooLong_FailsValidation()
    {
        var longQuery = new string('a', 501);
        var request = new SearchConsoleHistoryRequest(Guid.NewGuid(), Query: longQuery);

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Query");
    }
}
