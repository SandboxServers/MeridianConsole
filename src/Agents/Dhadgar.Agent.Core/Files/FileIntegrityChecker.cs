using System.Security.Cryptography;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Verifies file integrity using SHA256 hashes.
/// </summary>
public sealed class FileIntegrityChecker : IFileIntegrityChecker
{
    private const int BufferSize = 81920; // 80KB buffer for efficient streaming
    private readonly ILogger<FileIntegrityChecker> _logger;

    public FileIntegrityChecker(ILogger<FileIntegrityChecker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found for hash computation", filePath);
        }

        _logger.LogDebug("Computing SHA256 hash for {FilePath}", filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _logger.LogDebug("Computed hash for {FilePath}: {Hash}", filePath, hashString);

        return hashString;
    }

    public async Task<Result<bool>> VerifyHashAsync(
        string filePath,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        if (!File.Exists(filePath))
        {
            return Result<bool>.Failure($"[File.NotFound] File not found: {filePath}");
        }

        try
        {
            var actualHash = await ComputeHashAsync(filePath, cancellationToken);
            var normalizedExpected = expectedHash.ToLowerInvariant().Trim();

            if (actualHash.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Hash verification succeeded for {FilePath}", filePath);
                return Result<bool>.Success(true);
            }

            _logger.LogWarning(
                "Hash verification failed for {FilePath}. Expected: {Expected}, Actual: {Actual}",
                filePath,
                normalizedExpected,
                actualHash);

            return Result<bool>.Failure(
                $"[File.HashMismatch] Hash mismatch. Expected: {normalizedExpected}, Actual: {actualHash}");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error during hash verification for {FilePath}", filePath);
            return Result<bool>.Failure(
                $"[File.IOError] IO error during hash verification: {ex.Message}");
        }
    }
}
