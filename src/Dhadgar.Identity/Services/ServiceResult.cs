namespace Dhadgar.Identity.Services;

public static class ServiceResult
{
    public static ServiceResult<T> Fail<T>(string error)
        => new(false, error, default);

    public static ServiceResult<T> Ok<T>(T value)
        => new(true, null, value);
}

public sealed record ServiceResult<T>(bool Success, string? Error, T? Value);

/// <summary>
/// Result of a bulk operation supporting partial success.
/// </summary>
/// <typeparam name="T">The type of identifier for items in the operation.</typeparam>
public sealed record BulkOperationResult<T>(
    IReadOnlyCollection<T> Succeeded,
    IReadOnlyCollection<BulkItemError<T>> Failed)
{
    public int TotalRequested => Succeeded.Count + Failed.Count;
    public bool PartialSuccess => Succeeded.Count > 0 && Failed.Count > 0;
    public bool AllSucceeded => Failed.Count == 0;
    public bool AllFailed => Succeeded.Count == 0 && Failed.Count > 0;
}

/// <summary>
/// Error information for a single item in a bulk operation.
/// </summary>
/// <typeparam name="T">The type of identifier for the item.</typeparam>
public sealed record BulkItemError<T>(
    T ItemId,
    string ErrorCode,
    string? Details);
