using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows implementation of process management using Job Objects for process isolation.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class manages game server processes on customer hardware.
///
/// Security measures implemented:
/// - Job Objects with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE to prevent orphan processes
/// - ArgumentList instead of Arguments string to prevent command injection
/// - Output line truncation to 64KB to prevent memory exhaustion
/// - Path validation before executable launch
/// - Proper disposal with _disposed flag set FIRST
/// - ConcurrentDictionary for thread-safe process tracking
/// - LinkedCancellationTokenSource for disposal propagation
///
/// All paths must be validated before use. Executables must exist and be within allowed directories.
/// </remarks>
public sealed class WindowsProcessManager : IProcessManager, IDisposable
{
    private readonly ILogger<WindowsProcessManager> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, ProcessEntry> _processes = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly object _disposalLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// Maximum allowed output line length (64KB) to prevent memory exhaustion.
    /// Lines exceeding this length will be truncated.
    /// </summary>
    private const int MaxOutputLineLength = 64 * 1024;

    /// <summary>
    /// Exit code used when forcibly terminating a job object.
    /// </summary>
    private const uint ForcedTerminationExitCode = 0xDEAD;

    /// <summary>
    /// Exit code used when terminating due to memory limit exceeded.
    /// </summary>
    private const uint MemoryLimitExceededExitCode = 0xDEAE;

    /// <summary>
    /// Default graceful shutdown timeout if not specified.
    /// </summary>
    private static readonly TimeSpan DefaultGracefulTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc />
    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsProcessManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="timeProvider">Optional time provider for testability.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public WindowsProcessManager(
        ILogger<WindowsProcessManager> logger,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// CA2000 is suppressed because ownership of jobHandle and osProcess
    /// is transferred to the ProcessEntry stored in _processes dictionary on success,
    /// or cleaned up via CleanupFailedStart on all failure paths.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership transferred to ProcessEntry on success, cleaned up on failure paths")]
    public async Task<Result<ManagedProcess>> StartProcessAsync(
        ProcessConfig config,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<ManagedProcess>.Failure("[Process.Disposed] Process manager has been disposed");
        }

        ArgumentNullException.ThrowIfNull(config);

        // SECURITY: Validate executable path
        var pathValidation = ValidateExecutablePath(config.ExecutablePath);
        if (pathValidation.IsFailure)
        {
            return Result<ManagedProcess>.Failure(pathValidation.Error);
        }

        // SECURITY: Validate working directory if specified, get normalized path
        string? validatedWorkingDirectory = null;
        if (!string.IsNullOrEmpty(config.WorkingDirectory))
        {
            var workDirValidation = ValidateWorkingDirectory(config.WorkingDirectory);
            if (workDirValidation.IsFailure)
            {
                return Result<ManagedProcess>.Failure(workDirValidation.Error);
            }
            validatedWorkingDirectory = workDirValidation.Value;
        }

        // Link the cancellation token with disposal
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);

        var processId = Guid.NewGuid();
        JobObjectHandle? jobHandle = null;
        System.Diagnostics.Process? osProcess = null;

        try
        {
            // Create Job Object for process isolation
            jobHandle = CreateConfiguredJobObject(config, processId);
            if (jobHandle is null || jobHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError(
                    "Failed to create Job Object for process {ProcessId}. Win32 error: {Error}",
                    processId, error);
                return Result<ManagedProcess>.Failure(
                    $"[Process.JobObjectCreationFailed] Failed to create Job Object. Win32 error: {error}");
            }

            // Configure process start info (use validated working directory)
            var startInfo = CreateProcessStartInfo(config, validatedWorkingDirectory);

            // Create and start the process
            osProcess = new System.Diagnostics.Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            // Wire up event handlers before starting
            var managedProcess = new ManagedProcess
            {
                ProcessId = processId,
                ServerId = config.ServerId,
                Config = config,
                State = ProcessState.Starting,
                StartedAt = _timeProvider.GetUtcNow()
            };

            var entry = new ProcessEntry(
                managedProcess,
                osProcess,
                jobHandle,
                config);

            // Add to tracking before starting (so events can find it)
            if (!_processes.TryAdd(processId, entry))
            {
                _logger.LogError("Failed to add process {ProcessId} to tracking dictionary", processId);
                CleanupFailedStart(processId, osProcess, jobHandle);
                // Set to null after cleanup to prevent double-dispose in outer catch
                osProcess = null;
                jobHandle = null;
                return Result<ManagedProcess>.Failure(
                    "[Process.TrackingFailed] Failed to track process");
            }

            // Wire up output handlers
            if (config.CaptureStdout)
            {
                osProcess.OutputDataReceived += (_, e) => HandleOutputData(processId, e.Data, isError: false);
            }

            if (config.CaptureStderr)
            {
                osProcess.ErrorDataReceived += (_, e) => HandleOutputData(processId, e.Data, isError: true);
            }

            // Wire up exit handler
            osProcess.Exited += (_, _) => HandleProcessExited(processId);

            // Start the process
            if (!osProcess.Start())
            {
                _processes.TryRemove(processId, out _);
                CleanupFailedStart(processId, osProcess, jobHandle);
                // Set to null after cleanup to prevent double-dispose in outer catch
                osProcess = null;
                jobHandle = null;
                return Result<ManagedProcess>.Failure(
                    "[Process.StartFailed] Failed to start process");
            }

            // CRITICAL: Assign to Job Object IMMEDIATELY after start
            // This must happen before any child processes can be spawned
            if (!NativeMethods.AssignProcessToJobObject(jobHandle.DangerousGetHandle(), osProcess.Handle))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError(
                    "Failed to assign process {OsPid} to Job Object. Win32 error: {Error}",
                    osProcess.Id, error);

                // Kill the process since we couldn't contain it
                try { osProcess.Kill(); } catch { /* Best effort */ }
                _processes.TryRemove(processId, out _);
                CleanupFailedStart(processId, osProcess, jobHandle);
                // Set to null after cleanup to prevent double-dispose in outer catch
                osProcess = null;
                jobHandle = null;

                return Result<ManagedProcess>.Failure(
                    $"[Process.JobAssignmentFailed] Failed to assign process to Job Object. Win32 error: {error}");
            }

