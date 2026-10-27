using System.Text.RegularExpressions;
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
/// The manifest is untrusted input: <see cref="Reject"/> refuses any item the driver does not own (D-S237).
/// </summary>
internal sealed class CleanupEngine
{
    private const string LocalMachine = "LocalMachine";
    private const string HklmHive = "HKLM";
    private const string KeyStorageProvider = "Microsoft Software Key Storage Provider";
    private const string PlatformCryptoProvider = "Microsoft Platform Crypto Provider";
    private const string CngKeyPrefix = "meridian-uat-";
    private static readonly Regex ContainerNamePattern = new(@"^meridian-uat-[a-z0-9][a-z0-9_.-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex CngKeyNamePattern = new(@"^[A-Za-z0-9_.{}-]{1,128}\z", RegexOptions.CultureInvariant);

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
        // Reject runs inside the guard, so a probe that throws cannot abort cleanup; the undo pass records it.
        foreach (var entry in entries.Reverse())
        {
            if (entry is not (ServiceEntry or ProcessEntry))
            {
                continue;
            }

            QuiesceIgnoringResult(() => Reject(entry) is not null
                ? Result.Success()
                : entry switch
                {
                    ServiceEntry service => _actions.StopService(service.Name),
                    ProcessEntry process => _actions.KillProcess(process.Pid, process.StartTimeUtc),
                    _ => Result.Success(),
                });
        }

        foreach (var entry in entries.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            (string Kind, string Identity, Result? Result) outcome;
#pragma warning disable CA1031 // One entry's exception must not stop the rest; it is recorded as that entry's failure.
            try
            {
                outcome = Undo(entry);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = (EntryKind(entry), Identity(entry), Result.Failure($"{ex.GetType().Name}: {ex.Message}"));
            }
#pragma warning restore CA1031

            if (!outcome.Result.HasValue)
            {
                continue;
            }

            var result = outcome.Result.Value;
            if (result.IsSuccess)
            {
                undone++;
            }
            else
            {
                failed.Add(new CleanupFailure(outcome.Kind, outcome.Identity, result.Error));
            }
        }

        return Task.FromResult(new CleanupReport(undone, failed));
    }

    /// <summary>
    /// The allowlist of machine items undo may touch, one case per kind. Anything else is refused before any
    /// call. Directories and files are checked by <see cref="DeleteGuarded"/>; processes by their PID and start time.
    /// Add a new kind here when a packet adds one. The agent's own CNG key is deleted through its certificate, never by name (D-S238).
    /// </summary>
    private Result? Reject(CleanupEntry entry)
    {
        var allowed = entry switch
        {
            ServiceEntry s => string.Equals(s.Name, AgentWindowsFacts.ServiceName, StringComparison.Ordinal),
            ProcessEntry p => ProcessImageAllowed(p),
            RegistryKeyEntry k => string.Equals(k.Hive, HklmHive, StringComparison.Ordinal)
                && (IsAgentKey(k.Path) || IsParentKey(k.Path)),
            RegistryValueEntry v => IsAgentKey(v.Path)
                && string.Equals(v.Name, AgentWindowsFacts.EnrollmentTokenValueName, StringComparison.Ordinal),
            CertificateEntry c => string.Equals(c.StoreLocation, LocalMachine, StringComparison.Ordinal)
                && c.StoreName is "Root" or "My" or AgentWindowsFacts.CaStoreName,
            CertStoreEntry s => string.Equals(s.StoreName, AgentWindowsFacts.CaStoreName, StringComparison.Ordinal),
            EventSourceEntry e => string.Equals(e.Source, AgentWindowsFacts.EventLogSourceName, StringComparison.Ordinal),
            ContainerEntry c => ContainerNamePattern.IsMatch(c.Name),
            MsiProductEntry m => string.Equals(m.UpgradeCode, AgentWindowsFacts.MsiUpgradeCode, StringComparison.OrdinalIgnoreCase)
                && _runPaths.Validate(m.MsiPath).IsSuccess,
            CngKeyEntry k => k.Provider is KeyStorageProvider or PlatformCryptoProvider
                && k.KeyName.StartsWith(CngKeyPrefix, StringComparison.Ordinal)
                && CngKeyNamePattern.IsMatch(k.KeyName),
            _ => true,
        };

        return allowed
            ? null
            : Result.Failure($"manifest entry refused: {EntryKind(entry)} {Identity(entry)} is not a UAT-managed item");
    }

