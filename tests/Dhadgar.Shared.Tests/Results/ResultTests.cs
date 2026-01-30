using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Shared.Tests.Results;

/// <summary>
/// Tests for the Result and Result{T} types.
/// </summary>
public sealed class ResultTests
{
    #region Result (non-generic) Tests

    [Fact]
    public void Result_Success_ReturnsSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void Result_Failure_ReturnsFailedResult()
    {
        var result = Result.Failure("Something went wrong");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public void Result_Match_ExecutesOnSuccessForSuccessfulResult()
    {
        var result = Result.Success();

        var output = result.Match(
            onSuccess: () => "success",
            onFailure: error => $"failure: {error}");

        Assert.Equal("success", output);
    }

    [Fact]
    public void Result_Match_ExecutesOnFailureForFailedResult()
    {
        var result = Result.Failure("error message");

        var output = result.Match(
            onSuccess: () => "success",
            onFailure: error => $"failure: {error}");

        Assert.Equal("failure: error message", output);
    }

    #endregion

    #region Result<T> Tests

    [Fact]
    public void ResultT_Success_ReturnsSuccessfulResultWithValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void ResultT_Failure_ReturnsFailedResult()
    {
        var result = Result<int>.Failure("Something went wrong");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public void ResultT_Value_ThrowsOnFailure()
    {
        var result = Result<int>.Failure("error");

        var exception = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Contains("error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultT_Match_ExecutesOnSuccessForSuccessfulResult()
    {
        var result = Result<int>.Success(42);

        var output = result.Match(
            onSuccess: value => $"value is {value}",
            onFailure: error => $"failure: {error}");

        Assert.Equal("value is 42", output);
    }

    [Fact]
    public void ResultT_Match_ExecutesOnFailureForFailedResult()
    {
        var result = Result<int>.Failure("error message");

        var output = result.Match(
            onSuccess: value => $"value is {value}",
            onFailure: error => $"failure: {error}");

        Assert.Equal("failure: error message", output);
    }

    [Fact]
    public void ResultT_TryGetValue_ReturnsTrueAndValueForSuccess()
    {
        var result = Result<string>.Success("hello");

        var success = result.TryGetValue(out var value);

        Assert.True(success);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void ResultT_TryGetValue_ReturnsFalseAndDefaultForFailure()
    {
        var result = Result<string>.Failure("error");

        var success = result.TryGetValue(out var value);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void ResultT_TryGetValue_ReturnsFalseAndDefaultValueTypeForFailure()
    {
        var result = Result<int>.Failure("error");

        var success = result.TryGetValue(out var value);

        Assert.False(success);
        Assert.Equal(0, value);
    }

    [Fact]
    public void ResultT_ValueOr_ReturnsValueOnSuccess()
    {
        var result = Result<int>.Success(42);

        Assert.Equal(42, result.ValueOr(0));
    }

    [Fact]
    public void ResultT_ValueOr_ReturnsDefaultOnFailure()
    {
        var result = Result<int>.Failure("error");

        Assert.Equal(99, result.ValueOr(99));
    }

    [Fact]
    public void ResultT_Map_TransformsValueOnSuccess()
    {
        var result = Result<int>.Success(10);

        var mapped = result.Map(x => x * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(20, mapped.Value);
    }

    [Fact]
    public void ResultT_Map_PropagatesErrorOnFailure()
    {
        var result = Result<int>.Failure("original error");

        var mapped = result.Map(x => x * 2);

        Assert.True(mapped.IsFailure);
        Assert.Equal("original error", mapped.Error);
    }

    [Fact]
    public void ResultT_Bind_ChainsResultsOnSuccess()
    {
        var result = Result<int>.Success(10);

        var bound = result.Bind(x => Result<string>.Success($"value: {x}"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 10", bound.Value);
    }

    [Fact]
    public void ResultT_Bind_PropagatesErrorOnFailure()
    {
        var result = Result<int>.Failure("original error");

        var bound = result.Bind(x => Result<string>.Success($"value: {x}"));

        Assert.True(bound.IsFailure);
        Assert.Equal("original error", bound.Error);
    }

    [Fact]
    public void ResultT_ImplicitConversion_CreatesSuccessFromValue()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ResultT_FromValue_CreatesSuccessResult()
    {
        var result = Result<int>.FromValue(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    #endregion
}