            // Set up memory limit monitoring if KillOnMemoryExceeded is enabled
            // This must be done after the process is assigned to the job
            if (config.Limits?.MemoryMb > 0 && config.Limits.KillOnMemoryExceeded)
            {
                if (!SetupMemoryLimitMonitoring(entry, processId))
                {
                    _logger.LogWarning(
                        "Memory limit monitoring could not be set up for process {ProcessId}. " +
                        "Memory limits are still enforced, but processes will not be automatically terminated on violation.",
                        processId);
                }
            }

            // Update process info
            managedProcess.OsPid = osProcess.Id;
            managedProcess.State = ProcessState.Running;

            // Begin async output reading
            if (config.CaptureStdout)
            {
                osProcess.BeginOutputReadLine();
            }

            if (config.CaptureStderr)
            {
                osProcess.BeginErrorReadLine();
            }

            _logger.LogInformation(
                "Started process {ProcessId} (PID: {OsPid}) for server {ServerId}",
                processId, osProcess.Id, config.ServerId);

            // Ownership transferred to ProcessEntry in _processes dictionary
            // Set to null to prevent cleanup in case of any future exception paths
            osProcess = null;
            jobHandle = null;

            return Result<ManagedProcess>.Success(managedProcess);
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Win32 error starting process for server {ServerId}", config.ServerId);
            CleanupFailedStart(processId, osProcess, jobHandle);
            return Result<ManagedProcess>.Failure(
                $"[Process.Win32Error] Failed to start process: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // IMPORTANT: ObjectDisposedException must be caught before InvalidOperationException
            // since ObjectDisposedException inherits from InvalidOperationException
            CleanupFailedStart(processId, osProcess, jobHandle);
            return Result<ManagedProcess>.Failure(
                "[Process.Disposed] Process manager was disposed during startup");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation starting process for server {ServerId}", config.ServerId);
            CleanupFailedStart(processId, osProcess, jobHandle);
            return Result<ManagedProcess>.Failure(
                $"[Process.InvalidOperation] Failed to start process: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> StopProcessAsync(
        Guid processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager has been disposed");
        }

        if (!_processes.TryGetValue(processId, out var entry))
        {
            return Result<bool>.Failure($"[Process.NotFound] Process {processId} not found");
        }

        // Link cancellation token with disposal
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);

        entry.ManagedProcess.State = ProcessState.Stopping;

        try
        {
            var osProcess = entry.OsProcess;

            // Check if already exited
            if (osProcess.HasExited)
            {
                entry.ManagedProcess.State = ProcessState.Stopped;
                entry.ManagedProcess.ExitCode = osProcess.ExitCode;
                entry.ManagedProcess.ExitedAt = _timeProvider.GetUtcNow();
                return Result<bool>.Success(true);
            }

            // Try graceful shutdown first - send close to main window
            var gracefulTimeout = timeout > TimeSpan.Zero ? timeout : DefaultGracefulTimeout;

            _logger.LogInformation(
                "Attempting graceful shutdown of process {ProcessId} (PID: {OsPid}) with timeout {Timeout}",
                processId, entry.ManagedProcess.OsPid, gracefulTimeout);

            // Try CloseMainWindow for GUI apps
            try
            {
                if (osProcess.MainWindowHandle != IntPtr.Zero)
                {
                    osProcess.CloseMainWindow();
                }
            }
            catch (InvalidOperationException)
            {
                // Process may have already exited
            }

            // Wait for graceful exit
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token);
                timeoutCts.CancelAfter(gracefulTimeout);

                await osProcess.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                entry.ManagedProcess.State = ProcessState.Stopped;
                entry.ManagedProcess.ExitCode = osProcess.ExitCode;
                entry.ManagedProcess.ExitedAt = _timeProvider.GetUtcNow();

                _logger.LogInformation(
                    "Process {ProcessId} exited gracefully with code {ExitCode}",
                    processId, osProcess.ExitCode);

