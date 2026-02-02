using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Service for managing file transfers between agent and control plane or users.
/// </summary>
public interface IFileTransferService
{
    /// <summary>
    /// Download a file from the control plane.
    /// </summary>
    /// <param name="request">Download request details.</param>
    /// <param name="progress">Progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with local file path.</returns>
    Task<Result<FileTransferResult>> DownloadAsync(
        FileDownloadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a file to the control plane.
    /// </summary>
    /// <param name="request">Upload request details.</param>
    /// <param name="progress">Progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with remote file identifier.</returns>
    Task<Result<FileTransferResult>> UploadAsync(
        FileUploadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the status of an ongoing transfer.
    /// </summary>
    /// <param name="transferId">Transfer identifier.</param>
    /// <returns>Transfer status, or null if not found.</returns>
    FileTransferStatus? GetTransferStatus(Guid transferId);

    /// <summary>
    /// Cancel an ongoing transfer.
    /// </summary>
    /// <param name="transferId">Transfer identifier.</param>
    Task CancelTransferAsync(Guid transferId);

    /// <summary>
    /// Get all active transfers.
    /// </summary>
    IReadOnlyList<FileTransferStatus> GetActiveTransfers();
}

/// <summary>
/// Request to download a file.
/// </summary>
public sealed class FileDownloadRequest
{
    /// <summary>
    /// Unique transfer identifier.
    /// </summary>
    public Guid TransferId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Remote file URL or identifier.
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Local destination path.
    /// </summary>
    public required string DestinationPath { get; init; }

    /// <summary>
    /// Expected file hash for verification (SHA256).
    /// </summary>
    public string? ExpectedHash { get; init; }

    /// <summary>
    /// Expected file size in bytes.
    /// </summary>
    public long? ExpectedSizeBytes { get; init; }

    /// <summary>
    /// Allow P2P transfer if available.
    /// </summary>
    public bool AllowP2P { get; init; } = true;
}

/// <summary>
/// Request to upload a file.
/// </summary>
public sealed class FileUploadRequest
{
    /// <summary>
    /// Unique transfer identifier.
    /// </summary>
    public Guid TransferId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Local source file path.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Remote destination identifier or path.
    /// </summary>
    public string? DestinationId { get; init; }

    /// <summary>
    /// Allow P2P transfer if available.
    /// </summary>
    public bool AllowP2P { get; init; } = true;
}

/// <summary>
/// Result of a file transfer.
/// </summary>
public sealed class FileTransferResult
{
    /// <summary>
    /// Transfer identifier.
    /// </summary>
    public required Guid TransferId { get; init; }

    /// <summary>
    /// Local file path (for downloads).
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    /// Remote file identifier (for uploads).
    /// </summary>
    public string? RemoteId { get; init; }

    /// <summary>
    /// File hash (SHA256).
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// Transfer duration.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether P2P transfer was used.
    /// </summary>
    public bool UsedP2P { get; init; }
}

/// <summary>
/// Progress of an ongoing transfer.
/// </summary>
public sealed class FileTransferProgress
{
    /// <summary>
    /// Transfer identifier.
    /// </summary>
    public required Guid TransferId { get; init; }

    /// <summary>
    /// Bytes transferred so far.
    /// </summary>
    public required long BytesTransferred { get; init; }

    /// <summary>
    /// Total bytes to transfer.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercent =>
        TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100 : 0;

    /// <summary>
    /// Current transfer rate in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; init; }
}

/// <summary>
/// Status of a file transfer.
/// </summary>
public sealed class FileTransferStatus
{
    /// <summary>
    /// Transfer identifier.
    /// </summary>
    public required Guid TransferId { get; init; }

    /// <summary>
    /// Transfer direction.
    /// </summary>
    public required FileTransferDirection Direction { get; init; }

    /// <summary>
    /// Current state.
    /// </summary>
    public required FileTransferState State { get; init; }

    /// <summary>
    /// Current progress.
    /// </summary>
    public FileTransferProgress? Progress { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When the transfer started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Whether using P2P transfer.
    /// </summary>
    public bool UsingP2P { get; init; }
}

/// <summary>
/// File transfer direction.
/// </summary>
public enum FileTransferDirection
{
    Download,
    Upload
}

/// <summary>
/// File transfer state.
/// </summary>
public enum FileTransferState
{
    Pending,
    Connecting,
    Transferring,
    Verifying,
    Completed,
    Failed,
    Cancelled
}
