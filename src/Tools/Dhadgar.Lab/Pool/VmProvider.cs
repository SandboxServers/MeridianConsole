using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// The vm provider (L30): a placeholder that every call refuses. The vm tier is a follow-up (D-ML31).
/// </summary>
public sealed class VmProvider : IPoolProvider
{
    private const string Unavailable = "[Lab.Unavailable] vm tier is a follow-up (D-ML31)";

    /// <inheritdoc />
    public string Kind => "vm";

    /// <inheritdoc />
    public PoolDimensions Dimensions { get; } = new("windows", null, "servercore", "hyperv", "vm", "x64", "own", true);

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
