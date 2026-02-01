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

    public PathValidator(ILogger<PathValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<string> ValidatePath(string path, IEnumerable<string> allowedBasePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(allowedBasePaths);

        // Normalize the path
        var normalizedPath = NormalizePath(path);

        // Check for path safety
        if (!IsSafePath(normalizedPath))
        {
            _logger.LogWarning("Path validation failed: unsafe path characters in {Path}", path);
            return Result<string>.Failure(
                "[Path.Unsafe] Path contains unsafe characters or traversal patterns");
        }

        // Convert to absolute path
        var fullPath = Path.GetFullPath(normalizedPath);

        // Check if the path is within any of the allowed base paths
        var allowedBases = allowedBasePaths.ToList();
        var isAllowed = false;

        foreach (var basePath in allowedBases)
        {
            var normalizedBase = Path.GetFullPath(NormalizePath(basePath));

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
            _logger.LogWarning(
                "Path validation failed: {Path} is not within allowed directories",
                path);
            return Result<string>.Failure(
                "[Path.NotAllowed] Path is not within allowed directories");
        }

        return Result<string>.Success(fullPath);
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

    public string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Replace alternate separators with the platform separator
        var normalized = path.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        // Remove duplicate separators
        var doubleSeparator = $"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}";
        while (normalized.Contains(doubleSeparator, StringComparison.Ordinal))
        {
            normalized = normalized.Replace(
                doubleSeparator,
                $"{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
        }

        // Trim trailing separators (except for root)
        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
        }

        return normalized;
    }
}
