using System.Buffers;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Validates file paths for security (prevents path traversal attacks).
/// </summary>
public sealed class PathValidator : IPathValidator
{
    private readonly ILogger<PathValidator> _logger;

    // Characters that are not allowed in path components (cached for performance)
    private static readonly SearchValues<char> InvalidPathChars =
        SearchValues.Create(['<', '>', ':', '"', '|', '?', '*', '\0']);

    // Patterns that indicate path traversal attempts
    private static readonly string[] TraversalPatterns = ["..", "..\\", "../"];

    // Maximum number of allowed base paths to process (prevent DoS via excessive iteration)
    private const int MaxAllowedBasePaths = 100;

    public PathValidator(ILogger<PathValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<string> ValidatePath(string path, IEnumerable<string> allowedBasePaths)
    {
        // Validate inputs without throwing to prevent exception-based DoS
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<string>.Failure("[Path.Empty] Path cannot be null or empty");
        }

        if (allowedBasePaths is null)
        {
            return Result<string>.Failure("[Path.InvalidConfig] Allowed base paths not configured");
        }

        try
        {
            // Normalize the path
            var normalizeResult = NormalizePath(path);
            if (!normalizeResult.IsSuccess)
            {
                return Result<string>.Failure(normalizeResult.Error!);
            }
            var normalizedPath = normalizeResult.Value!;

            // Check for path safety
            if (!IsSafePath(normalizedPath))
            {
                // SECURITY: Log sanitized path info (length only) to prevent log injection
                _logger.LogWarning("Path validation failed: unsafe path characters (length: {PathLength})", path.Length);
                return Result<string>.Failure(
                    "[Path.Unsafe] Path contains unsafe characters or traversal patterns");
            }

            // Convert to absolute path
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(normalizedPath);
            }
            catch (PathTooLongException)
            {
                _logger.LogWarning("Path validation failed: path too long (length: {PathLength})", path.Length);
                return Result<string>.Failure("[Path.TooLong] Path exceeds maximum length");
            }

            // Check if the path is within any of the allowed base paths
            var isAllowed = false;
            var processedCount = 0;

            foreach (var basePath in allowedBasePaths)
            {
                // Cap iterations to prevent DoS via excessive allowedBasePaths
                if (++processedCount > MaxAllowedBasePaths)
                {
                    _logger.LogWarning("Exceeded maximum allowed base paths limit ({Limit})", MaxAllowedBasePaths);
                    break;
                }

                if (string.IsNullOrWhiteSpace(basePath))
                {
                    continue; // Skip invalid base paths
                }

                string normalizedBase;
                try
                {
                    var baseNormalizeResult = NormalizePath(basePath);
                    if (!baseNormalizeResult.IsSuccess)
                    {
                        _logger.LogWarning("Skipping invalid base path during validation: normalization failed");
                        continue; // Skip malformed base paths
                    }
                    normalizedBase = Path.GetFullPath(baseNormalizeResult.Value!);
                }
                catch (Exception ex) when (ex is ArgumentException or PathTooLongException or System.Security.SecurityException)
                {
                    _logger.LogWarning(ex, "Skipping invalid base path during validation");
                    continue; // Skip malformed base paths
                }

                // Ensure the base path ends with a separator to prevent partial matches
                // e.g., prevent "/allowed/path" from matching "/allowed/pathmalicious"
                if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
                {
                    normalizedBase += Path.DirectorySeparatorChar;
                }

                // Use case-insensitive comparison on Windows, case-sensitive on Unix
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (fullPath.StartsWith(normalizedBase, comparison) ||
                    fullPath.Equals(normalizedBase.TrimEnd(Path.DirectorySeparatorChar), comparison))
                {
                    isAllowed = true;
                    break;
                }
            }

            if (!isAllowed)
            {
                // SECURITY: Log sanitized path info only
                _logger.LogWarning(
                    "Path validation failed: path (length: {PathLength}) is not within allowed directories",
                    path.Length);
                return Result<string>.Failure(
                    "[Path.NotAllowed] Path is not within allowed directories");
            }

            return Result<string>.Success(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Path validation failed (length: {PathLength})", path.Length);
            return Result<string>.Failure("[Path.Invalid] Path validation failed");
        }
    }

    public bool IsSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Check for invalid characters, but allow colons for Windows drive letters (e.g., C:\)
        var spanToCheck = path.AsSpan();
        if (OperatingSystem.IsWindows() && path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            // Skip the drive letter portion (e.g., "C:") when checking for invalid chars
            spanToCheck = spanToCheck.Slice(2);
        }

        if (spanToCheck.IndexOfAny(InvalidPathChars) >= 0)
        {
            return false;
        }

        // Check for traversal patterns
        foreach (var pattern in TraversalPatterns)
        {
            if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Check for null bytes (potential injection)
        if (path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        // Check for control characters
        foreach (var c in path)
        {
            if (char.IsControl(c) && c != '\t')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Normalizes a file path by replacing alternate separators and removing duplicates.
    /// Returns Result to avoid exception-based DoS.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>Result containing the normalized path or a failure.</returns>
    public Result<string> NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<string>.Failure("[Path.Empty] Path cannot be null or empty");
        }

        try
        {
            // Replace alternate separators with the platform separator
            var normalized = path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);

            // Remove duplicate separators, but preserve the root (drive/UNC/device prefix)
            // This prevents \\server\share from becoming \server\share or C:\ becoming C:
            var root = Path.GetPathRoot(normalized) ?? string.Empty;
            var rest = normalized[root.Length..];
            var doubleSeparator = $"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}";
            while (rest.Contains(doubleSeparator, StringComparison.Ordinal))
            {
                rest = rest.Replace(
                    doubleSeparator,
                    $"{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal);
            }
            normalized = root + rest;

            // Trim trailing separators (except for root)
            if (normalized.Length > root.Length + 1)
            {
                normalized = Path.TrimEndingDirectorySeparator(normalized);
            }

            return Result<string>.Success(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return Result<string>.Failure("[Path.NormalizationFailed] Path normalization failed");
        }
    }
}
