using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// Runs the daemon's host children (log followers, and later Playwright and fleet jobs), captures their output and
/// supervises them. Every line goes through <see cref="IRawLineObserver"/>, then <see cref="Redactor"/>, then the ring and
/// <c>logs/&lt;name&gt;.jsonl</c>. The watchdog relaunches an unexpected exit only while a lease is held, the target is not
/// faulted and fewer than three relaunches happened in five minutes (D-ML19).
/// </summary>
public sealed class LabProcessSupervisor : ILabStatusSource, IDisposable
{
    /// <summary>
    /// Relaunches allowed inside <see cref="RelaunchWindow"/>.
    /// </summary>
    public const int MaxRelaunchesPerWindow = 3;

    /// <summary>
    /// The window over which relaunches are counted.
    /// </summary>
    public static readonly TimeSpan RelaunchWindow = TimeSpan.FromMinutes(5);

    private const string StateRunning = "running";
    private const string StateStopped = "stopped";
    private const string StateCrashed = "crashed";
    private const string StateExited = "exited";
    private const string StateFailed = "failed";
    private const int MaxNameLength = 64;

    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, ManagedProcess> _processes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly LogPathGuard _logPaths;
    private readonly Redactor _redactor;
    private readonly LeaseBook _leases;
    private readonly TimeProvider _time;
    private readonly IRawLineObserver _observer;
    private readonly IFaultTargets _faults;
    private readonly WindowsJobObject? _job;
    private readonly string? _jobError;
    private bool _disposed;

