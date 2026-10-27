using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Cleanup;

/// <summary>A failed undo: the entry kind, its identity, and the message.</summary>
internal sealed record CleanupFailure(string Kind, string Identity, string Message);

/// <summary>Outcome of an undo pass.</summary>
internal sealed record CleanupReport(int Undone, IReadOnlyList<CleanupFailure> Failed);

/// <summary>
/// Undoes a manifest. First a quiesce pass stops every service and kills every process (newest first),
/// then every entry is undone in reverse order. One failure does not stop the rest. Entries with
/// <c>existedBefore: true</c> are never deleted. Running it twice gives the same calls (idempotent).
/// </summary>
internal sealed class CleanupEngine
{
    private readonly IMachineActions _actions;
    private readonly RunPaths _runPaths;

    /// <summary>Creates an engine over the given machine actions and run folder.</summary>
    public CleanupEngine(IMachineActions actions, RunPaths runPaths)
    {
        _actions = actions;
        _runPaths = runPaths;
    }

    /// <summary>Undoes the entries. Pass <see cref="CancellationToken.None"/>: cleanup is not cancelled by Ctrl+C.</summary>
    public Task<CleanupReport> UndoAsync(IReadOnlyList<CleanupEntry> entries, CancellationToken ct)
    {
        var failed = new List<CleanupFailure>();
        var undone = 0;

        // Quiesce: a running service or agent process would hold its key, store or folder.
        foreach (var entry in entries.Reverse())
        {
            switch (entry)
            {
                case ServiceEntry service:
                    _ = _actions.StopService(service.Name);
                    break;
                case ProcessEntry process:
                    _ = _actions.KillProcess(process.Pid, process.StartTimeUtc);
                    break;
            }
        }

        foreach (var entry in entries.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            var (kind, identity, result) = Undo(entry);
            if (!result.HasValue)
            {
                continue;
            }

            var outcome = result.Value;
            if (outcome.IsSuccess)
            {
                undone++;
            }
            else
            {
                failed.Add(new CleanupFailure(kind, identity, outcome.Error));
            }
        }

        return Task.FromResult(new CleanupReport(undone, failed));
    }

    private (string Kind, string Identity, Result? Result) Undo(CleanupEntry entry) => entry switch
    {
        ContainerEntry c => ("container", c.Name, _actions.RemoveContainer(c.Name)),
        ProcessEntry p => ("process", p.Pid.ToString(), _actions.KillProcess(p.Pid, p.StartTimeUtc)),
        RegistryKeyEntry k when !k.ExistedBefore => ("registryKey", $"{k.Hive}\\{k.Path}", _actions.DeleteRegistryKey(k.Hive, k.Path)),
        RegistryValueEntry v when !v.ExistedBefore => ("registryValue", $"{v.Path}\\{v.Name}", _actions.DeleteRegistryValue(v.Path, v.Name)),
        CertificateEntry c => ("certificate", c.ThumbprintSha1, _actions.RemoveCertificate(c.StoreLocation, c.StoreName, c.ThumbprintSha1)),
        CngKeyEntry k => ("cngKey", k.KeyName, _actions.DeleteCngKey(k.KeyName, k.Provider, k.MachineKey)),
        CertStoreEntry s when !s.ExistedBefore => ("certStore", s.StoreName, _actions.UnregisterCertStore(s.StoreName)),
        DirectoryEntry d when !d.ExistedBefore => ("directory", d.Path, DeleteGuarded(d.Path, _actions.DeleteDirectory)),
        FileEntry f => ("file", f.Path, DeleteGuarded(f.Path, _actions.DeleteFile)),
        EventSourceEntry e when !e.ExistedBefore => ("eventSource", e.Source, _actions.DeleteEventSource(e.Source)),
        ServiceEntry s => ("service", s.Name, StopThenDelete(s.Name)),
        _ => ("skipped", entry.GetType().Name, null),
    };

    private Result StopThenDelete(string name)
    {
        var stopped = _actions.StopService(name);
        if (stopped.IsFailure)
        {
            return stopped;
        }

        return _actions.DeleteService(name);
    }

    private Result DeleteGuarded(string path, Func<string, Result> delete)
    {
        var validated = _runPaths.Validate(path);
        if (validated.IsFailure && !AgentDirectories.IsMachineAgentDirectory(path))
        {
            return Result.Failure($"refusing to delete outside the run folder: {validated.Error}");
        }

        return delete(path);
    }
}
