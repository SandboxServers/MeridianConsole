using System.Diagnostics.CodeAnalysis;

namespace Dhadgar.Mods.Services;

public static class ServiceResult
{
    public static ServiceResult<T> Fail<T>(string error)
        => new(false, error, default);

    public static ServiceResult<T> Ok<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, null, value);
    }
}

/// <summary>
/// Represents the result of a service operation that may fail.
/// When Success is true, Value is guaranteed to be non-null.
/// </summary>
public sealed record ServiceResult<T>
{
    public ServiceResult(bool success, string? error, T? value)
    {
        Success = success;
        Error = error;
        Value = value;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool Success { get; }

    public string? Error { get; }

    public T? Value { get; }
}
