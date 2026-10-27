using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Preflight;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Results;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat;

/// <summary>
/// Dispatches the verbs and owns the guard chain (D-S225). <c>run</c> refuses before any machine change
/// unless it is elevated, has <c>--accept-machine-changes</c>, is not launched by an agent (CLAUDECODE),
/// has no agent footprint, and passes preflight for run. Cleanup and results always run in <c>finally</c>.
/// </summary>
internal static class UatRunner
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

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
        return RunWithAsync(args, new WindowsMachineActions(), new MachineProbe(), launchedByAgent, ScenarioRegistry, ct);
    }

    /// <summary>Runs a verb with explicit machine seams, so the guards and cleanup can be tested with fakes.</summary>
    internal static async Task<int> RunWithAsync(
        UatArgs args,
        IMachineActions actions,
        IMachineProbe probe,
        bool launchedByAgent,
        IReadOnlyDictionary<string, IUatScenario> scenarios,
        CancellationToken ct)
    {
        var paths = new RunPaths(args.RepoRoot, args.RunId);
        Console.WriteLine($"run folder: {paths.RunDir}");
        return args.Verb switch
        {
            "preflight" => await PreflightVerbAsync(args, paths, actions, probe, launchedByAgent, ct),
            "cleanup" => await CleanupVerbAsync(args, paths, actions, probe, launchedByAgent),
            "stack-only" => await StackOnlyVerbAsync(args, paths, actions, probe, launchedByAgent, ct),
            _ => await RunVerbAsync(args, paths, actions, probe, launchedByAgent, scenarios, ct),
        };
    }

    private static int Refuse(string message)
    {
        Console.Error.WriteLine(message);
        return UatExitCodes.Refused;
    }

    private static async Task<int> PreflightVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.RunDir);
        var started = DateTime.UtcNow;
        var report = await new PreflightChecks(probe, actions, paths.RunDir, null, launchedByAgent)
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
            [], overall, pass ? UatExitCodes.Pass : UatExitCodes.Fail, launchedByAgent, actions, new Redactor());
    }

    /// <summary>
    /// The stack-only smoke (P19): needs no elevation and allows CLAUDECODE. Refuses when Docker is down, port 5672
    /// is busy, the dev certificate is untrusted, or a <c>meridian-uat-*</c> container exists. Changes nothing outside
    /// Docker and the run folder.
    /// </summary>
    private static async Task<int> StackOnlyVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent, CancellationToken ct)
    {
        var preflight = await new PreflightChecks(probe, actions, paths.RunDir, null, launchedByAgent)
            .RunAsync(PreflightMode.StackOnly, ct);
        if (!preflight.PassesFor(PreflightMode.StackOnly))
        {
            var failed = preflight.Checks.Where(c => c.RequiredFor(PreflightMode.StackOnly) && !c.Passed)
                .Select(c => $"{c.Name}: {c.Detail}");
            return Refuse("stack-only refused: " + string.Join("; ", failed));
        }

        var existing = await CommandRunner.RunAsync(
            "docker", ["ps", "-a", "--filter", "name=meridian-uat-", "--format", "{{.Names}}"], GitTimeout, null, null, ct);
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
            cleanup = await CleanupGuardedAsync(paths, actions, probe, warnings);
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
            overall, exitCode, launchedByAgent, actions, redactor);
    }

    private static async Task<int> RunVerbAsync(
        UatArgs args,
        RunPaths paths,
        IMachineActions actions,
        IMachineProbe probe,
        bool launchedByAgent,
        IReadOnlyDictionary<string, IUatScenario> scenarios,
        CancellationToken ct)
    {
        // Guard chain: every refusal happens before the first machine change or manifest entry.
        if (launchedByAgent)
        {
            return Refuse("run refused: CLAUDECODE is set; only the user launches run (D-S225)");
        }

        if (!args.AcceptMachineChanges)
        {
            return Refuse("run refused: --accept-machine-changes is missing");
        }

        if (!actions.IsElevated())
        {
            return Refuse("run refused: not elevated");
        }

        var footprint = probe.Scan();
        if (!footprint.IsEmpty)
        {
            return Refuse("run refused: agent footprint present: " + string.Join(", ", footprint.Items));
        }

        var preflight = await new PreflightChecks(probe, actions, paths.RunDir, args.Mode, launchedByAgent)
            .RunAsync(PreflightMode.Run, ct);
        if (!preflight.PassesFor(PreflightMode.Run))
        {
            return Refuse("run refused: preflight does not pass for run (run -Preflight for details)");
        }

        var started = DateTime.UtcNow;
        Directory.CreateDirectory(paths.RunDir);
        var steps = new StepRecorder();
        var warnings = new List<string>();
        var manifest = new CleanupManifest(paths.Manifest);
        var redactor = new Redactor(); // One redactor per run: child output, step evidence and results.* all pass through it.
        string? internalError = null;
        CleanupSummary cleanup = CleanupSummary.NotRun();

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
            cleanup = await CleanupGuardedAsync(paths, actions, probe, warnings);
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

        return await PersistAsync(args, paths, started, "run", args.Mode, steps.Steps, cleanup, warnings,
            overall, exitCode, launchedByAgent, actions, redactor);
    }

    private static async Task<int> CleanupVerbAsync(
        UatArgs args, RunPaths paths, IMachineActions actions, IMachineProbe probe, bool launchedByAgent)
    {
        // D-S225: an agent may not run cleanup. This is checked before the manifest is read.
        if (launchedByAgent)
        {
            return Refuse("cleanup refused: CLAUDECODE is set; only the user runs cleanup (D-S225)");
        }

        var given = Path.GetFullPath(args.RunDir!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(given, paths.RunDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("cleanup refused: --run-dir does not match this run folder");
        }

        if (!Directory.Exists(paths.RunDir))
        {
            return Refuse("cleanup refused: run folder does not exist");
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
            Console.Error.WriteLine(written.Error);
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
        ServiceEntry or RegistryKeyEntry or RegistryValueEntry or CertStoreEntry or EventSourceEntry => true,
        CertificateEntry c => string.Equals(c.StoreLocation, "LocalMachine", StringComparison.Ordinal),
        CngKeyEntry k => k.MachineKey,
        DirectoryEntry d => AgentDirectories.IsMachineAgentDirectory(d.Path),
        _ => false,
    };

    /// <summary>
    /// <see cref="CleanupAsync"/> with a last resort: an exception from cleanup is recorded as a footprint-left
    /// summary, so the caller still writes results.json.
    /// </summary>
    private static async Task<CleanupSummary> CleanupGuardedAsync(
        RunPaths paths, IMachineActions actions, IMachineProbe probe, List<string> warnings)
    {
#pragma warning disable CA1031 // Cleanup must never escape: results.json is written in every case (D-S225).
        try
        {
            return await CleanupAsync(paths, actions, probe, warnings);
        }
        catch (Exception ex)
        {
            return new CleanupSummary("footprint-left", 0,
                [new CleanupFailure("cleanup", paths.RunDir, $"{ex.GetType().Name}: {ex.Message}")], [], [], []);
        }
#pragma warning restore CA1031
    }

    /// <summary>Undoes the manifest, rescans the machine, and compares the Root snapshot.</summary>
    private static async Task<CleanupSummary> CleanupAsync(RunPaths paths, IMachineActions actions, IMachineProbe probe, List<string> warnings)
    {
        var loaded = CleanupManifest.Load(paths.Manifest);
        if (loaded.IsFailure)
        {
            return new CleanupSummary("footprint-left", 0,
                [new CleanupFailure("manifest", paths.Manifest, loaded.Error)], [], [], []);
        }

        var report = await new CleanupEngine(actions, paths).UndoAsync(loaded.Value, CancellationToken.None);
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
        IMachineActions actions,
        Redactor redactor)
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
            warnings);

        var written = ResultsWriter.Write(results, paths.RunDir, redactor);
        if (written.IsFailure)
        {
            Console.Error.WriteLine(written.Error);
            return UatExitCodes.InternalError;
        }

        Console.WriteLine($"results: {Path.Combine(paths.RunDir, "results.json")}");
        return exitCode;
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