    /// <summary>
    /// Raised once for every child exit, after the watchdog decision. Feeds L21's I6.
    /// </summary>
    [SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "ProcessExit is a positional record and cannot derive from EventArgs (records may only inherit a sealed type), so EventHandler<ProcessExit> is the shape the L07 spec and its tests use.")]
    public event EventHandler<ProcessExit>? Exited;

    /// <summary>
    /// Creates the supervisor. On Windows it creates the daemon's kill-on-close job object.
    /// </summary>
    public LabProcessSupervisor(
        LogPathGuard logPaths,
        Redactor redactor,
        LeaseBook leases,
        TimeProvider time,
        IRawLineObserver observer,
        IFaultTargets faults)
    {
        _logPaths = logPaths ?? throw new ArgumentNullException(nameof(logPaths));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _faults = faults ?? throw new ArgumentNullException(nameof(faults));

        Result<WindowsJobObject> job = WindowsJobObject.Create();
        if (job.IsSuccess)
        {
            _job = job.Value;
        }
        else
        {
            _jobError = job.Error;
        }
    }

    /// <summary>
    /// Starts a child under the spec. Fails with <c>[Lab.Conflict]</c> when the name is already running, and with
    /// <c>[Lab.Unavailable]</c> when the readiness probe fails (the child is stopped again).
    /// </summary>
    public async Task<Result<ProcessStatusDto>> StartAsync(LabProcessSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!IsValidName(spec.Name))
        {
            return Result<ProcessStatusDto>.Failure("[Lab.InvalidArgument] process name must be 1 to 64 letters, digits, '-' or '_'");
        }

        Result<string> logPath = _logPaths.ResolveLogFile(spec.Name);
        if (logPath.IsFailure)
        {
            return Result<ProcessStatusDto>.Failure(logPath.Error);
        }

        ManagedProcess entry;
        lock (_gate)
        {
            if (_disposed)
            {
                return Result<ProcessStatusDto>.Failure("[Lab.Unavailable] the supervisor is disposed");
            }

            if (_processes.TryGetValue(spec.Name, out ManagedProcess? existing))
            {
                if (string.Equals(existing.State, StateRunning, StringComparison.Ordinal))
                {
                    return Result<ProcessStatusDto>.Failure("[Lab.Conflict] the process is already running");
                }

                entry = existing;
            }
            else
            {
                entry = new ManagedProcess(spec.Name, new LogRing());
                _processes[spec.Name] = entry;
            }

            entry.Spec = spec;
            entry.LogPath = logPath.Value;
            entry.StopRequested = false;
            if (entry.Writer is null)
            {
                Result<StreamWriter> writer = OpenWriter(logPath.Value!);
                if (writer.IsFailure)
                {
                    entry.State = StateFailed;
                    return Result<ProcessStatusDto>.Failure(writer.Error);
                }

                entry.Writer = writer.Value;
            }

            Result launched = Launch(entry);
            if (launched.IsFailure)
            {
                entry.State = StateFailed;
                return Result<ProcessStatusDto>.Failure(launched.Error);
            }
        }

        if (spec.Readiness is not null && !await RunReadinessAsync(spec.Readiness, ct).ConfigureAwait(false))
        {
            _ = await StopAsync(spec.Name).ConfigureAwait(false);
            return Result<ProcessStatusDto>.Failure("[Lab.Unavailable] the process did not become ready");
        }

        lock (_gate)
        {
            return Result<ProcessStatusDto>.Success(ToStatus(entry));
        }
    }

    /// <summary>
    /// Stops a child. The exit is expected: it is never relaunched and never counted as a restart.
    /// </summary>
    public async Task<Result> StopAsync(string name)
    {
        Task<bool>? exit;
        lock (_gate)
        {
            if (!_processes.TryGetValue(name, out ManagedProcess? entry))
            {
                return Result.Failure("[Lab.NotFound] no process has that name");
            }

            entry.StopRequested = true;
            if (entry.Process is null)
            {
                return Result.Success();
            }

            exit = entry.ExitSignal?.Task;
            KillQuietly(entry.Process);
        }

        if (exit is null)
        {
            return Result.Success();
        }

        try
        {
            _ = await exit.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Result.Failure("[Lab.Unavailable] the process did not exit in time");
        }

        return Result.Success();
    }

    /// <summary>
    /// Stops a child and starts it again under the spec it last ran with.
    /// </summary>
    public async Task<Result<ProcessStatusDto>> RestartAsync(string name)
    {
        LabProcessSpec? spec;
        lock (_gate)
        {
            if (!_processes.TryGetValue(name, out ManagedProcess? entry))
            {
                return Result<ProcessStatusDto>.Failure("[Lab.NotFound] no process has that name");
            }

            spec = entry.Spec;
        }

        Result stopped = await StopAsync(name).ConfigureAwait(false);
        if (stopped.IsFailure)
        {
            return Result<ProcessStatusDto>.Failure(stopped.Error);
        }

        return await StartAsync(spec!, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// The status of every supervised process, sorted by name. Carries no environment and no arguments.
    /// </summary>
    public IReadOnlyList<ProcessStatusDto> Snapshot()
    {
        lock (_gate)
        {
            return _processes.Values
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(ToStatus)
                .ToList();
        }
    }

    /// <summary>
    /// The log ring of one supervised process, or null for an unknown name. Log clauses read it (C7).
    /// </summary>
    public LogRing? RingFor(string name)
    {
        lock (_gate)
        {
            return _processes.TryGetValue(name, out ManagedProcess? entry) ? entry.Ring : null;
        }
    }

    /// <summary>
    /// The captured lines of one process, oldest first. Fails with <c>[Lab.NotFound]</c> for an unknown name.
    /// </summary>
    public Result<IReadOnlyList<LogLine>> ReadLines(string name)
    {
        LogRing ring;
        lock (_gate)
        {
            if (!_processes.TryGetValue(name, out ManagedProcess? entry))
            {
                return Result<IReadOnlyList<LogLine>>.Failure("[Lab.NotFound] no captured log for that process");
            }

            ring = entry.Ring;
        }

        return Result<IReadOnlyList<LogLine>>.Success(ring.Snapshot());
    }

    /// <summary>
    /// Adds the process list to <c>GET /status</c> and <c>lab_status</c>.
    /// </summary>
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (ProcessStatusDto process in Snapshot())
        {
            builder.Processes.Add(process);
        }
    }

    /// <summary>
    /// Kills every running child and closes the job object. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        List<Process> running = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (ManagedProcess entry in _processes.Values)
            {
                if (entry.Process is not null)
                {
                    running.Add(entry.Process);
                }

                entry.Writer?.Dispose();
                entry.Writer = null;
            }
        }

        foreach (Process process in running)
        {
            KillQuietly(process);
        }

        _job?.Dispose();
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (char c in name)
        {
            bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static ProcessStatusDto ToStatus(ManagedProcess entry) =>
        new(entry.Name, entry.State, entry.Pid, entry.Restarts, entry.StartedAt);

    private static async Task<bool> RunReadinessAsync(Func<CancellationToken, Task<bool>> readiness, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ReadinessTimeout);
        try
        {
            return await readiness(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    private static Result<StreamWriter> OpenWriter(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<StreamWriter>.Failure("[Lab.Unavailable] the log file could not be opened");
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process already ended, or the OS would not kill it; the exit handler or the job object settles the state.
        }
    }

    /// <summary>
    /// Starts the child and attaches it. Caller holds <see cref="_gate"/>. A child that cannot join the job is killed.
    /// </summary>
    private Result Launch(ManagedProcess entry)
    {
        if (OperatingSystem.IsWindows() && _job is null)
        {
            return Result.Failure(_jobError ?? "[Lab.Unavailable] the job object is not available");
        }

        Result<Process> created = LabProcessStart.Create(entry.Spec);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error);
        }

        Process process = created.Value!;
        bool attached = false;
        try
        {
            entry.ExitSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => OnExited(entry, process);
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Ingest(entry, args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Ingest(entry, args.Data);
                }
            };

            Result started = LabProcessStart.StartProcess(process);
            if (started.IsFailure)
            {
                return started;
            }

            if (_job is not null)
            {
                Result assigned = _job.Assign(process.Handle);
                if (assigned.IsFailure)
                {
                    KillQuietly(process);
                    return assigned;
                }
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            entry.Process = process;
            entry.Pid = process.Id;
            entry.StartedAt = _time.GetUtcNow();
            entry.State = StateRunning;
            attached = true;
            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            return Result.Failure("[Lab.Process] the process could not be attached");
        }
        finally
        {
            if (!attached)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// The exit handler: records the exit, runs the watchdog decision, raises <see cref="Exited"/>.
    /// </summary>
    private void OnExited(ManagedProcess entry, Process process)
    {
        int? exitCode = null;
        try
        {
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The process object was disposed by the supervisor; the exit code stays unknown.
        }

        bool faulted = IsFaultedTarget(entry.Name);
        ProcessExit notice;
        TaskCompletionSource<bool>? done;
        try
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(entry.Process, process))
                {
                    return;
                }

                DateTimeOffset now = _time.GetUtcNow();
                entry.Process = null;
                entry.Pid = null;
                // WaitForExit() above drains the redirected output, so no more lines reach the writer.
                entry.Writer?.Dispose();
                entry.Writer = null;
                done = entry.ExitSignal;
                bool expected = entry.StopRequested || faulted;
                if (expected)
                {
                    entry.State = StateStopped;
                }
                else
                {
                    if (entry.Spec.Relaunch)
                    {
                        entry.Restarts++;
                    }

                    bool relaunch = entry.Spec.Relaunch && _leases.Current is not null && TakeRelaunchBudget(entry, now);
                    if (!relaunch)
                    {
                        entry.State = entry.Spec.Relaunch ? StateCrashed : StateExited;
                    }
                    else
                    {
                        Result<StreamWriter> reopened = entry.LogPath is null
                            ? Result<StreamWriter>.Failure("[Lab.Unavailable] no log path")
                            : OpenWriter(entry.LogPath);
                        if (reopened.IsSuccess)
                        {
                            entry.Writer = reopened.Value;
                        }

                        if (Launch(entry).IsFailure)
                        {
                            entry.State = StateCrashed;
                        }
                    }
                }

                notice = new ProcessExit(entry.Name, exitCode, expected, now);
            }
        }
        finally
        {
            process.Dispose();
        }

        _ = done?.TrySetResult(true);
        EventHandler<ProcessExit>? handler = Exited;
        handler?.Invoke(this, notice);
    }

    /// <summary>
    /// True when the relaunch fits the budget; records the attempt. Caller holds <see cref="_gate"/>.
    /// </summary>
    private static bool TakeRelaunchBudget(ManagedProcess entry, DateTimeOffset now)
    {
        _ = entry.Relaunches.RemoveAll(at => now - at >= RelaunchWindow);
        if (entry.Relaunches.Count >= MaxRelaunchesPerWindow)
        {
            return false;
        }

        entry.Relaunches.Add(now);
        return true;
    }

    private bool IsFaultedTarget(string name) =>
        _faults.IsFaulted("c:" + name) || _faults.IsFaulted("proc:" + name);

    /// <summary>
    /// One captured line: observer first (raw, never stored), then redaction, then the ring and the file.
    /// </summary>
    private void Ingest(ManagedProcess entry, string rawLine)
    {
        _observer.Observe(entry.Name, rawLine.AsSpan());
        string redacted = _redactor.Redact(rawLine);
        LogLine parsed = JsonLogLineParser.Parse(redacted, _time.GetUtcNow());
        // The parser decodes JSON escapes, so a vault value written escaped in the child's output is only visible now.
        LogLine line = parsed with
        {
            Level = _redactor.Redact(parsed.Level),
            Category = _redactor.Redact(parsed.Category),
            Message = _redactor.Redact(parsed.Message),
        };
        entry.Ring.Add(line);

        lock (_gate)
        {
            if (entry.Writer is null || _disposed)
            {
                return;
            }

            try
            {
                entry.Writer.WriteLine(JsonSerializer.Serialize(new LogFileRecord(line.Timestamp, line.Level, line.Category, line.EventId, line.Message)));
            }
            catch (IOException)
            {
                // The ring still holds the line; a failed disk write must not take the child's reader thread down.
            }
        }
    }

    private sealed record LogFileRecord(DateTimeOffset Ts, string Level, string Category, int? EventId, string Message);

    private sealed class ManagedProcess
    {
        public ManagedProcess(string name, LogRing ring)
        {
            Name = name;
            Ring = ring;
            Spec = null!;
        }

        public string Name { get; }

        public LogRing Ring { get; }

        public LabProcessSpec Spec { get; set; }

        public Process? Process { get; set; }

        public int? Pid { get; set; }

        public string State { get; set; } = StateStopped;

        public int Restarts { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public bool StopRequested { get; set; }

        public List<DateTimeOffset> Relaunches { get; } = [];

        public StreamWriter? Writer { get; set; }

        public string? LogPath { get; set; }

        public TaskCompletionSource<bool>? ExitSignal { get; set; }
    }
}
