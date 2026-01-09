namespace Dhadgar.Identity.Services;

public static class ServiceResult
{
    public static ServiceResult<T> Fail<T>(string error)
        => new(false, error, default);

    public static ServiceResult<T> Ok<T>(T value)
        => new(true, null, value);
}

public sealed record ServiceResult<T>(bool Success, string? Error, T? Value);
