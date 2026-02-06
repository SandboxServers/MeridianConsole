using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;

using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Services;

/// <summary>
/// Interface for managing Windows Services for game server isolation.
/// </summary>
public interface IWindowsServiceManager
{
    /// <summary>
    /// Creates a new Windows Service for a game server.
    /// </summary>
    /// <param name="config">Service configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service info on success.</returns>
    Task<Result<ServiceInfo>> CreateGameServerServiceAsync(
        GameServerServiceConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a game server Windows Service.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> DeleteGameServerServiceAsync(
        string serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a game server Windows Service.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> StartServiceAsync(
        string serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a game server Windows Service.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="timeout">Timeout for graceful stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> StopServiceAsync(
        string serverId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a game server Windows Service.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service status.</returns>
    Task<Result<ServiceStatus>> GetServiceStatusAsync(
        string serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a service exists.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <returns>True if the service exists.</returns>
    bool ServiceExists(string serverId);

    /// <summary>
    /// Cleans up orphaned game server services (services without matching tracked processes).
    /// </summary>
    /// <param name="activeServerIds">Set of currently active server IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of services cleaned up.</returns>
    Task<int> CleanupOrphanedServicesAsync(
        IReadOnlySet<string> activeServerIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages Windows Services for game server process isolation using Virtual Service Accounts.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class creates Windows Services that run as Virtual Service Accounts.
///
/// Security measures:
/// - Service names are validated against strict patterns to prevent injection
/// - Virtual Service Accounts (NT SERVICE\name) are used for automatic credential management
/// - Service binPath is validated and properly quoted
/// - All sc.exe commands use proper argument escaping
/// - Service creation is logged for audit trails
/// </remarks>
public sealed partial class WindowsServiceManager : IWindowsServiceManager
{
    private readonly ILogger<WindowsServiceManager> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Timeout for sc.exe commands.
    /// </summary>
    private static readonly TimeSpan ScCommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the absolute path to sc.exe in System32 to prevent PATH hijacking.
    /// </summary>
    private static readonly string ScExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "sc.exe");

    /// <summary>
    /// Default timeout for service start/stop operations.
    /// </summary>
    private static readonly TimeSpan DefaultServiceTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Pattern for valid server IDs.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ValidServerIdPattern();

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsServiceManager"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Optional time provider for testability.</param>
    public WindowsServiceManager(
        ILogger<WindowsServiceManager> logger,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<ServiceInfo>> CreateGameServerServiceAsync(
        GameServerServiceConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Validate server ID
        var validationResult = ValidateServerId(config.ServerId);
        if (validationResult.IsFailure)
        {
            return Result<ServiceInfo>.Failure(validationResult.Error);
        }

        // Validate all config fields using IValidatableObject
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(config);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            config, validationContext, validationResults, validateAllProperties: true))
        {
            var errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            return Result<ServiceInfo>.Failure($"[Service.InvalidConfig] Configuration validation failed: {errors}");
        }

        var serviceName = config.ServiceName;

        // Check if service already exists
        if (ServiceExists(config.ServerId))
        {
            _logger.LogWarning(
                "Service {ServiceName} already exists, will delete and recreate",
                serviceName);

            var deleteResult = await DeleteGameServerServiceAsync(config.ServerId, cancellationToken)
                .ConfigureAwait(false);

            if (deleteResult.IsFailure)
            {
                return Result<ServiceInfo>.Failure(
                    $"[Service.DeleteFailed] Failed to delete existing service: {deleteResult.Error}");
            }
        }

        // Validate wrapper executable exists
        if (!File.Exists(config.WrapperExecutablePath))
        {
            return Result<ServiceInfo>.Failure(
                $"[Service.WrapperNotFound] Wrapper executable not found: {config.WrapperExecutablePath}");
        }

        // Build the binPath for sc.exe
        // Format: "path\to\wrapper.exe" --server-id={serverId} --pipe={pipeName} --config={configPath}
        var binPath = BuildBinPath(config);

        _logger.LogInformation(
            "Creating Windows Service {ServiceName} with Virtual Service Account",
            serviceName);

        // Create the service using sc.exe
        // Using sc.exe rather than P/Invoke to CreateService for simplicity and maintainability
        var createArgs = new StringBuilder();
        createArgs.Append("create ");
        createArgs.Append(EscapeScArgument(serviceName));
        createArgs.Append(" type= own start= demand error= normal");
        createArgs.Append(" binPath= ");
        createArgs.Append(EscapeScArgument(binPath));

        // Set to run as Virtual Service Account
        // NT SERVICE\{serviceName} is automatically created and managed by Windows
        createArgs.Append(" obj= ");
        createArgs.Append(EscapeScArgument(config.ServiceAccountName));

        var createResult = await RunScCommandAsync(createArgs.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (createResult.IsFailure)
        {
            return Result<ServiceInfo>.Failure(
                $"[Service.CreateFailed] Failed to create service: {createResult.Error}");
        }

        // Set the display name and description
        if (!string.IsNullOrEmpty(config.DisplayName))
        {
            var displayNameResult = await RunScCommandAsync(
                $"config {EscapeScArgument(serviceName)} displayname= {EscapeScArgument(config.DisplayName)}",
                cancellationToken).ConfigureAwait(false);

            if (displayNameResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to set display name for service {ServiceName}: {Error}",
                    serviceName, displayNameResult.Error);
            }
        }

        if (!string.IsNullOrEmpty(config.Description))
        {
            var descResult = await RunScCommandAsync(
                $"description {EscapeScArgument(serviceName)} {EscapeScArgument(config.Description)}",
                cancellationToken).ConfigureAwait(false);

            if (descResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to set description for service {ServiceName}: {Error}",
                    serviceName, descResult.Error);
            }
        }

        // Configure recovery options (restart on failure)
        var recoveryResult = await ConfigureServiceRecoveryAsync(serviceName, cancellationToken)
            .ConfigureAwait(false);

        if (recoveryResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to configure recovery for service {ServiceName}: {Error}",
                serviceName, recoveryResult.Error);
        }

        _logger.LogInformation(
            "Successfully created Windows Service {ServiceName} running as {Account}",
            serviceName, config.ServiceAccountName);

        return Result<ServiceInfo>.Success(new ServiceInfo
        {
            ServiceName = serviceName,
            Status = ServiceStatus.Stopped,
            ServiceAccountName = config.ServiceAccountName,
            ServerDirectory = config.ServerDirectory,
            CreatedAt = _timeProvider.GetUtcNow()
        });
    }

    /// <inheritdoc />
    public async Task<Result> DeleteGameServerServiceAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateServerId(serverId);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var serviceName = $"{GameServerServiceConfig.ServiceNamePrefix}{serverId}";

        // Check if service exists
        if (!ServiceExists(serverId))
        {
            _logger.LogDebug("Service {ServiceName} does not exist, nothing to delete", serviceName);
            return Result.Success();
        }

        // Stop the service if it's running
        var statusResult = await GetServiceStatusAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (statusResult.IsSuccess && statusResult.Value is ServiceStatus.Running or ServiceStatus.Starting)
        {
            _logger.LogInformation("Stopping service {ServiceName} before deletion", serviceName);

            var stopResult = await StopServiceAsync(serverId, DefaultServiceTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (stopResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to stop service {ServiceName} gracefully: {Error}. Will attempt force delete.",
                    serviceName, stopResult.Error);
            }

            // Wait a moment for the service to fully stop
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        // Delete the service
        var deleteResult = await RunScCommandAsync(
            $"delete {EscapeScArgument(serviceName)}",
            cancellationToken).ConfigureAwait(false);

        if (deleteResult.IsFailure)
        {
            return Result.Failure($"[Service.DeleteFailed] Failed to delete service: {deleteResult.Error}");
        }

        _logger.LogInformation("Successfully deleted Windows Service {ServiceName}", serviceName);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> StartServiceAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateServerId(serverId);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var serviceName = $"{GameServerServiceConfig.ServiceNamePrefix}{serverId}";

        if (!ServiceExists(serverId))
        {
            return Result.Failure($"[Service.NotFound] Service {serviceName} does not exist");
        }

        try
        {
            using var controller = new ServiceController(serviceName);

            if (controller.Status == ServiceControllerStatus.Running)
            {
                _logger.LogDebug("Service {ServiceName} is already running", serviceName);
                return Result.Success();
            }

            if (controller.Status == ServiceControllerStatus.StartPending)
            {
                _logger.LogDebug("Service {ServiceName} is already starting", serviceName);
                // Wait for it to finish starting
                controller.WaitForStatus(ServiceControllerStatus.Running, DefaultServiceTimeout);
                return Result.Success();
            }

            _logger.LogInformation("Starting service {ServiceName}", serviceName);

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, DefaultServiceTimeout);

            _logger.LogInformation("Successfully started service {ServiceName}", serviceName);
            return Result.Success();
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return Result.Failure($"[Service.StartTimeout] Timeout waiting for service {serviceName} to start");
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure($"[Service.StartFailed] Failed to start service: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            return Result.Failure($"[Service.StartFailed] Win32 error starting service: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result> StopServiceAsync(
        string serverId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateServerId(serverId);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var serviceName = $"{GameServerServiceConfig.ServiceNamePrefix}{serverId}";

        if (!ServiceExists(serverId))
        {
            return Result.Failure($"[Service.NotFound] Service {serviceName} does not exist");
        }

        try
        {
            using var controller = new ServiceController(serviceName);

            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                _logger.LogDebug("Service {ServiceName} is already stopped", serviceName);
                return Result.Success();
            }

            if (controller.Status == ServiceControllerStatus.StopPending)
            {
                _logger.LogDebug("Service {ServiceName} is already stopping", serviceName);
                controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return Result.Success();
            }

            _logger.LogInformation("Stopping service {ServiceName} with timeout {Timeout}", serviceName, timeout);

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);

            _logger.LogInformation("Successfully stopped service {ServiceName}", serviceName);
            return Result.Success();
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            _logger.LogWarning("Timeout stopping service {ServiceName}, attempting force kill", serviceName);

            // Try to force kill via sc.exe
            var killResult = await RunScCommandAsync(
                $"stop {EscapeScArgument(serviceName)}",
                cancellationToken).ConfigureAwait(false);

            if (killResult.IsFailure)
            {
                return Result.Failure($"[Service.StopTimeout] Failed to stop service {serviceName}");
            }

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure($"[Service.StopFailed] Failed to stop service: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            return Result.Failure($"[Service.StopFailed] Win32 error stopping service: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<Result<ServiceStatus>> GetServiceStatusAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateServerId(serverId);
        if (validationResult.IsFailure)
        {
            return Task.FromResult(Result<ServiceStatus>.Failure(validationResult.Error));
        }

        var serviceName = $"{GameServerServiceConfig.ServiceNamePrefix}{serverId}";

        try
        {
            using var controller = new ServiceController(serviceName);
            var status = MapServiceStatus(controller.Status);
            return Task.FromResult(Result<ServiceStatus>.Success(status));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(Result<ServiceStatus>.Success(ServiceStatus.NotInstalled));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
        {
            return Task.FromResult(Result<ServiceStatus>.Success(ServiceStatus.NotInstalled));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<ServiceStatus>.Failure(
                $"[Service.StatusError] Failed to get service status: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public bool ServiceExists(string serverId)
    {
        var validationResult = ValidateServerId(serverId);
        if (validationResult.IsFailure)
        {
            return false;
        }

        var serviceName = $"{GameServerServiceConfig.ServiceNamePrefix}{serverId}";

        try
        {
            using var controller = new ServiceController(serviceName);
            // Accessing Status will throw if service doesn't exist
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid service name
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupOrphanedServicesAsync(
        IReadOnlySet<string> activeServerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeServerIds);

        var cleanedUp = 0;

        try
        {
            // Get all services with our prefix
            var services = ServiceController.GetServices()
                .Where(s => s.ServiceName.StartsWith(
                    GameServerServiceConfig.ServiceNamePrefix,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var service in services)
            {
                try
                {
                    // Extract server ID from service name
                    var serverId = service.ServiceName[GameServerServiceConfig.ServiceNamePrefix.Length..];

                    if (!activeServerIds.Contains(serverId))
                    {
                        _logger.LogInformation(
                            "Cleaning up orphaned service {ServiceName}",
                            service.ServiceName);

                        var deleteResult = await DeleteGameServerServiceAsync(serverId, cancellationToken)
                            .ConfigureAwait(false);

                        if (deleteResult.IsSuccess)
                        {
                            cleanedUp++;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Failed to cleanup orphaned service {ServiceName}: {Error}",
                                service.ServiceName, deleteResult.Error);
                        }
                    }
                }
                finally
                {
                    service.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during orphaned service cleanup");
        }

        if (cleanedUp > 0)
        {
            _logger.LogInformation("Cleaned up {Count} orphaned game server services", cleanedUp);
        }

        return cleanedUp;
    }

    #region Private Methods

    /// <summary>
    /// Validates a server ID for security.
    /// </summary>
    private static Result ValidateServerId(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return Result.Failure("[Service.InvalidServerId] Server ID is required");
        }

        if (serverId.Length > 200)
        {
            return Result.Failure("[Service.InvalidServerId] Server ID exceeds maximum length");
        }

        if (!ValidServerIdPattern().IsMatch(serverId))
        {
            return Result.Failure(
                "[Service.InvalidServerId] Server ID contains invalid characters. " +
                "Only alphanumeric, hyphen, and underscore are allowed.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Builds the binPath argument for sc.exe create.
    /// </summary>
    private static string BuildBinPath(GameServerServiceConfig config)
    {
        var builder = new StringBuilder();

        // Quote the executable path
        builder.Append('"');
        builder.Append(config.WrapperExecutablePath);
        builder.Append('"');

        // Add arguments
        builder.Append(" --server-id=");
        builder.Append(config.ServerId);

        builder.Append(" --pipe=");
        builder.Append(config.PipeName);

        builder.Append(" --config=");
        builder.Append('"');
        builder.Append(config.ConfigFilePath);
        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>
    /// Escapes an argument for sc.exe.
    /// </summary>
    private static string EscapeScArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        // If the value contains spaces or special characters, quote it
        if (value.Contains(' ', StringComparison.Ordinal) ||
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal))
        {
            // Escape internal quotes and wrap in quotes
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    /// <summary>
    /// Runs an sc.exe command and returns the result.
    /// </summary>
    private async Task<Result> RunScCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running: sc.exe {Arguments}", arguments);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ScExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ScCommandTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best effort
                }

                return Result.Failure("[Service.Timeout] sc.exe command timed out");
            }

            var exitCode = process.ExitCode;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (exitCode != 0)
            {
                var errorMessage = !string.IsNullOrWhiteSpace(error) ? error : output;
                _logger.LogWarning(
                    "sc.exe command failed with exit code {ExitCode}: {Error}",
                    exitCode, errorMessage);

                return Result.Failure(FormattableString.Invariant($"sc.exe failed (exit code {exitCode}): {errorMessage.Trim()}"));
            }

            _logger.LogDebug("sc.exe command succeeded: {Output}", output.Trim());
            return Result.Success();
        }
        catch (Win32Exception ex)
        {
            return Result.Failure($"[Service.ScExeError] Failed to run sc.exe: {ex.Message}");
        }
    }

    /// <summary>
    /// Configures service recovery options.
    /// </summary>
    private async Task<Result> ConfigureServiceRecoveryAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        // Configure progressive recovery delays: 5s, 10s, 30s
        // Reset counter after 24 hours
        var recoveryArgs = $"failure {EscapeScArgument(serviceName)} reset= 86400 actions= restart/5000/restart/10000/restart/30000";

        return await RunScCommandAsync(recoveryArgs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps ServiceControllerStatus to our ServiceStatus enum.
    /// </summary>
    private static ServiceStatus MapServiceStatus(ServiceControllerStatus status)
    {
        return status switch
        {
            ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
            ServiceControllerStatus.StartPending => ServiceStatus.Starting,
            ServiceControllerStatus.StopPending => ServiceStatus.Stopping,
            ServiceControllerStatus.Running => ServiceStatus.Running,
            ServiceControllerStatus.ContinuePending => ServiceStatus.Starting,
            ServiceControllerStatus.PausePending => ServiceStatus.Stopping,
            ServiceControllerStatus.Paused => ServiceStatus.Paused,
            _ => ServiceStatus.Unknown
        };
    }

    #endregion
}
