namespace Dhadgar.Identity.Services;

public sealed record ServiceResult<T>(bool Success, string? Error, T? Value)
{
    public static ServiceResult<T> Fail(string error)
        => new(false, error, default);

    public static ServiceResult<T> Ok(T value)
        => new(true, null, value);
}
