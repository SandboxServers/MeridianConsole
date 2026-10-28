using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab;

/// <summary>
/// Every directory the lab owns. All paths hang off <see cref="Root"/>; nothing is written elsewhere.
/// The fixed folder names only: no containment check on input paths yet. Any path taken from a tool or the network
/// must be validated (<c>..</c>, null bytes, control characters, roots outside <see cref="Root"/>, symlinks and junctions)
/// before use; that check lands with the first packet that accepts such a path.
/// </summary>
public sealed class LabPaths
{
    private LabPaths(string root, string repoRoot)
    {
        Root = root;
        RepoRoot = repoRoot;
    }

    // Root: %LOCALAPPDATA%\meridian-lab on Windows; $XDG_STATE_HOME/meridian-lab or ~/.local/state/meridian-lab elsewhere;
    // MERIDIAN_LAB_HOME overrides.
    public string Root { get; }
    public string Bin => Path.Combine(Root, "bin");
    public string Builds => Path.Combine(Root, "builds");        // builds/<build8>/<project>
    public string Logs => Path.Combine(Root, "logs");            // logs/<process>.jsonl
    public string Audit => Path.Combine(Root, "audit");          // audit/audit-<yyyyMMdd>.jsonl
    public string UatRuns => Path.Combine(Root, "uat-runs");
    public string Config => Path.Combine(Root, "config");        // config/<service>.json overlays
    public string State => Path.Combine(Root, "state");          // state/nodes-ca, targets.json, faults.json, ...
    public string Results => Path.Combine(Root, "results");      // results/lab-results.db (L25)
    public string Triage => Path.Combine(Root, "triage");        // triage/ledger.md, triage/drafts/<bucket>.md (L26)
    public string Playwright => Path.Combine(Root, "playwright");// browsers (L35)
    public string FaultBackups => Path.Combine(Root, "fault-backups"); // corrupted-file originals (L20)
    public string Jobs => Path.Combine(Root, "jobs");            // jobs/<jobId>/ (L27)
    public string PidFile => Path.Combine(Root, "daemon.pid");
    public string RepoRoot { get; }                              // Lab:RepoRoot, set by install.ps1; empty when unset

    /// <summary>
    /// Builds the layout under an explicit root, for example <c>LabDaemonOptions.DataRoot</c>. Throws
    /// <see cref="ArgumentException"/> for an empty or malformed root; callers validate first.
    /// </summary>
    public static LabPaths FromRoot(string root, string? repoRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new LabPaths(Path.GetFullPath(root), repoRoot ?? string.Empty);
    }

    /// <summary>
    /// Resolves the lab root from <see cref="LabDefaults.HomeEnvVar"/> or the platform default.
    /// </summary>
    public static LabPaths Create(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var home = Environment.GetEnvironmentVariable(LabDefaults.HomeEnvVar);
        var root = string.IsNullOrWhiteSpace(home) ? DefaultRoot() : Path.GetFullPath(home);
        return new LabPaths(root, config["Lab:RepoRoot"] ?? string.Empty);
    }

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "meridian-lab");
        }

        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var stateHome = !string.IsNullOrWhiteSpace(xdgState) && Path.IsPathRooted(xdgState)
            ? xdgState
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(stateHome, "meridian-lab");
    }
}
