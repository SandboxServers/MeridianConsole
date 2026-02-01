using System.Collections.Concurrent;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentFileOptions = Dhadgar.Agent.Core.Configuration.FileOptions;

namespace Dhadgar.Agent.Core.Files;

/// <summary>
/// Manages file transfers between agent and control plane.
/// </summary>
public sealed class FileTransferService : IFileTransferService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPathValidator _pathValidator;
    private readonly IFileIntegrityChecker _integrityChecker;
    private readonly AgentFileOptions _fileOptions;
    private readonly AgentOptions _agentOptions;
    private readonly AgentMeter _meter;
    private readonly ILogger<FileTransferService> _logger;

    private readonly ConcurrentDictionary<Guid, TransferState> _activeTransfers = new();

    public FileTransferService(
        IHttpClientFactory httpClientFactory,
        IPathValidator pathValidator,
        IFileIntegrityChecker integrityChecker,
        IOptions<AgentFileOptions> fileOptions,
        IOptions<AgentOptions> agentOptions,
        AgentMeter meter,
        ILogger<FileTransferService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
        _integrityChecker = integrityChecker ?? throw new ArgumentNullException(nameof(integrityChecker));
        _fileOptions = fileOptions?.Value ?? throw new ArgumentNullException(nameof(fileOptions));
        _agentOptions = agentOptions?.Value ?? throw new ArgumentNullException(nameof(agentOptions));
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<FileTransferResult>> DownloadAsync(
        FileDownloadRequest? request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result<FileTransferResult>.Failure(
                "[Transfer.InvalidRequest] Download request cannot be null");
        }

        // SECURITY: Enforce concurrent transfer limit to prevent resource exhaustion
        if (_activeTransfers.Count >= _fileOptions.MaxConcurrentTransfers)
        {
            _logger.LogWarning(
                "Transfer limit reached ({Count}/{Max}), rejecting download {TransferId}",
                _activeTransfers.Count, _fileOptions.MaxConcurrentTransfers, request.TransferId);
            return Result<FileTransferResult>.Failure(
                "[Transfer.LimitReached] Maximum concurrent transfer limit reached");
        }

        var transferState = new TransferState
        {
            TransferId = request.TransferId,
            Direction = FileTransferDirection.Download,
            State = FileTransferState.Pending,
            StartedAt = DateTimeOffset.UtcNow,
            CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        if (!_activeTransfers.TryAdd(request.TransferId, transferState))
        {
            transferState.CancellationSource.Dispose();
            return Result<FileTransferResult>.Failure(
                "[Transfer.Duplicate] A transfer with this ID is already in progress");
        }

        try
        {
            // Validate destination path
            var allowedPaths = new[] { _fileOptions.TempDirectory, _agentOptions.Process.ServerBasePath };
            var pathResult = _pathValidator.ValidatePath(request.DestinationPath, allowedPaths);
            if (!pathResult.IsSuccess)
            {
                return Result<FileTransferResult>.Failure(pathResult.Error!);
            }

            var destinationPath = pathResult.Value!;

            // Ensure directory exists
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            transferState.State = FileTransferState.Connecting;

            // Download the file
            using var client = _httpClientFactory.CreateClient("ControlPlaneMtls");

            // SECURITY: Enforce HTTPS for all file transfers
            if (client.BaseAddress is not null &&
                !string.Equals(client.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return Result<FileTransferResult>.Failure(
                    "[Transfer.InsecureTransport] File transfers require HTTPS");
            }

            // Validate source URL - must be absolute if no base address configured
            if (client.BaseAddress is null && !Uri.IsWellFormedUriString(request.SourceUrl, UriKind.Absolute))
            {
                return Result<FileTransferResult>.Failure(
                    "[Transfer.InvalidUrl] Source URL must be absolute when no base address is configured");
            }

            // Validate source URL scheme if it's an absolute URL
            if (Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var sourceUri) &&
                !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return Result<FileTransferResult>.Failure(
                    "[Transfer.InsecureTransport] Source URL must use HTTPS");
            }

            using var response = await client.GetAsync(
                request.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead,
                transferState.CancellationSource.Token);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? request.ExpectedSizeBytes ?? 0;

            // Enforce max file size before starting download
            if (totalBytes > _fileOptions.MaxFileSizeBytes)
            {
                return Result<FileTransferResult>.Failure(
                    $"[File.TooLarge] File exceeds maximum size of {_fileOptions.MaxFileSizeBytes} bytes");
            }

            transferState.State = FileTransferState.Transferring;

            var startTime = DateTimeOffset.UtcNow;
            long bytesTransferred = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(transferState.CancellationSource.Token);
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                _fileOptions.TransferChunkSizeBytes,
                System.IO.FileOptions.Asynchronous);

            var buffer = new byte[_fileOptions.TransferChunkSizeBytes];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, transferState.CancellationSource.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), transferState.CancellationSource.Token);
                bytesTransferred += bytesRead;

                // Enforce max file size during download (in case Content-Length was missing or incorrect)
                if (bytesTransferred > _fileOptions.MaxFileSizeBytes)
                {
                    // Clean up partial download
                    await fileStream.DisposeAsync();
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogDebug(cleanupEx,
                            "Failed to clean up oversized download for {TransferId}: {Path}",
                            request.TransferId, destinationPath);
                    }
                    return Result<FileTransferResult>.Failure(
                        $"[File.TooLarge] File exceeds maximum size of {_fileOptions.MaxFileSizeBytes} bytes");
                }

                var elapsed = DateTimeOffset.UtcNow - startTime;
                var bytesPerSecond = elapsed.TotalSeconds > 0
                    ? (long)(bytesTransferred / elapsed.TotalSeconds)
                    : 0;

                var progressReport = new FileTransferProgress
                {
                    TransferId = request.TransferId,
                    BytesTransferred = bytesTransferred,
                    TotalBytes = totalBytes,
                    BytesPerSecond = bytesPerSecond
                };

                transferState.Progress = progressReport;
                progress?.Report(progressReport);
            }

            await fileStream.FlushAsync(transferState.CancellationSource.Token);

            transferState.State = FileTransferState.Verifying;

            // Verify hash if provided
            if (!string.IsNullOrEmpty(request.ExpectedHash))
            {
                var hashResult = await _integrityChecker.VerifyHashAsync(
                    destinationPath,
                    request.ExpectedHash,
                    transferState.CancellationSource.Token);

                if (!hashResult.IsSuccess)
                {
                    // Clean up failed download
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogDebug(cleanupEx,
                            "Failed to clean up partial download for {TransferId}: {Path}",
                            request.TransferId, destinationPath);
                    }
                    return Result<FileTransferResult>.Failure(hashResult.Error!);
                }
            }

            // Compute hash for result
            var fileHash = await _integrityChecker.ComputeHashAsync(
                destinationPath,
                transferState.CancellationSource.Token);

            var duration = DateTimeOffset.UtcNow - startTime;

            transferState.State = FileTransferState.Completed;

            _meter.RecordFileTransfer(bytesTransferred, isUpload: false);
            // SECURITY: Log only filename, not full path to avoid exposing filesystem layout
            _logger.LogInformation(
                "Download completed: {TransferId} to {FileName}, {Bytes} bytes in {Duration}",
                request.TransferId,
                Path.GetFileName(destinationPath),
                bytesTransferred,
                duration);

            return Result<FileTransferResult>.Success(new FileTransferResult
            {
                TransferId = request.TransferId,
                LocalPath = destinationPath,
                FileHash = fileHash,
                FileSizeBytes = bytesTransferred,
                Duration = duration,
                UsedP2P = false // P2P not yet implemented
            });
        }
        catch (OperationCanceledException)
        {
            transferState.State = FileTransferState.Cancelled;

            // Clean up partial download file
            try
            {
                if (File.Exists(request.DestinationPath))
                {
                    File.Delete(request.DestinationPath);
                    _logger.LogDebug("Cleaned up partial download for cancelled transfer {TransferId}", request.TransferId);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up partial download for cancelled transfer {TransferId}", request.TransferId);
            }

            return Result<FileTransferResult>.Failure(
                "[Transfer.Cancelled] File transfer was cancelled");
        }
        catch (Exception ex)
        {
            transferState.State = FileTransferState.Failed;
            transferState.ErrorMessage = ex.Message;
            // SECURITY: Log only filename, not full path
            _logger.LogError(ex, "Download failed for {TransferId}: {FileName}",
                request.TransferId, Path.GetFileName(request.DestinationPath));
            // Return sanitized error message without exception details
            return Result<FileTransferResult>.Failure(
                "[Transfer.Failed] Download failed due to an unexpected error");
        }
        finally
        {
            _activeTransfers.TryRemove(request.TransferId, out _);
            transferState.CancellationSource.Dispose();
        }
    }

    public async Task<Result<FileTransferResult>> UploadAsync(
        FileUploadRequest? request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result<FileTransferResult>.Failure(
                "[Transfer.InvalidRequest] Upload request cannot be null");
        }

        // SECURITY: Enforce concurrent transfer limit to prevent resource exhaustion
        if (_activeTransfers.Count >= _fileOptions.MaxConcurrentTransfers)
        {
            _logger.LogWarning(
                "Transfer limit reached ({Count}/{Max}), rejecting upload {TransferId}",
                _activeTransfers.Count, _fileOptions.MaxConcurrentTransfers, request.TransferId);
            return Result<FileTransferResult>.Failure(
                "[Transfer.LimitReached] Maximum concurrent transfer limit reached");
        }

        var transferState = new TransferState
        {
            TransferId = request.TransferId,
            Direction = FileTransferDirection.Upload,
            State = FileTransferState.Pending,
            StartedAt = DateTimeOffset.UtcNow,
            CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        if (!_activeTransfers.TryAdd(request.TransferId, transferState))
        {
            transferState.CancellationSource.Dispose();
            return Result<FileTransferResult>.Failure(
                "[Transfer.Duplicate] A transfer with this ID is already in progress");
        }

        try
        {
            // Validate source path to prevent path traversal attacks
            var allowedPaths = new[] { _fileOptions.TempDirectory, _agentOptions.Process.ServerBasePath };
            var pathResult = _pathValidator.ValidatePath(request.SourcePath, allowedPaths);
            if (!pathResult.IsSuccess)
            {
                return Result<FileTransferResult>.Failure(pathResult.Error!);
            }

            var sourcePath = pathResult.Value!;

            if (!File.Exists(sourcePath))
            {
                return Result<FileTransferResult>.Failure(
                    "[File.NotFound] Source file not found");
            }

            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Length > _fileOptions.MaxFileSizeBytes)
            {
                return Result<FileTransferResult>.Failure(
                    $"[File.TooLarge] File exceeds maximum size of {_fileOptions.MaxFileSizeBytes} bytes");
            }

            transferState.State = FileTransferState.Connecting;

            var startTime = DateTimeOffset.UtcNow;

            // Compute hash before upload
            var fileHash = await _integrityChecker.ComputeHashAsync(
                sourcePath,
                transferState.CancellationSource.Token);

            transferState.State = FileTransferState.Transferring;

            using var client = _httpClientFactory.CreateClient("ControlPlaneMtls");

            // SECURITY: Require BaseAddress for uploads (uses relative URL)
            if (client.BaseAddress is null)
            {
                return Result<FileTransferResult>.Failure(
                    "[Transfer.ConfigError] Control plane base address not configured for uploads");
            }

            // SECURITY: Enforce HTTPS for all file transfers
            if (!string.Equals(client.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return Result<FileTransferResult>.Failure(
                    "[Transfer.InsecureTransport] File transfers require HTTPS");
            }

            await using var fileStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _fileOptions.TransferChunkSizeBytes,
                System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);

            using var content = new StreamContent(fileStream);
            content.Headers.ContentLength = fileInfo.Length;

            var uploadUrl = $"api/v1/files/upload";
            if (!string.IsNullOrEmpty(request.DestinationId))
            {
                uploadUrl += $"?destinationId={Uri.EscapeDataString(request.DestinationId)}";
            }

            var response = await client.PostAsync(
                uploadUrl,
                content,
                transferState.CancellationSource.Token);

            response.EnsureSuccessStatusCode();

            var duration = DateTimeOffset.UtcNow - startTime;

            transferState.State = FileTransferState.Completed;

            _meter.RecordFileTransfer(fileInfo.Length, isUpload: true);
            // SECURITY: Log only filename, not full path to avoid exposing filesystem layout
            _logger.LogInformation(
                "Upload completed: {TransferId} from {FileName}, {Bytes} bytes in {Duration}",
                request.TransferId,
                Path.GetFileName(sourcePath),
                fileInfo.Length,
                duration);

            return Result<FileTransferResult>.Success(new FileTransferResult
            {
                TransferId = request.TransferId,
                RemoteId = request.DestinationId,
                FileHash = fileHash,
                FileSizeBytes = fileInfo.Length,
                Duration = duration,
                UsedP2P = false // P2P not yet implemented
            });
        }
        catch (OperationCanceledException)
        {
            transferState.State = FileTransferState.Cancelled;
            return Result<FileTransferResult>.Failure(
                "[Transfer.Cancelled] File transfer was cancelled");
        }
        catch (Exception ex)
        {
            transferState.State = FileTransferState.Failed;
            transferState.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Upload failed for {TransferId}", request.TransferId);
            // Return sanitized error message without exception details
            return Result<FileTransferResult>.Failure(
                "[Transfer.Failed] Upload failed due to an unexpected error");
        }
        finally
        {
            _activeTransfers.TryRemove(request.TransferId, out _);
            transferState.CancellationSource.Dispose();
        }
    }

    public FileTransferStatus? GetTransferStatus(Guid transferId)
    {
        if (!_activeTransfers.TryGetValue(transferId, out var state))
        {
            return null;
        }

        return new FileTransferStatus
        {
            TransferId = state.TransferId,
            Direction = state.Direction,
            State = state.State,
            Progress = state.Progress,
            ErrorMessage = state.ErrorMessage,
            StartedAt = state.StartedAt,
            UsingP2P = false
        };
    }

    public async Task CancelTransferAsync(Guid transferId)
    {
        if (_activeTransfers.TryGetValue(transferId, out var state))
        {
            _logger.LogInformation("Cancelling transfer {TransferId}", transferId);
            try
            {
                await state.CancellationSource.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // CTS was disposed because transfer completed concurrently - safe to ignore
                _logger.LogDebug("Transfer {TransferId} already completed before cancellation", transferId);
            }
        }
    }

    public IReadOnlyList<FileTransferStatus> GetActiveTransfers()
    {
        return _activeTransfers.Values
            .Select(s => new FileTransferStatus
            {
                TransferId = s.TransferId,
                Direction = s.Direction,
                State = s.State,
                Progress = s.Progress,
                ErrorMessage = s.ErrorMessage,
                StartedAt = s.StartedAt,
                UsingP2P = false
            })
            .ToList();
    }

    private sealed class TransferState
    {
        public Guid TransferId { get; init; }
        public FileTransferDirection Direction { get; init; }
        public FileTransferState State { get; set; }
        public FileTransferProgress? Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset StartedAt { get; init; }
        public required CancellationTokenSource CancellationSource { get; init; }
    }
}
