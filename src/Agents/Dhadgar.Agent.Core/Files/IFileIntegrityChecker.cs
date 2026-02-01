using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Verifies file integrity using cryptographic hashes.
/// </summary>
public interface IFileIntegrityChecker
{
    /// <summary>
    /// Compute the SHA256 hash of a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SHA256 hash as hex string.</returns>
    Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a file's hash matches expected value.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="expectedHash">Expected SHA256 hash (hex string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result.</returns>
    Task<Result<bool>> VerifyHashAsync(
        string filePath,
        string expectedHash,
        CancellationToken cancellationToken = default);
}
