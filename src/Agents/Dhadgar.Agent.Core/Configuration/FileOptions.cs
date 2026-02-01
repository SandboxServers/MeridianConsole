using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Configuration for file operations.
/// </summary>
public sealed class FileOptions
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
    /// </summary>
    [Url]
    public string StunServerUrl { get; set; } = "stun:stun.l.google.com:19302";

    /// <summary>
    /// TURN server URL for relay fallback.
    /// </summary>
    [Url]
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
}