    /// <summary>
    /// A process may be killed only if it is not the driver itself and its image is under the run folder or is one
    /// of <see cref="RunPaths.StackImages"/>. A process that no longer runs (gone, or its PID reused) is allowed:
    /// the kill re-checks the start time and is a no-op. An image that cannot be read is refused (fail closed).
    /// </summary>
    private bool ProcessImageAllowed(ProcessEntry process)
    {
        if (process.Pid == Environment.ProcessId)
        {
            return false;
        }

        var probe = _actions.GetProcessImage(process.Pid, process.StartTimeUtc);
        if (probe.IsFailure)
        {
            return false;
        }

        var image = probe.Value;
        if (!image.IsRunning)
        {
            return true;
        }

        return _runPaths.Validate(image.Path).IsSuccess
            || _runPaths.StackImages.Any(allowed => string.Equals(allowed, image.Path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAgentKey(string path) =>
        path.Equals(AgentWindowsFacts.RegistryKeyPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(AgentWindowsFacts.RegistryKeyPath + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Exactly the parent key; no prefix match, so siblings and children of the parent are refused (D-S238).</summary>
    private static bool IsParentKey(string path) =>
        path.Equals(AgentWindowsFacts.RegistryParentKeyPath, StringComparison.OrdinalIgnoreCase);

    private (string Kind, string Identity, Result? Result) Undo(CleanupEntry entry)
    {
        var refusal = Reject(entry);
        if (refusal is not null)
        {
            return (EntryKind(entry), Identity(entry), refusal);
        }

        return entry switch
        {
            ContainerEntry c => ("container", c.Name, _actions.RemoveContainer(c.Name)),
            ProcessEntry p => ("process", p.Pid.ToString(), _actions.KillProcess(p.Pid, p.StartTimeUtc)),
            RegistryKeyEntry k when !k.ExistedBefore && IsParentKey(k.Path) =>
                ("registryKey", $"{k.Hive}\\{k.Path}", _actions.DeleteEmptyRegistryKey(k.Hive, k.Path)),
            RegistryKeyEntry k when !k.ExistedBefore => ("registryKey", $"{k.Hive}\\{k.Path}", _actions.DeleteRegistryKey(k.Hive, k.Path)),
            RegistryValueEntry v when !v.ExistedBefore => ("registryValue", $"{v.Path}\\{v.Name}", _actions.DeleteRegistryValue(v.Path, v.Name)),
            CertificateEntry c => ("certificate", c.ThumbprintSha1, _actions.RemoveCertificate(c.StoreLocation, c.StoreName, c.ThumbprintSha1)),
            CngKeyEntry k => ("cngKey", k.KeyName, _actions.DeleteCngKey(k.KeyName, k.Provider, k.MachineKey)),
            CertStoreEntry s when !s.ExistedBefore => ("certStore", s.StoreName, _actions.UnregisterCertStore(s.StoreName)),
            DirectoryEntry d when !d.ExistedBefore => ("directory", d.Path, DeleteGuarded(d.Path, _actions.DeleteDirectory, isDirectory: true)),
            FileEntry f => ("file", f.Path, DeleteGuarded(f.Path, _actions.DeleteFile, isDirectory: false)),
            EventSourceEntry e when !e.ExistedBefore => ("eventSource", e.Source, _actions.DeleteEventSource(e.Source)),
            ServiceEntry s => ("service", s.Name, StopThenDelete(s.Name)),
            MsiProductEntry m => ("msiProduct", m.UpgradeCode, _actions.UninstallMsiProduct(m.UpgradeCode)),
            _ => ("skipped", entry.GetType().Name, null),
        };
    }

    private static string EntryKind(CleanupEntry entry) => entry switch
    {
        ContainerEntry => "container",
        ProcessEntry => "process",
        RegistryKeyEntry => "registryKey",
        RegistryValueEntry => "registryValue",
        CertificateEntry => "certificate",
        CngKeyEntry => "cngKey",
        CertStoreEntry => "certStore",
        DirectoryEntry => "directory",
        FileEntry => "file",
        EventSourceEntry => "eventSource",
        ServiceEntry => "service",
        MsiProductEntry => "msiProduct",
        _ => "skipped",
    };

    private static string Identity(CleanupEntry entry) => entry switch
    {
        ContainerEntry c => c.Name,
        ProcessEntry p => p.Pid.ToString(),
        RegistryKeyEntry k => $"{k.Hive}\\{k.Path}",
        RegistryValueEntry v => $"{v.Path}\\{v.Name}",
        CertificateEntry c => c.ThumbprintSha1,
        CngKeyEntry k => k.KeyName,
        CertStoreEntry s => s.StoreName,
        DirectoryEntry d => d.Path,
        FileEntry f => f.Path,
        EventSourceEntry e => e.Source,
        ServiceEntry s => s.Name,
        MsiProductEntry m => m.UpgradeCode,
        _ => entry.GetType().Name,
    };

    /// <summary>Runs a quiesce call. Its result is ignored; the undo pass records any service or process that is left.</summary>
    private static void QuiesceIgnoringResult(Func<Result> call)
    {
#pragma warning disable CA1031 // Best effort: a quiesce failure must not stop the undo pass.
        try
        {
            _ = call();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
        }
#pragma warning restore CA1031
    }

    private Result StopThenDelete(string name)
    {
        var stopped = _actions.StopService(name);
        if (stopped.IsFailure)
        {
            return stopped;
        }

        return _actions.DeleteService(name);
    }

    /// <summary>
    /// Refuses a path that is not under the run folder. A directory may also be exactly the machine agent
    /// directory; a file never may.
    /// </summary>
    private Result DeleteGuarded(string path, Func<string, Result> delete, bool isDirectory)
    {
        var validated = _runPaths.Validate(path);
        var machineDirectory = isDirectory && AgentDirectories.IsMachineAgentDirectory(path);
        if (validated.IsFailure && !machineDirectory)
        {
            return Result.Failure($"refusing to delete outside the run folder: {validated.Error}");
        }

        return delete(path);
    }
}
