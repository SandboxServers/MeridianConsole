using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Preflight;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Results;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat;

/// <summary>
/// Dispatches the verbs and owns the guard chain (D-S225, D-S239). <c>run</c> refuses before any machine change
/// unless it is elevated, has <c>--accept-machine-changes</c>, has no run folder already in use, and passes preflight
/// for run. An agent run (CLAUDECODE set, a label and not authentication) also needs a clean tree and never
/// <c>-NoBuild</c>, and writes <c>launch.json</c>; an agent cleanup works only on a folder whose <c>launch.json</c>
/// says <c>agent</c>. Cleanup and results always run in <c>finally</c>.
/// </summary>
internal static class UatRunner
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EventLogTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Scenarios built so far: the stack and enrollment smoke (P19) for each mode.</summary>
    internal static IReadOnlyDictionary<string, IUatScenario> ScenarioRegistry { get; } =
        new Dictionary<string, IUatScenario>(StringComparer.Ordinal)
        {
            ["process"] = new StackScenario("process"),
            ["service"] = new StackScenario("service"),
            ["msi"] = new StackScenario("msi"),
        };

    /// <summary>Runs a verb against the real machine.</summary>
    public static Task<int> RunAsync(UatArgs args, CancellationToken ct)
    {
        var launchedByAgent = Environment.GetEnvironmentVariable("CLAUDECODE") is not null;

        // The cleanup msiexec /x writes its verbose log next to the run's other logs (spec step 8, msi-cleanup.log).
        var paths = new RunPaths(args.RepoRoot, args.RunId);
        var actions = new WindowsMachineActions(Path.Combine(paths.Logs, "msi-cleanup.log"));
        return RunWithAsync(args, actions, new MachineProbe(), launchedByAgent, new GitWorkTree(), ScenarioRegistry, ct);
    }

    /// <summary>Runs a verb with explicit machine seams, so the guards and cleanup can be tested with fakes.</summary>
    internal static async Task<int> RunWithAsync(
        UatArgs args,
        IMachineActions actions,
        IMachineProbe probe,
        bool launchedByAgent,
        IWorkTree workTree,
        IReadOnlyDictionary<string, IUatScenario> scenarios,
        CancellationToken ct)
    {
        var paths = new RunPaths(args.RepoRoot, args.RunId);
        Console.WriteLine($"run folder: {paths.RunDir}");
        return args.Verb switch
        {
            "preflight" => await PreflightVerbAsync(args, paths, actions, probe, launchedByAgent, workTree, ct),
            "cleanup" => await CleanupVerbAsync(args, paths, actions, probe, launchedByAgent),
            "stack-only" => await StackOnlyVerbAsync(args, paths, actions, probe, launchedByAgent, workTree, ct),
            _ => await RunVerbAsync(args, paths, actions, probe, launchedByAgent, workTree, scenarios, ct),
        };
    }

    private static int Refuse(string message)
    {
        Console.Error.WriteLine(message);
        return UatExitCodes.Refused;
    }

    /// <summary>
    /// The tree check for an agent run (D-S239). Null for a user run, which never asks git. Otherwise the check's result,
    /// so the run guard can quote a failure.
    /// </summary>
    private static async Task<Result<bool>?> TreeCheckAsync(bool launchedByAgent, IWorkTree workTree, string repoRoot, CancellationToken ct)
    {
        if (!launchedByAgent)
        {
            return null;
        }

        return await workTree.IsCleanAsync(repoRoot, ct);
    }

    /// <summary>The tree state for preflight and the record: null for user runs and when the check failed; else the clean value.</summary>
    private static bool? TreeState(Result<bool>? check) => check is { IsSuccess: true } clean ? clean.Value : null;

    private static async Task<int> PreflightVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent, IWorkTree workTree, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.RunDir);
        var treeClean = TreeState(await TreeCheckAsync(launchedByAgent, workTree, args.RepoRoot, ct));
        var started = DateTime.UtcNow;
        var report = await new PreflightChecks(probe, actions, paths.RunDir, null, launchedByAgent, treeClean)
            .RunAsync(PreflightMode.Preflight, ct);

        var pass = report.PassesFor(PreflightMode.Run);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var check in report.Checks)
        {
            var requirement = check.RequiredFor(PreflightMode.Run) ? "required for run" : "reported";
            evidence[check.Name] = $"{(check.Passed ? "pass" : "fail")} ({requirement}): {check.Detail}";
        }

        var step = new StepResult("U0", "Preflight", pass ? "pass" : "fail", evidence, null, 0);
        var overall = pass ? "pass" : "fail";
        return await PersistAsync(args, paths, started, "preflight", null, [step], CleanupSummary.NotRun(),
            [], overall, pass ? UatExitCodes.Pass : UatExitCodes.Fail, launchedByAgent, treeClean, actions, new Redactor());
    }

    /// <summary>
    /// The stack-only smoke (P19): needs no elevation and is allowed under CLAUDECODE (D-S239). Refuses when Docker is down, port 5672
    /// is busy, the dev certificate is untrusted, or a <c>meridian-uat-*</c> container exists. Writes <c>launch.json</c>
    /// and changes nothing outside Docker and the run folder.
    /// </summary>
    private static async Task<int> StackOnlyVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent, IWorkTree workTree, CancellationToken ct)
    {
        // The manifest is then written only by this process: a planted or stale manifest must never reach cleanup (N1).
        if (Directory.Exists(paths.RunDir))
        {
            return Refuse("stack-only refused: run folder already exists");
        }

        var treeClean = TreeState(await TreeCheckAsync(launchedByAgent, workTree, args.RepoRoot, ct));
        var preflight = await new PreflightChecks(probe, actions, paths.RunDir, null, launchedByAgent, treeClean)
            .RunAsync(PreflightMode.StackOnly, ct);
        if (!preflight.PassesFor(PreflightMode.StackOnly))
        {
            var failed = preflight.Checks.Where(c => c.RequiredFor(PreflightMode.StackOnly) && !c.Passed)
                .Select(c => $"{c.Name}: {c.Detail}");
            return Refuse("stack-only refused: " + string.Join("; ", failed));
        }

        var existing = await CommandRunner.RunAsync(
            "docker", ["ps", "-a", "--filter", "name=meridian-uat-", "--format", "{{.Names}}"], GitTimeout, null, null, ct: ct);
        if (existing.IsFailure || existing.Value.ExitCode != 0)
        {
            return Refuse("stack-only refused: docker ps failed");
        }

        var names = existing.Value.StandardOutput.Trim();
        if (names.Length > 0)
        {
            return Refuse("stack-only refused: meridian-uat container exists: " + names.ReplaceLineEndings(", "));
        }

        var started = DateTime.UtcNow;
        Directory.CreateDirectory(paths.RunDir);
        var launch = await WriteLaunchAsync(paths, args, "stack-only", null, launchedByAgent, actions.IsElevated(), started);
        if (launch is not null)
        {
            return Refuse("stack-only refused: " + launch);
        }

        var steps = new StepRecorder();
        var warnings = new List<string>();
        var manifest = new CleanupManifest(paths.Manifest);
        var redactor = new Redactor();
        string? internalError = null;
        CleanupSummary cleanup;

        try
        {
            var scenario = new StackScenario("stack-only");
            var context = new RunContext(paths, args.RunId, "stack-only", manifest, redactor);
            await steps.RunAsync("U1", "stack-only", c => scenario.RunAsync(context, c), ct);
        }
#pragma warning disable CA1031 // Internal errors are recorded, then cleanup still runs (exit 4).
        catch (Exception ex)
        {
            internalError = $"{ex.GetType().FullName}: {ex.Message}";
        }
#pragma warning restore CA1031
        finally
        {
            cleanup = await CleanupGuardedAsync(paths, actions, probe, warnings, entry => IsStackOnlyEntry(paths, entry));
        }

        if (internalError is not null)
        {
            warnings.Add("internal error: " + internalError);
        }

        var overall = internalError is not null ? "fail" : Overall(steps.Steps, cleanup);
        var exitCode = cleanup.Status == "footprint-left" ? UatExitCodes.FootprintLeft
            : internalError is not null ? UatExitCodes.InternalError
            : overall == "pass" ? UatExitCodes.Pass
            : UatExitCodes.Fail;

        return await PersistAsync(args, paths, started, "stack-only", null, steps.Steps, cleanup, warnings,
            overall, exitCode, launchedByAgent, treeClean, actions, redactor);
    }

    private static async Task<int> RunVerbAsync(
        UatArgs args,
        RunPaths paths,
        IMachineActions actions,
        IMachineProbe probe,
        bool launchedByAgent,
        IWorkTree workTree,
        IReadOnlyDictionary<string, IUatScenario> scenarios,
        CancellationToken ct)
    {
        // Guard chain: every refusal happens before the first machine change, launch record or manifest entry.
        if (!args.AcceptMachineChanges)
        {
            return Refuse("run refused: --accept-machine-changes is missing");
        }

        if (!actions.IsElevated())
        {
            return Refuse("run refused: not elevated");
        }

        var tree = await TreeCheckAsync(launchedByAgent, workTree, args.RepoRoot, ct);
        var treeClean = TreeState(tree);
        if (tree is { IsFailure: true } failed)
        {
            return Refuse($"run refused: tree check failed (D-S239): {failed.Error}");
        }

        if (treeClean == false)
        {
            return Refuse("run refused: agent run needs a clean tree (D-S239)");
        }

        if (LaunchRecord.Exists(paths) || File.Exists(paths.Manifest))
        {
            return Refuse("run refused: the run folder was already used");
        }

        var footprint = probe.Scan();
        if (!footprint.IsEmpty)
        {
            return Refuse("run refused: agent footprint present: " + string.Join(", ", footprint.Items));
        }

        var preflight = await new PreflightChecks(probe, actions, paths.RunDir, args.Mode, launchedByAgent, treeClean)
            .RunAsync(PreflightMode.Run, ct);
        if (!preflight.PassesFor(PreflightMode.Run))
        {
            return Refuse("run refused: preflight does not pass for run (run -Preflight for details)");
        }

        var started = DateTime.UtcNow;
        Directory.CreateDirectory(paths.RunDir);
        var launch = await WriteLaunchAsync(paths, args, "run", args.Mode, launchedByAgent, actions.IsElevated(), started);
        if (launch is not null)
        {
            return Refuse("run refused: " + launch);
        }

        var steps = new StepRecorder();
        var warnings = new List<string>();
        var manifest = new CleanupManifest(paths.Manifest);
        var redactor = new Redactor(); // One redactor per run: child output, step evidence and results.* all pass through it.
        string? internalError = null;
        CleanupSummary cleanup = CleanupSummary.NotRun();
        StepResult u9;

        try
        {
            var snapshot = RootSnapshot.Take();
            if (snapshot.IsFailure)
            {
                internalError = snapshot.Error;
            }
            else
            {
                var saved = snapshot.Value.Save(paths.RootBefore);
                if (saved.IsFailure)
                {
                    internalError = saved.Error;
                }
            }

            if (internalError is null)
            {
                if (scenarios.TryGetValue(args.Mode!, out var scenario))
                {
                    var context = new RunContext(paths, args.RunId, args.Mode!, manifest, redactor);
                    await steps.RunAsync("U1", scenario.Mode, c => scenario.RunAsync(context, c), ct);
                }
                else
                {
                    steps.AddSkipped("U1", args.Mode!, "scenario not built yet (P19)");
                }
            }
        }
#pragma warning disable CA1031 // Internal errors are recorded, then cleanup still runs (exit 4).
        catch (Exception ex)
        {
            internalError = $"{ex.GetType().FullName}: {ex.Message}";
        }
#pragma warning restore CA1031
        finally
        {
            // The events go out before the manifest undo, which deletes the event source (U9).
            await ExportAgentEventsAsync(paths, started, redactor, warnings);
            cleanup = await CleanupGuardedAsync(paths, actions, probe, warnings);
            u9 = CleanupAndScan(paths, cleanup, redactor, warnings);
        }

        if (internalError is not null)
        {
            warnings.Add("internal error: " + internalError);
        }

        var allSteps = steps.Steps.Append(u9).ToList();
        var overall = internalError is not null ? "fail" : Overall(allSteps, cleanup);
        var exitCode = cleanup.Status == "footprint-left" ? UatExitCodes.FootprintLeft
            : internalError is not null ? UatExitCodes.InternalError
            : overall == "pass" ? UatExitCodes.Pass
            : UatExitCodes.Fail;

        var serviceRun = args.Mode == "service" ? ServiceInfoFor(allSteps) : null;
        var msiRun = args.Mode == "msi" ? MsiInfoFor(allSteps) : null;
        return await PersistAsync(args, paths, started, "run", args.Mode, allSteps, cleanup, warnings,
            overall, exitCode, launchedByAgent, treeClean, actions, redactor, serviceRun, msiRun);
    }

    /// <summary>
    /// The msi object of <c>results.json</c> (P23), read from the <c>msi</c> evidence the MSI legs wrote under U1.
    /// A part the run did not reach is null, -1 or empty.
    /// </summary>
    internal static MsiRunInfo MsiInfoFor(IReadOnlyList<StepResult> steps)
    {
        var evidence = steps.FirstOrDefault(s => s.Id == "U1")?.Evidence;
        var msi = evidence is not null && evidence.TryGetValue("msi", out var value) ? value as IReadOnlyDictionary<string, object?> : null;
        if (msi is null)
        {
            return new MsiRunInfo(null, null, -1, -1, []);
        }

        return new MsiRunInfo(
            msi.GetValueOrDefault("productCode") as string,
            msi.GetValueOrDefault("productVersion") as string,
            msi.GetValueOrDefault("installExitCode") is int install ? install : -1,
            msi.GetValueOrDefault("uninstallExitCode") is int uninstall ? uninstall : -1,
            msi.GetValueOrDefault("residue") is IEnumerable<string> residue ? residue.ToList() : []);
    }

    /// <summary>
    /// The service object of <c>results.json</c> (P22). Runs after cleanup: the TLS leaf's thumbprint (from U10 in
    /// the U1 evidence) must be absent from <c>LocalMachine\Root</c> by then. The U1 evidence is missing when the
    /// scenario failed, so the flags are then false, which means "not shown", not "failed".
    /// </summary>
    private static ServiceRunInfo ServiceInfoFor(IReadOnlyList<StepResult> steps)
    {
        var evidence = steps.FirstOrDefault(s => s.Id == "U1")?.Evidence;
        var decoyRemoved = evidence is not null && evidence.TryGetValue("U3", out var u3) && u3 is "pass";
        var tlsThumbprint = evidence is not null && evidence.TryGetValue("tlsThumbprint", out var tls) ? tls as string : null;
        var tlsRootRemoved = false;
        if (tlsThumbprint is not null)
        {
            var root = RootSnapshot.Take();
            tlsRootRemoved = root.IsSuccess && !root.Value.Thumbprints.Contains(tlsThumbprint, StringComparer.OrdinalIgnoreCase);
        }

        return new ServiceRunInfo(AgentWindowsFacts.ServiceName, "LocalSystem", decoyRemoved, tlsRootRemoved);
    }

    private static async Task<int> CleanupVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent)
    {
        var given = Path.GetFullPath(args.RunDir!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(given, paths.RunDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("cleanup refused: --run-dir does not match this run folder");
        }

        if (!Directory.Exists(paths.RunDir))
        {
            return Refuse("cleanup refused: run folder does not exist");
        }

        // D-S239: an agent may clean up only a folder whose launch.json says it was launched by an agent. This is checked
        // before the manifest is read.
        if (launchedByAgent)
        {
            var launch = LaunchRecord.Load(paths);
            if (launch.IsFailure)
            {
                return Refuse($"cleanup refused: {launch.Error}; the user runs this cleanup (D-S239)");
            }

            if (launch.Value.LaunchedBy != "agent" || launch.Value.RunId != args.RunId)
            {
                return Refuse("cleanup refused: not an agent run; the user runs this cleanup (D-S239)");
            }
        }

        if (!actions.IsElevated())
        {
            var manifest = CleanupManifest.Load(paths.Manifest);
            if (manifest.IsSuccess && manifest.Value.Any(RequiresElevation))
            {
                return Refuse("cleanup refused: not elevated and the manifest holds machine items; run cleanup elevated");
            }
        }

        var warnings = new List<string>();
        var cleanup = await CleanupGuardedAsync(paths, actions, probe, warnings);
        var written = ResultsWriter.WriteCleanup(cleanup, paths.RunDir, new Redactor());
        if (written.IsFailure)
        {
            await Console.Error.WriteLineAsync(written.Error);
            return UatExitCodes.InternalError;
        }

        foreach (var warning in warnings)
        {
            Console.WriteLine($"warning: {warning}");
        }

        Console.WriteLine($"cleanup: {cleanup.Status} (undone {cleanup.Undone})");
        return cleanup.Status == "clean" ? UatExitCodes.Pass : UatExitCodes.FootprintLeft;
    }

    /// <summary>True when undoing this entry needs an elevated process (service, HKLM, LocalMachine store, machine directory).</summary>
    private static bool RequiresElevation(CleanupEntry entry) => entry switch
    {
        ServiceEntry or RegistryKeyEntry or RegistryValueEntry or CertStoreEntry or EventSourceEntry or MsiProductEntry => true,
        CertificateEntry c => string.Equals(c.StoreLocation, "LocalMachine", StringComparison.Ordinal),
        CngKeyEntry k => k.MachineKey,
        DirectoryEntry d => AgentDirectories.IsMachineDirectory(d.Path),
        _ => false,
    };

    /// <summary>
    /// <see cref="CleanupAsync"/> with a last resort: an exception from cleanup is recorded as a footprint-left
    /// summary, so the caller still writes results.json.
    /// </summary>
    private static async Task<CleanupSummary> CleanupGuardedAsync(
        RunPaths paths, IMachineActions actions, IMachineProbe probe, List<string> warnings, Func<CleanupEntry, bool>? keep = null)
    {
#pragma warning disable CA1031 // Cleanup must never escape: results.json is written in every case (D-S225).
        try
        {
            return await CleanupAsync(paths, actions, probe, warnings, keep);
        }
        catch (Exception ex)
        {
            return new CleanupSummary("footprint-left", 0,
                [new CleanupFailure("cleanup", paths.RunDir, $"{ex.GetType().Name}: {ex.Message}")], [], [], []);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Exports the agent's Application events from this run to <c>logs\eventlog.txt</c>, redacted. Runs before the manifest
    /// undo, which deletes the event source. A failed export is a warning: U9 does not depend on it.
    /// </summary>
    private static async Task ExportAgentEventsAsync(RunPaths paths, DateTime startedUtc, Redactor redactor, List<string> warnings)
    {
        var since = startedUtc.ToString("O", CultureInfo.InvariantCulture);
        var query = $"*[System[Provider[@Name='Meridian Console Agent'] and TimeCreated[@SystemTime>='{since}']]]";
        var result = await CommandRunner.RunAsync(
            "wevtutil", ["qe", "Application", $"/q:{query}", "/f:text"], EventLogTimeout, redactor, null, ct: CancellationToken.None);
        if (result.IsFailure || result.Value.ExitCode != 0)
        {
            warnings.Add("agent event export failed; logs\\eventlog.txt was not written");
            return;
        }

        try
        {
            Directory.CreateDirectory(paths.Logs);
            await File.WriteAllTextAsync(
                Path.Combine(paths.Logs, "eventlog.txt"),
                redactor.Redact(result.Value.StandardOutput),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"agent event export could not be written ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// U9 after the manifest undo: the undo must have no failures, the probe must be empty and the Root diff must have
    /// no additions or removals. Then the secret scan runs, and the run's Nodes CA, agent state and agent data folders are
    /// deleted (they are not manifest entries; they are only ever under the run folder).
    /// </summary>
    internal static StepResult CleanupAndScan(RunPaths paths, CleanupSummary cleanup, Redactor redactor, List<string> warnings)
    {
        var clock = Stopwatch.StartNew();
        var failures = new List<string>();
        if (cleanup.Failed.Count > 0)
        {
            failures.Add($"manifest undo had {cleanup.Failed.Count} failure(s)");
        }

        if (cleanup.Remaining.Count > 0)
        {
            failures.Add("probe is not empty after cleanup: " + string.Join(", ", cleanup.Remaining));
        }

        if (cleanup.RootAdded.Count > 0 || cleanup.RootRemoved.Count > 0)
        {
            failures.Add($"Root changed: {cleanup.RootAdded.Count} added, {cleanup.RootRemoved.Count} removed");
        }

        var scan = SecretScan.Run(paths, redactor);
        failures.AddRange(scan.Failures);
        warnings.AddRange(scan.Warnings);

        var directoryFailures = DeleteRunDirectories(paths);
        failures.AddRange(directoryFailures);

        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cleanup"] = cleanup.Status,
            ["undone"] = cleanup.Undone,
            ["filesScanned"] = scan.FilesScanned,
            ["secretFindings"] = scan.Failures.Count,
            ["passwordWarnings"] = scan.Warnings.Count,
        };
        clock.Stop();
        return failures.Count == 0
            ? new StepResult("U9", "Cleanup and scan", "pass", evidence, null, (long)clock.Elapsed.TotalMilliseconds)
            : new StepResult("U9", "Cleanup and scan", "fail", evidence, string.Join("; ", failures), (long)clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Deletes the run's Nodes CA, agent state and agent data folders. Each path is checked against the run folder first.</summary>
    private static List<string> DeleteRunDirectories(RunPaths paths)
    {
        var failures = new List<string>();
        foreach (var dir in new[] { paths.Ca, paths.AgentState, paths.AgentData })
        {
            var valid = paths.Validate(dir);
            if (valid.IsFailure)
            {
                failures.Add(valid.Error!);
                continue;
            }

            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"could not delete {Path.GetFileName(dir)} ({ex.GetType().Name})");
            }
        }

        return failures;
    }

    /// <summary>
    /// The entries stack-only may undo: the containers and child processes it starts, and directories under the run
    /// folder. Machine items (services, certificates, registry, event sources) are never undone by stack-only (N1).
    /// </summary>
    private static bool IsStackOnlyEntry(RunPaths paths, CleanupEntry entry) => entry switch
    {
        ContainerEntry or ProcessEntry => true,
        DirectoryEntry d => paths.Validate(d.Path).IsSuccess,
        _ => false,
    };

    /// <summary>Undoes the manifest, rescans the machine, and compares the Root snapshot.</summary>
    private static async Task<CleanupSummary> CleanupAsync(
        RunPaths paths, IMachineActions actions, IMachineProbe probe, List<string> warnings, Func<CleanupEntry, bool>? keep = null)
    {
        var loaded = CleanupManifest.Load(paths.Manifest);
        if (loaded.IsFailure)
        {
            return new CleanupSummary("footprint-left", 0,
                [new CleanupFailure("manifest", paths.Manifest, loaded.Error)], [], [], []);
        }

        var entries = keep is null ? loaded.Value : loaded.Value.Where(keep).ToList();
        var report = await new CleanupEngine(actions, paths).UndoAsync(entries, CancellationToken.None);
        var failed = report.Failed.ToList();
        var rootAdded = new List<string>();
        var rootRemoved = new List<string>();

        if (File.Exists(paths.RootBefore))
        {
            var before = RootSnapshot.Load(paths.RootBefore);
            var after = RootSnapshot.Take();
            if (before.IsSuccess && after.IsSuccess)
            {
                var diff = RootSnapshot.Diff(before.Value, after.Value, FindInRoot);
                rootAdded.AddRange(diff.RootAdded);
                rootRemoved.AddRange(diff.RootRemoved);
                warnings.AddRange(diff.Warnings);
            }
            else
            {
                failed.Add(new CleanupFailure("rootSnapshot", paths.RootBefore,
                    before.IsFailure ? before.Error : after.Error));
            }
        }

        IReadOnlyList<string> remaining;
#pragma warning disable CA1031 // A failed rescan is a footprint-left finding, not a crash.
        try
        {
            remaining = probe.Scan().Items;
        }
        catch (Exception ex)
        {
            failed.Add(new CleanupFailure("probe", "scan", $"{ex.GetType().Name}: {ex.Message}"));
            remaining = [];
        }
#pragma warning restore CA1031

        var clean = failed.Count == 0 && remaining.Count == 0 && rootAdded.Count == 0 && rootRemoved.Count == 0;
        return new CleanupSummary(clean ? "clean" : "footprint-left", report.Undone, failed, remaining, rootAdded, rootRemoved);
    }

    /// <summary>A copy of the LocalMachine\Root certificate with this thumbprint, or null. The caller disposes it.</summary>
    private static X509Certificate2? FindInRoot(string thumbprint)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        try
        {
            return found.Count > 0 ? new X509Certificate2(found[0]) : null;
        }
        finally
        {
            foreach (var cert in found)
            {
                cert.Dispose();
            }
        }
    }

    private static string Overall(IReadOnlyList<StepResult> steps, CleanupSummary cleanup)
    {
        if (steps.Any(s => s.Status == "fail") || cleanup.Status == "footprint-left")
        {
            return "fail";
        }

        if (cleanup.Status != "clean" || steps.Any(s => s.Status == "inconclusive"))
        {
            return "inconclusive";
        }

        // A run where every step was skipped proves nothing, so it is not a pass.
        return steps.Any(s => s.Status == "pass") && steps.All(s => s.Status is "pass" or "skipped")
            ? "pass"
            : "inconclusive";
    }

    private static async Task<int> PersistAsync(
        UatArgs args,
        RunPaths paths,
        DateTime started,
        string verb,
        string? mode,
        IReadOnlyList<StepResult> steps,
        CleanupSummary cleanup,
        IReadOnlyList<string> warnings,
        string overall,
        int exitCode,
        bool launchedByAgent,
        bool? agentTreeClean,
        IMachineActions actions,
        Redactor redactor,
        ServiceRunInfo? serviceRun = null,
        MsiRunInfo? msiRun = null)
    {
        var (sha, dirty) = await ReadGitAsync(args.RepoRoot);
        var results = new UatResults(
            SchemaVersion,
            args.RunId,
            verb,
            mode,
            started,
            DateTime.UtcNow,
            sha,
            dirty,
            Environment.OSVersion.Version.Build,
            Environment.Version.ToString(),
            launchedByAgent ? "agent" : "user",
            actions.IsElevated(),
            overall,
            exitCode,
            steps,
            cleanup,
            warnings,
            launchedByAgent ? new AgentRunInfo("D-S239", "CLAUDECODE", agentTreeClean == true) : null,
            serviceRun,
            msiRun);

        var written = ResultsWriter.Write(results, paths.RunDir, redactor);
        if (written.IsFailure)
        {
            await Console.Error.WriteLineAsync(written.Error);
            return UatExitCodes.InternalError;
        }

        Console.WriteLine($"results: {Path.Combine(paths.RunDir, "results.json")}");
        return exitCode;
    }

    /// <summary>
    /// Writes <c>launch.json</c> (D-S239) before the first manifest entry. Returns null when written, else the refusal
    /// text. The record is never overwritten.
    /// </summary>
    private static async Task<string?> WriteLaunchAsync(
        RunPaths paths, UatArgs args, string verb, string? mode, bool launchedByAgent, bool elevated, DateTime started)
    {
        var head = await CommandRunner.RunAsync("git", ["rev-parse", "HEAD"], GitTimeout, null, args.RepoRoot);
        var sha = head.IsSuccess && head.Value.ExitCode == 0 ? head.Value.StandardOutput.Trim() : "unknown";
        var record = new LaunchRecord(
            SchemaVersion, args.RunId, verb, mode, launchedByAgent ? "agent" : "user", elevated, sha, started);
        var written = LaunchRecord.Write(paths, record);
        return written.IsFailure ? written.Error : null;
    }

    private static async Task<(string Sha, bool Dirty)> ReadGitAsync(string repoRoot)
    {
        var head = await CommandRunner.RunAsync("git", ["rev-parse", "HEAD"], GitTimeout, null, repoRoot);
        var status = await CommandRunner.RunAsync("git", ["status", "--porcelain", "--", "src", "tests", "scripts"], GitTimeout, null, repoRoot);
        var sha = head.IsSuccess && head.Value.ExitCode == 0 ? head.Value.StandardOutput.Trim() : "unknown";
        var dirty = !status.IsSuccess || status.Value.ExitCode != 0 || status.Value.StandardOutput.Trim().Length > 0;
        return (sha, dirty);
    }
}
