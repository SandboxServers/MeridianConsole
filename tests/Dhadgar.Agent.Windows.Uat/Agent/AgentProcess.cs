using System.Diagnostics;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// Runs the published agent as a console child of the driver (process mode, P20 step 3). The first start writes
/// the manifest entries the agent may cause, before the agent can cause them.
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
    private bool _entriesWritten;

    /// <summary>Creates the process wrapper for one run.</summary>
    public AgentProcess(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest)
    {
        _paths = paths;
        _job = job;
        _redactor = redactor;
        _manifest = manifest;
    }

    /// <summary>The agent's process id while it runs; null before the first start.</summary>
    public int? ProcessId => _child?.ProcessId;

    /// <summary>Starts the agent. Writes the first-start manifest entries before the first start.</summary>
    public Result Start()
    {
        if (_child is { ExitCode: null })
        {
            return Result.Failure("[AgentProcess.Running] the agent is already running");
        }

        if (!_entriesWritten)
        {
            var entries = WriteFirstStartEntries();
            if (entries.IsFailure)
            {
                return entries;
            }

            _entriesWritten = true;
        }

        var spec = new ChildSpec(
            Name: "agent",
            FileName: Path.Combine(_paths.AgentPublish, ExecutableName),
            Arguments: [],
            WorkingDirectory: _paths.AgentPublish,
            Environment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
            });

        var started = ChildProcess.Start(spec, _job, _redactor, _manifest);
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

        return await _child.StopAsync();
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

            return Result<bool>.Success(File.ReadLines(logPath).Any(line => line.Contains(RecoveryWarningText, StringComparison.Ordinal)));
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
    /// Appends the entries the agent may cause on its first start: its event source, its CA store (guaranteed absent by
    /// preflight) and the two machine directories. Each entry records whether the item existed before.
    /// </summary>
    private Result WriteFirstStartEntries()
    {
        var machineDir = AgentDirectories.MachineAgentDirectory;
        var tempDir = Path.Combine(Path.GetDirectoryName(machineDir)!, "Temp");

        var entries = new List<CleanupEntry>
        {
            new EventSourceEntry(AgentWindowsFacts.EventLogSourceName, SafeEventSourceExists(AgentWindowsFacts.EventLogSourceName)),
            new CertStoreEntry(AgentWindowsFacts.CaStoreName, ExistedBefore: false),
            new DirectoryEntry(machineDir, Directory.Exists(machineDir)),
            new DirectoryEntry(tempDir, Directory.Exists(tempDir)),
        };

        foreach (var entry in entries)
        {
            var appended = _manifest.Append(entry);
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
