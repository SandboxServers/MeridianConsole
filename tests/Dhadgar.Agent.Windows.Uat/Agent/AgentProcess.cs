using System.Diagnostics;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// Runs the published agent as a console child of the driver (process mode, P20 step 3). The first start writes
/// the manifest entries the agent may cause, before the agent can cause them. The legs reach it through
/// <see cref="IAgentHost"/> (<see cref="ProcessAgentHost"/>).
/// </summary>
internal sealed class AgentProcess : IDisposable
{
    /// <summary>The published agent executable, relative to the agent publish folder.</summary>
    public const string ExecutableName = "Dhadgar.Agent.Windows.exe";

    /// <summary>Log message the agent writes when service recovery cannot be configured (expected in process mode).</summary>
    public const string RecoveryWarningText = "Failed to configure service recovery delays";

    private readonly RunPaths _paths;
    private readonly JobObject _job;
    private readonly Redactor _redactor;
    private readonly CleanupManifest _manifest;
    private ChildProcess? _child;
    private readonly ChildSpec _spec;
    private bool _entriesWritten;

    /// <summary>Creates the process wrapper for one run.</summary>
    public AgentProcess(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest)
        : this(paths, job, redactor, manifest, DefaultSpec(paths))
    {
    }

    /// <summary>Test seam: runs <paramref name="spec"/> instead of the published agent.</summary>
    internal AgentProcess(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest, ChildSpec spec)
    {
        _paths = paths;
        _job = job;
        _redactor = redactor;
        _manifest = manifest;
        _spec = spec;
    }

    private static ChildSpec DefaultSpec(RunPaths paths) => new(
        Name: "agent",
        FileName: Path.Combine(paths.AgentPublish, ExecutableName),
        Arguments: [],
        WorkingDirectory: paths.AgentPublish,
        Environment: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
        });

    /// <summary>The agent's process id while it runs; null before the first start.</summary>
    public int? ProcessId => _child?.ProcessId;

    /// <summary>
    /// Writes the first-start manifest entries (once) before the first start. <see cref="ProcessAgentHost.PrepareAsync"/>
    /// calls it; <see cref="Start"/> calls it too, so a direct start still records them.
    /// </summary>
    public Result PrepareFirstStart()
    {
        if (_entriesWritten)
        {
            return Result.Success();
        }

        var entries = AppendFirstStartEntries(_manifest);
        if (entries.IsFailure)
        {
            return entries;
        }

        _entriesWritten = true;
        return Result.Success();
    }

    /// <summary>Starts the agent. Writes the first-start manifest entries before the first start.</summary>
    public Result Start()
    {
        if (_child is { ExitCode: null })
        {
            return Result.Failure("[AgentProcess.Running] the agent is already running");
        }

        var prepared = PrepareFirstStart();
        if (prepared.IsFailure)
        {
            return prepared;
        }

        var started = ChildProcess.Start(_spec, _job, _redactor, _manifest);
        if (started.IsFailure)
        {
            return Result.Failure(started.Error);
        }

        _child?.Dispose();
        _child = started.Value;
        return Result.Success();
    }

    /// <summary>Kills the agent's process tree (a crash-style stop, P20 step 3).</summary>
    public async Task<Result> StopAsync()
    {
        if (_child is null)
        {
            return Result.Success();
        }

        var stopped = await _child.StopAsync();
        if (stopped.IsSuccess)
        {
            _child.Dispose(); // closes agent.log so the next start and the U9 scan can open it
            _child = null;
        }

        return stopped;
    }

    /// <summary>
    /// True when the agent's log holds the expected service-recovery warning. Process mode has no service, so the
    /// warning is expected on every start. A missing log gives false.
    /// </summary>
    public Result<bool> SawRecoveryWarning()
    {
        var logPath = Path.Combine(_paths.Logs, "agent.log");
        try
        {
            if (!File.Exists(logPath))
            {
                return Result<bool>.Success(false);
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Contains(RecoveryWarningText, StringComparison.Ordinal))
                {
                    return Result<bool>.Success(true);
                }
            }

            return Result<bool>.Success(false);
        }
        catch (IOException)
        {
            return Result<bool>.Failure("[AgentProcess.Log] agent.log could not be read");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<bool>.Failure("[AgentProcess.Log] access denied reading agent.log");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _child?.Dispose();
        _child = null;
    }

    /// <summary>
    /// The entries the agent may cause on its first start, in order: its event source, its CA store (absent by preflight)
    /// and the machine agent folder (which holds the agent's machine Temp and Servers; D-S238).
    /// </summary>
    internal static IReadOnlyList<CleanupEntry> FirstStartEntries(bool eventSourceExisted, bool agentDirectoryExisted) =>
    [
        new EventSourceEntry(AgentWindowsFacts.EventLogSourceName, eventSourceExisted),
        new CertStoreEntry(AgentWindowsFacts.CaStoreName, ExistedBefore: false),
        new DirectoryEntry(AgentDirectories.MachineAgentDirectory, agentDirectoryExisted),
    ];

    /// <summary>
    /// Appends <see cref="FirstStartEntries"/> to <paramref name="manifest"/>. Each entry records whether the item
    /// existed before. Shared by the process and the service host so both modes record the same entries.
    /// </summary>
    internal static Result AppendFirstStartEntries(CleanupManifest manifest)
    {
        var entries = FirstStartEntries(
            SafeEventSourceExists(AgentWindowsFacts.EventLogSourceName),
            Directory.Exists(AgentDirectories.MachineAgentDirectory));

        foreach (var entry in entries)
        {
            var appended = manifest.Append(entry);
            if (appended.IsFailure)
            {
                return appended;
            }
        }

        return Result.Success();
    }

    private static bool SafeEventSourceExists(string source)
    {
        try
        {
            return EventLog.SourceExists(source);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
