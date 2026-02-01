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
                // Ensure path is normalized (no .. or . components)
                var normalizedPath = Path.GetFullPath(TempDirectory);
                if (!TempDirectory.Equals(normalizedPath, StringComparison.Ordinal) &&
                    !TempDirectory.Equals(normalizedPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                {
                    yield return new ValidationResult(
                        $"{nameof(TempDirectory)} must be a normalized absolute path (use '{normalizedPath}' instead)",
                        [nameof(TempDirectory)]);
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
        return pattern.IsMatch(url);
    }

    [GeneratedRegex(@"^stuns?:[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*(\:\d{1,5})?$", RegexOptions.Compiled)]
    private static partial Regex StunUrlRegex();

    [GeneratedRegex(@"^turns?:[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*(\:\d{1,5})?$", RegexOptions.Compiled)]
    private static partial Regex TurnUrlRegex();
}
