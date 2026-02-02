using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Configuration for file operations.
/// </summary>
public sealed partial class FileOptions : IValidatableObject
{
    /// <summary>
    /// Temporary directory for downloads and staging.
    /// </summary>
    [Required]
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum file size for transfers (bytes). Default: 10GB.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>
    /// Enable P2P file transfer (ICE/STUN/TURN).
    /// </summary>
    public bool EnableP2PTransfer { get; set; } = true;

    /// <summary>
    /// STUN server URL for ICE connectivity.
    /// Supports stun: and stuns: schemes.
    /// </summary>
    public string StunServerUrl { get; set; } = "stun:stun.l.google.com:19302";

    /// <summary>
    /// TURN server URL for relay fallback.
    /// Supports turn: and turns: schemes.
    /// </summary>
    public string? TurnServerUrl { get; set; }

    /// <summary>
    /// TURN server username (if required).
    /// </summary>
    public string? TurnServerUsername { get; set; }

    /// <summary>
    /// TURN server credential (if required).
    /// </summary>
    public string? TurnServerCredential { get; set; }

    /// <summary>
    /// Chunk size for file transfers in bytes. Default: 64KB.
    /// </summary>
    [Range(1024, 1024 * 1024)]
    public int TransferChunkSizeBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Number of retry attempts for failed transfers.
    /// </summary>
    [Range(0, 10)]
    public int TransferRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in seconds.
    /// </summary>
    [Range(1, 60)]
    public int TransferRetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of concurrent file transfers.
    /// Prevents resource exhaustion from unbounded parallel transfers.
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrentTransfers { get; set; } = 10;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // TempDirectory must be an absolute, normalized path (same as ProcessOptions.ServerBasePath)
        if (!string.IsNullOrEmpty(TempDirectory))
        {
            if (!Path.IsPathRooted(TempDirectory))
            {
                yield return new ValidationResult(
                    $"{nameof(TempDirectory)} must be an absolute path",
                    [nameof(TempDirectory)]);
            }
            else
            {
                // Use TryGetFullPath to safely normalize (handles invalid paths without throwing)
                if (!TryGetFullPath(TempDirectory, out var normalizedPath))
                {
                    yield return new ValidationResult(
                        $"{nameof(TempDirectory)} contains invalid path characters or is malformed",
                        [nameof(TempDirectory)]);
                }
                else
                {
                    // OS-aware comparison: case-insensitive on Windows, case-sensitive elsewhere
                    var comparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                    var trimmedNormalized = normalizedPath.TrimEnd(Path.DirectorySeparatorChar);
                    var trimmedInput = TempDirectory.TrimEnd(Path.DirectorySeparatorChar);

                    if (!trimmedInput.Equals(trimmedNormalized, comparison))
                    {
                        yield return new ValidationResult(
                            $"{nameof(TempDirectory)} must be a normalized absolute path (use '{normalizedPath}' instead)",
                            [nameof(TempDirectory)]);
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(StunServerUrl) && !IsValidStunTurnUrl(StunServerUrl, isStun: true))
        {
            yield return new ValidationResult(
                $"{nameof(StunServerUrl)} must be a valid STUN URL with scheme 'stun:' or 'stuns:'",
                [nameof(StunServerUrl)]);
        }

        if (!string.IsNullOrEmpty(TurnServerUrl) && !IsValidStunTurnUrl(TurnServerUrl, isStun: false))
        {
            yield return new ValidationResult(
                $"{nameof(TurnServerUrl)} must be a valid TURN URL with scheme 'turn:' or 'turns:'",
                [nameof(TurnServerUrl)]);
        }
    }

    /// <summary>
    /// Validates a STUN or TURN URL format.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="isStun">True for STUN URLs (stun/stuns), false for TURN URLs (turn/turns).</param>
    /// <returns>True if the URL is valid, false otherwise.</returns>
    private static bool IsValidStunTurnUrl(string url, bool isStun)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Pattern: scheme:host[:port]
        // Host can be hostname or IP address
        var pattern = isStun ? StunUrlRegex() : TurnUrlRegex();
        if (!pattern.IsMatch(url))
        {
            return false;
        }

        // Extract and validate port if present (regex allows \d{1,5} but max valid port is 65535)
        var colonIndex = url.LastIndexOf(':');
        if (colonIndex > 0)
        {
            // Find the port portion (after the last colon, if it's a port not part of scheme)
            var afterLastColon = url[(colonIndex + 1)..];
            // Check if it looks like a port (all digits)
            if (afterLastColon.Length > 0 && afterLastColon.All(char.IsDigit))
            {
                if (int.TryParse(afterLastColon, out var port) && (port < 1 || port > 65535))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Safely attempts to get the full path, returning false if the path is invalid.
    /// </summary>
    private static bool TryGetFullPath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^stuns?:[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*(\:\d{1,5})?$", RegexOptions.Compiled)]
    private static partial Regex StunUrlRegex();

    [GeneratedRegex(@"^turns?:[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*(\:\d{1,5})?$", RegexOptions.Compiled)]
    private static partial Regex TurnUrlRegex();
}
