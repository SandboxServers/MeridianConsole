using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// The dimensions a pool member reports (C13). Constraints select members by these values.
/// </summary>
public sealed record PoolDimensions(
    string Os,          // "windows" | "linux"
    int? OsBuild,       // e.g. 26100
    string Sku,         // "servercore" | "nanoserver" | "debian" | "host"
    string Isolation,   // "process" | "hyperv" | "none"
    string Kind,        // "container" | "host" | "vm"
    string Arch,        // "x64" | "arm64"
    string Clock,       // "host" (shared); "own" only for vm
    bool Reboot);       // false for containers

/// <summary>
/// What a caller asks for (C13). A null field matches any value; <see cref="Count"/> members are provisioned.
/// </summary>
public sealed record PoolConstraints(string? Os, int? MinOsBuild, string? Sku, string? Isolation, string? Kind, string? Arch,
    bool? Reboot, int Count = 1);

/// <summary>
/// The lifecycle of one pool member (C13).
/// </summary>
public enum MemberState { Provisioning, Ready, Leased, Resetting, Failed }

/// <summary>
/// One pool member (C13). Member ids: <c>lin-01</c>, <c>win-01</c>, <c>host</c>; the target id is <c>m:&lt;member id&gt;</c>.
/// </summary>
public sealed record PoolMember(string MemberId, string TargetId, string? ContainerName, PoolDimensions Dimensions,
    MemberState State, DateTimeOffset CreatedAt);

/// <summary>
/// One provider of pool members (C13). Providers never decide policy: <see cref="PoolManager"/> chooses, limits and records.
/// </summary>
public interface IPoolProvider
{
    /// <summary>
    /// The provider kind, such as <c>linux-container</c>, <c>host</c>, <c>vm</c> or <c>windows-container</c>.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// The dimensions every member of this provider reports. Used to match constraints before anything is provisioned.
    /// </summary>
    PoolDimensions Dimensions { get; }

    /// <summary>
    /// True when the provider can provision now. A false answer carries the reason in the Result error.
    /// </summary>
    Task<Result<bool>> IsAvailableAsync(CancellationToken ct);

    /// <summary>
    /// Provisions one member with the given id (the manager assigns ids).
    /// </summary>
    Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, string memberId, CancellationToken ct);

    /// <summary>
    /// Recreates the member from the image with the same id.
    /// </summary>
    Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct);

    /// <summary>
    /// Destroys the member's container. Only called for members the registry holds.
    /// </summary>
    Task<Result> DestroyAsync(PoolMember member, CancellationToken ct);
}

/// <summary>
/// One provider as <c>pool_list</c> shows it.
/// </summary>
/// <param name="Kind">The provider kind.</param>
/// <param name="Available">Whether it can provision now.</param>
/// <param name="Reason">The reason it cannot, when it cannot.</param>
public sealed record PoolProviderStatus(string Kind, bool Available, string? Reason);
