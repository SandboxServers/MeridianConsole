using System.Diagnostics.CodeAnalysis;

namespace Dhadgar.Shared.Results;

/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// This is the non-generic version for operations that don't return a value.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1000:DoNotDeclareStaticMembersOnGenericTypes",
    Justification = "Factory methods on Result types are conventional and expected.")]
public readonly record struct Result
{
    private readonly string? _error;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string Error => _error ?? string.Empty;

    private Result(bool isSuccess, string? error)
    {
        if (isSuccess && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("A successful result cannot have an error message.");
        }

        if (!isSuccess && string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("A failed result must have an error message.");
        }

        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new(true, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(string error) => new(false, error);

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>The current result.</returns>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    /// <param name="action">The action to execute with the error message.</param>
    /// <returns>The current result.</returns>
    public Result OnFailure(Action<string> action)
    {
        if (IsFailure)
        {
            action(Error);
        }
        return this;
    }
}

/// <summary>
/// Represents the result of an operation that can succeed with a value or fail.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:DoNotDeclareStaticMembersOnGenericTypes",
    Justification = "Factory methods on Result types are conventional and expected.")]
public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string Error => _error ?? string.Empty;

    /// <summary>
    /// Gets the value if the operation succeeded.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing Value on a failed result.</exception>
    public T Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {Error}");
            }
            return _value!;
        }
    }

    private Result(bool isSuccess, T? value, string? error)
    {
        if (isSuccess && value is null)
        {
            throw new InvalidOperationException("A successful result must have a value.");
        }

        if (isSuccess && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("A successful result cannot have an error message.");
        }

        if (!isSuccess && string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("A failed result must have an error message.");
        }

        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static Result<T> Success(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Failure(string error) => new(false, default, error);

    /// <summary>
    /// Creates a successful result from a value (named alternative for implicit operator).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static Result<T> FromValue(T value) => Success(value);

    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static implicit operator Result<T>(T value) => FromValue(value);

    /// <summary>
    /// Executes a function if the result is successful and returns a new result.
    /// </summary>
    /// <typeparam name="TNext">The type of the next result value.</typeparam>
    /// <param name="func">The function to execute with the current value.</param>
    /// <returns>A new result.</returns>
    public Result<TNext> Map<TNext>(Func<T, TNext> func)
    {
        if (IsFailure)
        {
            return Result<TNext>.Failure(Error);
        }
        return Result<TNext>.Success(func(Value));
    }

    /// <summary>
    /// Executes a function if the result is successful and returns the result directly.
    /// </summary>
    /// <typeparam name="TNext">The type of the next result value.</typeparam>
    /// <param name="func">The function to execute with the current value.</param>
    /// <returns>The result returned by the function.</returns>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func)
    {
        if (IsFailure)
        {
            return Result<TNext>.Failure(Error);
        }
        return func(Value);
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    /// <param name="action">The action to execute with the value.</param>
    /// <returns>The current result.</returns>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
        {
            action(Value);
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    /// <param name="action">The action to execute with the error message.</param>
    /// <returns>The current result.</returns>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure)
        {
            action(Error);
        }
        return this;
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the specified default value.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The value or default value.</returns>
    public T ValueOr(T defaultValue) => IsSuccess ? Value : defaultValue;
}