                return Result<bool>.Success(true);
            }
            catch (OperationCanceledException) when (!linkedCts.Token.IsCancellationRequested)
            {
                // Graceful timeout expired, proceed to force kill
                _logger.LogWarning(
                    "Graceful shutdown timeout for process {ProcessId}, forcing termination",
                    processId);
            }

            // Force terminate via Job Object
            return await ForceTerminateAsync(processId, entry, linkedCts.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return Result<bool>.Failure("[Process.Disposed] Process resources were disposed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Process {ProcessId} may have already exited", processId);
            return Result<bool>.Success(true);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> KillProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager has been disposed");
        }

        if (!_processes.TryGetValue(processId, out var entry))
        {
            return Result<bool>.Failure($"[Process.NotFound] Process {processId} not found");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);

        return await ForceTerminateAsync(processId, entry, linkedCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ManagedProcess? GetProcess(Guid processId)
    {
        if (_disposed)
        {
            return null;
        }

        return _processes.TryGetValue(processId, out var entry) ? entry.ManagedProcess : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ManagedProcess> GetAllProcesses()
    {
        if (_disposed)
        {
            return [];
        }

        return _processes.Values.Select(e => e.ManagedProcess).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateResourceLimitsAsync(
        Guid processId,
        ResourceLimits limits,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager has been disposed");
        }

        ArgumentNullException.ThrowIfNull(limits);

        if (!_processes.TryGetValue(processId, out var entry))
        {
            return Result<bool>.Failure($"[Process.NotFound] Process {processId} not found");
        }

        try
        {
            // Update memory limits
            if (limits.MemoryMb.HasValue)
            {
                if (limits.MemoryMb.Value <= 0)
                {
                    return Result<bool>.Failure(
                        "[Process.InvalidResourceLimits] MemoryMb must be greater than 0");
                }

                var memoryResult = SetMemoryLimits(entry.JobHandle, limits.MemoryMb.Value, limits.KillOnMemoryExceeded);
                if (memoryResult.IsFailure)
                {
                    return memoryResult;
                }
            }

            // Update CPU limits
            if (limits.CpuPercent.HasValue)
            {
                if (limits.CpuPercent.Value <= 0)
                {
                    return Result<bool>.Failure(
                        "[Process.InvalidResourceLimits] CpuPercent must be greater than 0");
                }

                var cpuResult = SetCpuLimits(entry.JobHandle, limits.CpuPercent.Value);
                if (cpuResult.IsFailure)
                {
                    return cpuResult;
                }
            }

            _logger.LogInformation(
                "Updated resource limits for process {ProcessId}: CPU={CpuPercent}%, Memory={MemoryMb}MB",
                processId, limits.CpuPercent, limits.MemoryMb);

            return Result<bool>.Success(true);
        }
        catch (ObjectDisposedException)
        {
            return Result<bool>.Failure("[Process.Disposed] Job object was disposed");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // SECURITY: Set _disposed FIRST to prevent any new operations
        lock (_disposalLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        _logger.LogInformation("Disposing WindowsProcessManager, terminating all processes");

        // Signal disposal to any waiting operations
        try
        {
            _disposalCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        // Terminate all tracked processes via their Job Objects
        foreach (var kvp in _processes)
        {
            var entry = kvp.Value;
            try
            {
                // Terminate the Job Object - this kills all processes in the job
                if (!entry.JobHandle.IsInvalid && !entry.JobHandle.IsClosed)
                {
                    NativeMethods.TerminateJobObject(
                        entry.JobHandle.DangerousGetHandle(),
                        ForcedTerminationExitCode);
                }

                // Dispose resources (including monitoring task, completion port, job handle, and process)
                entry.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing process entry {ProcessId}", kvp.Key);
            }
        }

        _processes.Clear();

        try
        {
            _disposalCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        _logger.LogInformation("WindowsProcessManager disposed");
    }

    #region Private Methods

    /// <summary>
    /// Creates and configures a Job Object with the specified limits.
    /// </summary>
    /// <remarks>
    /// When KillOnMemoryExceeded is true, this method sets up memory limits
    /// but does NOT set up the IO completion port monitoring. That must be
    /// done separately via <see cref="SetupMemoryLimitMonitoringAsync"/> after
    /// the process entry is created and tracked.
    /// </remarks>
    private JobObjectHandle? CreateConfiguredJobObject(ProcessConfig config, Guid processId)
    {
        // Create an anonymous Job Object
        var handle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var jobHandle = new JobObjectHandle(handle, ownsHandle: true);

        try
        {
            // Set basic limits - always enable KILL_ON_JOB_CLOSE to prevent orphans
            var extendedInfo = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = NativeMethods.JobObjectLimit.KillOnJobClose
                }
            };

            // Configure memory limits if specified
            // Note: KillOnMemoryExceeded is handled via IO completion port monitoring,
            // not via DieOnUnhandledException (which doesn't actually enforce memory termination)
            if (config.Limits?.MemoryMb > 0)
            {
                var memoryBytes = (nuint)(config.Limits.MemoryMb.Value * 1024L * 1024L);
                extendedInfo.BasicLimitInformation.LimitFlags |= NativeMethods.JobObjectLimit.ProcessMemory;
                extendedInfo.ProcessMemoryLimit = memoryBytes;
            }

            // Configure max active processes if specified
            if (config.Limits?.MaxChildProcesses > 0)
            {
                extendedInfo.BasicLimitInformation.LimitFlags |= NativeMethods.JobObjectLimit.ActiveProcess;
                extendedInfo.BasicLimitInformation.ActiveProcessLimit = (uint)config.Limits.MaxChildProcesses.Value;
            }

            // Apply extended limits
            var infoSize = (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var infoPtr = Marshal.AllocHGlobal((int)infoSize);

            try
            {
                Marshal.StructureToPtr(extendedInfo, infoPtr, fDeleteOld: false);

                if (!NativeMethods.SetInformationJobObject(
                    jobHandle.DangerousGetHandle(),
                    NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
                    infoPtr,
                    infoSize))
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning(
                        "Failed to set extended limits on Job Object for process {ProcessId}. Win32 error: {Error}",
                        processId, error);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }

            // Configure CPU limits if specified
            if (config.Limits?.CpuPercent > 0)
            {
                var cpuResult = SetCpuLimits(jobHandle, config.Limits.CpuPercent.Value);
                if (cpuResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to set CPU limits for process {ProcessId}: {Error}",
                        processId, cpuResult.Error);
                }
            }

            return jobHandle;
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a ProcessStartInfo with security-safe configuration.
    /// </summary>
    /// <param name="config">The process configuration.</param>
    /// <param name="validatedWorkingDirectory">The validated, normalized working directory path (or null to use executable's directory).</param>
    private static ProcessStartInfo CreateProcessStartInfo(ProcessConfig config, string? validatedWorkingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            WorkingDirectory = validatedWorkingDirectory ?? Path.GetDirectoryName(config.ExecutablePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = config.CaptureStdout,
            RedirectStandardError = config.CaptureStderr,
            RedirectStandardInput = false
        };

        // SECURITY: Use ArgumentList instead of Arguments to prevent command injection
        // Arguments are passed as separate array elements, not shell-interpreted
        if (!string.IsNullOrWhiteSpace(config.Arguments))
        {
            // Parse arguments respecting quotes
            var args = ParseArguments(config.Arguments);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        // Set environment variables
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    /// <summary>
    /// Parses a command line string into individual arguments using the native Windows API.
    /// </summary>
    /// <remarks>
    /// SECURITY: Uses CommandLineToArgvW which is the proper Windows API for parsing command lines.
    /// It handles all edge cases including quoted strings, escaped characters, and special characters
    /// correctly, matching how Windows itself parses command lines.
    /// </remarks>
    private static List<string> ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var argvPtr = NativeMethods.CommandLineToArgvW(commandLine, out var argc);

        if (argvPtr == IntPtr.Zero)
        {
            // If the native API fails, fall back to basic splitting
            // This should rarely happen, but provides a safety net
            return [.. commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        }

        try
        {
            var args = new List<string>(argc);

            for (var i = 0; i < argc; i++)
            {
                var argPtr = Marshal.ReadIntPtr(argvPtr, i * IntPtr.Size);
                var arg = Marshal.PtrToStringUni(argPtr);
                if (arg is not null)
                {
                    args.Add(arg);
                }
            }

            return args;
        }
        finally
        {
            // SECURITY: Always free the memory allocated by CommandLineToArgvW
            NativeMethods.LocalFree(argvPtr);
        }
    }

    /// <summary>
    /// Sets memory limits on a Job Object.
    /// </summary>
    /// <remarks>
    /// Note: The killOnExceeded parameter is no longer used here. Memory limit enforcement
    /// with termination is handled via IO completion port monitoring in SetupMemoryLimitMonitoring.
    /// This method only sets the memory limit on the job object, which will cause allocation
    /// failures when exceeded (but not automatic termination).
    /// </remarks>
    private static Result<bool> SetMemoryLimits(JobObjectHandle jobHandle, int memoryMb, bool killOnExceeded)
    {
        // Note: killOnExceeded is handled separately via IO completion port monitoring
        // The DieOnUnhandledException flag was previously used here incorrectly -
        // it controls error dialogs, not memory enforcement

        var extendedInfo = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JobObjectLimit.KillOnJobClose |
                            NativeMethods.JobObjectLimit.ProcessMemory
            },
            ProcessMemoryLimit = (nuint)(memoryMb * 1024L * 1024L)
        };

        var infoSize = (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var infoPtr = Marshal.AllocHGlobal((int)infoSize);

        try
        {
            Marshal.StructureToPtr(extendedInfo, infoPtr, fDeleteOld: false);

            if (!NativeMethods.SetInformationJobObject(
                jobHandle.DangerousGetHandle(),
                NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
                infoPtr,
                infoSize))
            {
                var error = Marshal.GetLastWin32Error();
                return Result<bool>.Failure(
                    $"[Process.SetMemoryLimitsFailed] Failed to set memory limits. Win32 error: {error}");
            }

            return Result<bool>.Success(true);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// Sets CPU rate limits on a Job Object.
    /// </summary>
    private static Result<bool> SetCpuLimits(JobObjectHandle jobHandle, int cpuPercent)
    {
        // CPU rate is specified as 1-10000 where 10000 = 100%
        // Our input is 1-100, so multiply by 100
        var cpuRate = (uint)Math.Clamp(cpuPercent * 100, 100, 10000);

        var cpuInfo = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = NativeMethods.CpuRateControlFlags.Enable | NativeMethods.CpuRateControlFlags.HardCap,
            CpuRate = cpuRate
        };

        var infoSize = (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        var infoPtr = Marshal.AllocHGlobal((int)infoSize);

        try
        {
            Marshal.StructureToPtr(cpuInfo, infoPtr, fDeleteOld: false);

            if (!NativeMethods.SetInformationJobObject(
                jobHandle.DangerousGetHandle(),
                NativeMethods.JobObjectInfoType.CpuRateControlInformation,
                infoPtr,
                infoSize))
            {
                var error = Marshal.GetLastWin32Error();
                return Result<bool>.Failure(
                    $"[Process.SetCpuLimitsFailed] Failed to set CPU limits. Win32 error: {error}");
            }

            return Result<bool>.Success(true);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// Sets up IO completion port monitoring for memory limit enforcement.
    /// </summary>
    /// <param name="entry">The process entry to monitor.</param>
    /// <param name="processId">The process ID for logging.</param>
    /// <returns>True if monitoring was set up successfully, false otherwise.</returns>
    /// <remarks>
    /// SECURITY: This method creates an IO completion port associated with the job object
    /// to receive notifications when the memory limit is exceeded. When a
    /// JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT message is received, the job is terminated.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of IoCompletionPortHandle is transferred to ProcessEntry.CompletionPortHandle on success, or disposed on failure paths")]
    private bool SetupMemoryLimitMonitoring(ProcessEntry entry, Guid processId)
    {
        // Create the IO completion port
        var completionPortHandle = NativeMethods.CreateIoCompletionPort(
            NativeMethods.InvalidHandleValue,
            IntPtr.Zero,
            (nuint)processId.GetHashCode(), // Use process ID hash as completion key
            1); // Single thread for monitoring

        if (completionPortHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "Failed to create IO completion port for memory monitoring on process {ProcessId}. Win32 error: {Error}",
                processId, error);
            return false;
        }

        var ioCompletionPort = new IoCompletionPortHandle(completionPortHandle, ownsHandle: true);

        try
        {
            // Associate the completion port with the job object
            var associateInfo = new NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT
            {
                CompletionKey = (nuint)processId.GetHashCode(),
                CompletionPort = ioCompletionPort.DangerousGetHandle()
            };

            var infoSize = (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT>();
            var infoPtr = Marshal.AllocHGlobal((int)infoSize);

            try
            {
                Marshal.StructureToPtr(associateInfo, infoPtr, fDeleteOld: false);

                if (!NativeMethods.SetInformationJobObject(
                    entry.JobHandle.DangerousGetHandle(),
                    NativeMethods.JobObjectInfoType.AssociateCompletionPortInformation,
                    infoPtr,
                    infoSize))
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning(
                        "Failed to associate completion port with job for process {ProcessId}. Win32 error: {Error}",
                        processId, error);
                    ioCompletionPort.Dispose();
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }

            // Store the completion port handle in the entry
            entry.CompletionPortHandle = ioCompletionPort;

            // Create cancellation token source for the monitoring task
            entry.MonitoringCts = new CancellationTokenSource();

            // Start the background monitoring task
            entry.MonitoringTask = Task.Run(
                () => MonitorMemoryLimitAsync(entry, processId, entry.MonitoringCts.Token),
                entry.MonitoringCts.Token);

            _logger.LogDebug(
                "Memory limit monitoring started for process {ProcessId}",
                processId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Exception setting up memory limit monitoring for process {ProcessId}",
                processId);
            ioCompletionPort.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Background task that monitors the IO completion port for memory limit violations.
    /// </summary>
    /// <param name="entry">The process entry being monitored.</param>
    /// <param name="processId">The process ID for logging.</param>
    /// <param name="cancellationToken">Token to signal monitoring should stop.</param>
    private async Task MonitorMemoryLimitAsync(
        ProcessEntry entry,
        Guid processId,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Use Task.Run to avoid blocking the thread pool on the P/Invoke call
                var result = await Task.Run(() =>
                {
                    if (entry.CompletionPortHandle is null ||
                        entry.CompletionPortHandle.IsInvalid ||
                        entry.CompletionPortHandle.IsClosed)
                    {
                        return (Success: false, MessageType: 0u, OverlappedValue: IntPtr.Zero);
                    }

                    // Wait for completion port notification with 1 second timeout
                    // This allows periodic checking of the cancellation token
                    var success = NativeMethods.GetQueuedCompletionStatus(
                        entry.CompletionPortHandle.DangerousGetHandle(),
                        out var messageType,
                        out _,
                        out var overlapped,
                        1000); // 1 second timeout

                    return (Success: success, MessageType: messageType, OverlappedValue: overlapped);
                }, cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    // Timeout or error - continue loop to check cancellation
                    continue;
                }

                // Check for shutdown message
                if (result.MessageType == NativeMethods.JobObjectMsg.ShutdownMonitor)
                {
                    _logger.LogDebug(
                        "Memory limit monitor received shutdown signal for process {ProcessId}",
                        processId);
                    break;
                }

                // Check for memory limit exceeded
                if (result.MessageType == NativeMethods.JobObjectMsg.ProcessMemoryLimit ||
                    result.MessageType == NativeMethods.JobObjectMsg.JobMemoryLimit)
                {
                    var affectedPid = result.OverlappedValue.ToInt64();
                    _logger.LogWarning(
                        "Memory limit exceeded for process {ProcessId} (affected PID: {AffectedPid}). Terminating job.",
                        processId, affectedPid);

                    // Terminate the job
                    if (!entry.JobHandle.IsInvalid && !entry.JobHandle.IsClosed)
                    {
                        NativeMethods.TerminateJobObject(
                            entry.JobHandle.DangerousGetHandle(),
                            MemoryLimitExceededExitCode);

                        // Update the process state
                        entry.ManagedProcess.State = ProcessState.Failed;
                        entry.ManagedProcess.ExitedAt = _timeProvider.GetUtcNow();
                        entry.ManagedProcess.ExitCode = (int)MemoryLimitExceededExitCode;

                        _logger.LogInformation(
                            "Process {ProcessId} terminated due to memory limit violation",
                            processId);
                    }

                    break;
                }

                // Log other job object messages for debugging
                if (result.MessageType == NativeMethods.JobObjectMsg.NewProcess)
                {
                    _logger.LogDebug(
                        "New process assigned to job for {ProcessId}: PID {Pid}",
                        processId, result.OverlappedValue.ToInt64());
                }
                else if (result.MessageType == NativeMethods.JobObjectMsg.ExitProcess ||
                         result.MessageType == NativeMethods.JobObjectMsg.AbnormalExitProcess)
                {
                    _logger.LogDebug(
                        "Process exited from job {ProcessId}: PID {Pid}, Abnormal: {Abnormal}",
                        processId,
                        result.OverlappedValue.ToInt64(),
                        result.MessageType == NativeMethods.JobObjectMsg.AbnormalExitProcess);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            _logger.LogDebug(
                "Memory limit monitoring cancelled for process {ProcessId}",
                processId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in memory limit monitoring for process {ProcessId}",
                processId);
        }
    }

    /// <summary>
    /// Force terminates a process via its Job Object.
    /// </summary>
    private async Task<Result<bool>> ForceTerminateAsync(
        Guid processId,
        ProcessEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            entry.ManagedProcess.State = ProcessState.Stopping;

            _logger.LogWarning(
                "Force terminating process {ProcessId} (PID: {OsPid}) via Job Object",
                processId, entry.ManagedProcess.OsPid);

            // Terminate via Job Object - this kills all processes in the job
            if (!entry.JobHandle.IsInvalid && !entry.JobHandle.IsClosed)
            {
                if (!NativeMethods.TerminateJobObject(
                    entry.JobHandle.DangerousGetHandle(),
                    ForcedTerminationExitCode))
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning(
                        "TerminateJobObject failed for process {ProcessId}. Win32 error: {Error}",
                        processId, error);

                    // Fall back to direct process kill
                    try
                    {
                        if (!entry.OsProcess.HasExited)
                        {
                            entry.OsProcess.Kill(entireProcessTree: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to kill process tree for {ProcessId}", processId);
                    }
                }
            }

            // Wait briefly for exit
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                await entry.OsProcess.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout waiting for exit, but we've done what we can
            }

            entry.ManagedProcess.State = ProcessState.Stopped;
            entry.ManagedProcess.ExitedAt = _timeProvider.GetUtcNow();

            try
            {
                if (entry.OsProcess.HasExited)
                {
                    entry.ManagedProcess.ExitCode = entry.OsProcess.ExitCode;
                }
            }
            catch (InvalidOperationException)
            {
                // Process info no longer available
            }

            _logger.LogInformation(
                "Process {ProcessId} terminated with exit code {ExitCode}",
                processId, entry.ManagedProcess.ExitCode ?? (int)ForcedTerminationExitCode);

            return Result<bool>.Success(true);
        }
        catch (ObjectDisposedException)
        {
            return Result<bool>.Failure("[Process.Disposed] Process resources were disposed");
        }
    }

    /// <summary>
    /// Handles process output data with truncation for memory safety.
    /// </summary>
    private void HandleOutputData(Guid processId, string? data, bool isError)
    {
        if (string.IsNullOrEmpty(data) || _disposed)
        {
            return;
        }

        // SECURITY: Truncate output to prevent memory exhaustion
        var truncatedData = data.Length > MaxOutputLineLength
            ? data[..MaxOutputLineLength] + "... [TRUNCATED]"
            : data;

        try
        {
            OutputReceived?.Invoke(this, new ProcessOutputEventArgs
            {
                ProcessId = processId,
                Data = truncatedData,
                IsError = isError,
                Timestamp = _timeProvider.GetUtcNow()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invoking OutputReceived event for process {ProcessId}", processId);
        }
    }

    /// <summary>
    /// Handles process exit events.
    /// </summary>
    private void HandleProcessExited(Guid processId)
    {
        if (_disposed)
        {
            return;
        }

        if (!_processes.TryGetValue(processId, out var entry))
        {
            return;
        }

        var exitTime = _timeProvider.GetUtcNow();
        var exitCode = 0;
        var wasKilled = false;

        try
        {
            exitCode = entry.OsProcess.ExitCode;
            // Treat both forced termination (0xDEAD) and memory limit exceeded (0xDEAE) as killed
            wasKilled = exitCode == (int)ForcedTerminationExitCode || exitCode == (int)MemoryLimitExceededExitCode;
        }
        catch (InvalidOperationException)
        {
            // Process info no longer available
        }

        // Preserve existing Failed state (e.g., from memory limit kill) or set based on wasKilled
        entry.ManagedProcess.State = entry.ManagedProcess.State == ProcessState.Failed || wasKilled
            ? ProcessState.Failed
            : ProcessState.Stopped;
        entry.ManagedProcess.ExitedAt = exitTime;
        entry.ManagedProcess.ExitCode = exitCode;

        _logger.LogInformation(
            "Process {ProcessId} (PID: {OsPid}) exited with code {ExitCode} (WasKilled: {WasKilled})",
            processId, entry.ManagedProcess.OsPid, exitCode, wasKilled);

        try
        {
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs
            {
                ProcessId = processId,
                ExitCode = exitCode,
                ExitTime = exitTime,
                WasKilled = wasKilled
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invoking ProcessExited event for process {ProcessId}", processId);
        }

        // Handle auto-restart if configured
        if (entry.Config.AutoRestart && !wasKilled && entry.ManagedProcess.RestartCount < entry.Config.MaxRestartAttempts)
        {
            _ = HandleAutoRestartAsync(processId, entry);
        }
        else
        {
            // Clean up exited process resources when not auto-restarting
            // Use try-finally pattern to ensure disposal on all paths (CA2000)
            ProcessEntry? removedEntry = null;
            try
            {
                if (_processes.TryRemove(processId, out var removed))
                {
                    removedEntry = removed;
                    removedEntry.Dispose();
                    removedEntry = null; // Indicate successful disposal
                }
            }
            finally
            {
                // Dispose if not already disposed in try block
                if (removedEntry is not null)
                {
                    try
                    {
                        removedEntry.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed
                    }
                }
            }
        }
    }

    /// <summary>
    /// Handles automatic process restart.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: The old process entry is only removed AFTER the new process has started
    /// successfully to prevent loss of process tracking if the restart fails.
    /// </remarks>
    private async Task HandleAutoRestartAsync(Guid processId, ProcessEntry entry)
    {
        try
        {
            _logger.LogInformation(
                "Auto-restarting process {ProcessId} (attempt {Attempt}/{MaxAttempts}) after {Delay}",
                processId,
                entry.ManagedProcess.RestartCount + 1,
                entry.Config.MaxRestartAttempts,
                entry.Config.RestartDelay);

            await Task.Delay(entry.Config.RestartDelay, _disposalCts.Token).ConfigureAwait(false);

            if (_disposed)
            {
                return;
            }

            // Start new process BEFORE removing the old entry to prevent loss of tracking
            var result = await StartProcessAsync(entry.Config, _disposalCts.Token).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                result.Value.RestartCount = entry.ManagedProcess.RestartCount + 1;
                _logger.LogInformation(
                    "Successfully restarted process, new ProcessId: {NewProcessId}",
                    result.Value.ProcessId);

                // Only remove old entry AFTER new process started successfully
                _processes.TryRemove(processId, out _);

                // Clean up old resources
                try
                {
                    entry.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
                }
            }
            else
            {
                _logger.LogError(
                    "Failed to auto-restart process {ProcessId}: {Error}",
                    processId, result.Error);

                // On failure, still remove the old entry as the process has exited
                // but resources should be cleaned up
                _processes.TryRemove(processId, out _);

                try
                {
                    entry.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Auto-restart cancelled for process {ProcessId}", processId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-restart of process {ProcessId}", processId);
        }
    }

    /// <summary>
    /// Cleans up resources after a failed process start.
    /// </summary>
    private void CleanupFailedStart(
        Guid processId,
        System.Diagnostics.Process? osProcess,
        JobObjectHandle? jobHandle)
    {
        _processes.TryRemove(processId, out _);

        try
        {
            osProcess?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        try
        {
            jobHandle?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
    }

    /// <summary>
    /// Validates that the executable path is safe to execute.
    /// </summary>
    private Result<bool> ValidateExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Result<bool>.Failure("[Process.InvalidPath] Executable path is required");
        }

        // SECURITY: Ensure the path is absolute and normalized
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"[Process.InvalidPath] Invalid executable path: {ex.Message}");
        }

        // SECURITY: Check for path traversal attempts
        if (!fullPath.Equals(executablePath, StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathFullyQualified(executablePath))
        {
            _logger.LogWarning(
                "Potential path traversal detected. Original: {Original}, Resolved: {Resolved}",
                executablePath, fullPath);
            return Result<bool>.Failure(
                "[Process.PathTraversal] Path traversal attempt detected in executable path");
        }

        // Verify the file exists
        if (!File.Exists(fullPath))
        {
            return Result<bool>.Failure($"[Process.NotFound] Executable not found: {fullPath}");
        }

        // SECURITY: Verify it's an executable file type
        var extension = Path.GetExtension(fullPath).ToUpperInvariant();
        if (!IsAllowedExecutableExtension(extension))
        {
            return Result<bool>.Failure(
                $"[Process.InvalidExtension] File type not allowed: {extension}");
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Validates that the working directory exists and is accessible.
    /// Returns the normalized absolute path on success.
    /// </summary>
    /// <param name="workingDirectory">The working directory to validate.</param>
    /// <returns>Result containing the normalized absolute path on success, or failure with error message.</returns>
    private static Result<string> ValidateWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Result<string>.Failure("[Process.InvalidWorkingDir] Working directory cannot be empty");
        }

        // SECURITY: Require fully qualified paths to reject drive-relative paths (e.g., "C:foo")
        if (!Path.IsPathFullyQualified(workingDirectory))
        {
            return Result<string>.Failure("[Process.InvalidWorkingDir] Working directory must be a fully qualified absolute path");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(workingDirectory);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"[Process.InvalidWorkingDir] Invalid working directory: {ex.Message}");
        }

        // SECURITY: Detect path mutations (traversal sequences, normalization differences)
        var inputNormalized = workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPathNormalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!inputNormalized.Equals(fullPathNormalized, comparison))
        {
            return Result<string>.Failure("[Process.InvalidWorkingDir] Working directory contains path traversal sequences");
        }

        if (!Directory.Exists(fullPath))
        {
            return Result<string>.Failure($"[Process.WorkingDirNotFound] Working directory not found: {fullPath}");
        }

        return Result<string>.Success(fullPath);
    }

    /// <summary>
    /// Checks if the file extension is allowed for execution.
    /// </summary>
    private static bool IsAllowedExecutableExtension(string extension)
    {
        // Allow common executable extensions
        return extension switch
        {
            ".EXE" => true,
            ".BAT" => true,
            ".CMD" => true,
            ".COM" => true,
            "" => true, // Linux-style executables without extension
            _ => false
        };
    }

    #endregion

    #region Private Types

    /// <summary>
    /// Internal tracking entry for a managed process.
    /// </summary>
    private sealed class ProcessEntry : IDisposable
    {
        public ManagedProcess ManagedProcess { get; }
        public System.Diagnostics.Process OsProcess { get; }
        public JobObjectHandle JobHandle { get; }
        public ProcessConfig Config { get; }

        /// <summary>
        /// IO completion port handle for memory limit monitoring.
        /// Null if memory limit monitoring is not enabled.
        /// </summary>
        public IoCompletionPortHandle? CompletionPortHandle { get; set; }

        /// <summary>
        /// Cancellation token source to stop the memory monitoring task.
        /// </summary>
        public CancellationTokenSource? MonitoringCts { get; set; }

        /// <summary>
        /// Task running the memory limit monitor loop.
        /// </summary>
        public Task? MonitoringTask { get; set; }

        private volatile bool _disposed;

        public ProcessEntry(
            ManagedProcess managedProcess,
            System.Diagnostics.Process osProcess,
            JobObjectHandle jobHandle,
            ProcessConfig config)
        {
            ManagedProcess = managedProcess;
            OsProcess = osProcess;
            JobHandle = jobHandle;
            Config = config;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // Signal the monitoring task to stop
            try
            {
                if (MonitoringCts is not null && !MonitoringCts.IsCancellationRequested)
                {
                    // Post a shutdown message to wake up the monitoring thread
                    if (CompletionPortHandle is not null && !CompletionPortHandle.IsInvalid && !CompletionPortHandle.IsClosed)
                    {
                        NativeMethods.PostQueuedCompletionStatus(
                            CompletionPortHandle.DangerousGetHandle(),
                            NativeMethods.JobObjectMsg.ShutdownMonitor,
                            0,
                            IntPtr.Zero);
                    }

                    MonitoringCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }

            // Wait briefly for the monitoring task to complete
            try
            {
                MonitoringTask?.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch
            {
                // Ignore exceptions from the monitoring task
            }

            // Dispose resources in order
            try { MonitoringCts?.Dispose(); } catch (ObjectDisposedException) { }
            try { CompletionPortHandle?.Dispose(); } catch (ObjectDisposedException) { }
            try { JobHandle.Dispose(); } catch (ObjectDisposedException) { }
            try { OsProcess.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    #endregion
}
