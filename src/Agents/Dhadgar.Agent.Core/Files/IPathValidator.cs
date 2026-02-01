using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Validates file paths for security (prevents path traversal attacks).
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Validate that a path is within allowed directories.
    /// </summary>
    /// <param name="path">Path to validate.</param>
    /// <param name="allowedBasePaths">Allowed base directories.</param>
    /// <returns>Validation result with normalized path.</returns>
    Result<string> ValidatePath(string path, IEnumerable<string> allowedBasePaths);

    /// <summary>
    /// Check if a path is safe (no directory traversal, valid characters).
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if path is safe.</returns>
    bool IsSafePath(string path);

    /// <summary>
    /// Normalize a path for the current platform.
    /// Returns Result to avoid exception-based DoS.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>Result containing normalized path or failure.</returns>
    Result<string> NormalizePath(string path);
}
