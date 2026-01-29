using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Services;

public interface IEnrollmentTokenService
{
    /// <summary>
    /// Creates a new enrollment token for the organization.
    /// Returns both the token entity (with hash) and the plaintext token (for display).
    /// </summary>
    Task<(EnrollmentToken Token, string PlainTextToken)> CreateTokenAsync(
        Guid organizationId,
        string createdByUserId,
        string? label,
        TimeSpan? validity = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a plaintext token and returns the token entity if valid.
    /// Returns null if token is invalid, expired, revoked, or already used.
    /// </summary>
    Task<EnrollmentToken?> ValidateTokenAsync(
        string plainTextToken,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a token as used by a specific node.
    /// </summary>
    Task MarkTokenUsedAsync(
        Guid tokenId,
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a token so it can no longer be used.
    /// Returns true if the token was found and revoked, false if not found or doesn't belong to the organization.
    /// </summary>
    Task<bool> RevokeTokenAsync(
        Guid organizationId,
        Guid tokenId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active (non-expired, non-revoked, unused) tokens for an organization.
    /// </summary>
    Task<IReadOnlyList<EnrollmentTokenSummary>> GetActiveTokensAsync(
        Guid organizationId,
        CancellationToken ct = default);
}
