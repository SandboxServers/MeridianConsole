using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// The Windows container provider as L30 leaves it: a stub that is unavailable. Windows members arrive in L31.
/// </summary>
public sealed class WindowsContainerProvider : IPoolProvider
{
    private const string Unavailable = "[Lab.Unavailable] windows members arrive in L31";

    /// <inheritdoc />
    public string Kind => "windows-container";

    /// <inheritdoc />
    public PoolDimensions Dimensions { get; } = new("windows", null, "servercore", "process", "container", "x64", "host", false);

    /// <inheritdoc />
    public Task<Result<bool>> IsAvailableAsync(CancellationToken ct) => Task.FromResult(Result<bool>.Failure(Unavailable));

    /// <inheritdoc />
    public Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, string memberId, CancellationToken ct) =>
        Task.FromResult(Result<PoolMember>.Failure(Unavailable));

    /// <inheritdoc />
    public Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct) =>
        Task.FromResult(Result<PoolMember>.Failure(Unavailable));

    /// <inheritdoc />
    public Task<Result> DestroyAsync(PoolMember member, CancellationToken ct) => Task.FromResult(Result.Failure(Unavailable));
}
