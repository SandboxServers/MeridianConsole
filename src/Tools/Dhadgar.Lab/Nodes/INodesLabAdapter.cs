using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// The lab's only route into the Nodes product: it mints tokens and shifts certificate times through Nodes internals (C6).
/// </summary>
public interface INodesLabAdapter
{
    Task<Result<MintedToken>> MintTokenAsync(Guid organizationId, string label, TimeSpan validity, CancellationToken ct);
    Task<Result<int>> ShiftCertificateTimesAsync(Guid nodeId, TimeSpan shift, IReadOnlyCollection<CertTimeField> fields, CancellationToken ct);
}

/// <summary>
/// A minted enrollment token. The plain token is never returned; it is held in the vault by <see cref="TokenRef"/>.
/// </summary>
public sealed record MintedToken(Guid TokenId, string TokenRef, DateTimeOffset ExpiresAt);

/// <summary>
/// Certificate timestamps the lab may shift to simulate expiry and clock skew.
/// </summary>
public enum CertTimeField { IssuedAt, NotBefore, NotAfter, GraceUntil }
