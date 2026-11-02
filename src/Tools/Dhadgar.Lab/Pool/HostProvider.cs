using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// The host provider (L30): one read-only member, <c>host</c>. Provisioning returns it without touching the machine; reset and
/// destroy are refused (C13: <c>m:host</c> is read-only). It has no container, so the registry never holds it.
/// </summary>
public sealed class HostProvider : IPoolProvider
{
    /// <summary>The member id of the host.</summary>
    public const string MemberId = "host";

    /// <summary>The refusal every mutating call returns for the host.</summary>
    public const string ReadOnlyError = "[Lab.BlastRadius] host is read-only";

    /// <inheritdoc />
    public string Kind => "host";

    /// <inheritdoc />
    public PoolDimensions Dimensions { get; } = new(
        "windows",
        Environment.OSVersion.Version.Build,
        "host",
        "host",
        "host",
        "x64",
        "host",
        false);

    /// <inheritdoc />
    public Task<Result<bool>> IsAvailableAsync(CancellationToken ct) => Task.FromResult(Result<bool>.Success(true));

    /// <inheritdoc />
    public Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, string memberId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        return Task.FromResult(Result<PoolMember>.Success(new PoolMember(
            MemberId,
            "m:" + MemberId,
            null,
            Dimensions,
            MemberState.Ready,
            DateTimeOffset.MinValue)));
    }

    /// <inheritdoc />
    public Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct) =>
        Task.FromResult(Result<PoolMember>.Failure(ReadOnlyError));

    /// <inheritdoc />
    public Task<Result> DestroyAsync(PoolMember member, CancellationToken ct) =>
        Task.FromResult(Result.Failure(ReadOnlyError));
}
