using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>
/// Paths of one UAT run under <c>&lt;repo&gt;\.uat\agent-windows\&lt;runId&gt;\</c>. Every path the
/// driver writes or deletes goes through <see cref="Validate"/> (R-RUNDIR).
/// </summary>
internal sealed class RunPaths
{
    /// <summary>Creates the paths for one run.</summary>
    public RunPaths(string repoRoot, string runId)
    {
        RunDir = Path.GetFullPath(Path.Combine(repoRoot, ".uat", "agent-windows", runId));
    }

    /// <summary>The run folder.</summary>
    public string RunDir { get; }

    /// <summary>Certificate authority material (Nodes CA storage).</summary>
    public string Ca => Path.Combine(RunDir, "ca");

    /// <summary>TLS material for service and MSI modes.</summary>
    public string Tls => Path.Combine(RunDir, "tls");

    /// <summary>Published agent folder.</summary>
    public string AgentPublish => Path.Combine(RunDir, "agent");

    /// <summary>Agent state directory.</summary>
    public string AgentState => Path.Combine(RunDir, "agent-state");

    /// <summary>Agent data directory (servers, temp).</summary>
    public string AgentData => Path.Combine(RunDir, "agent-data");

    /// <summary>Child process logs.</summary>
    public string Logs => Path.Combine(RunDir, "logs");

    /// <summary>The cleanup manifest.</summary>
    public string Manifest => Path.Combine(RunDir, "cleanup-manifest.jsonl");

    /// <summary>The LocalMachine\Root thumbprint snapshot taken before the first change.</summary>
    public string RootBefore => Path.Combine(RunDir, "root-before.txt");

    /// <summary>Validates that <paramref name="path"/> is a full path under this run folder with no traversal.</summary>
    public Result<string> Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<string>.Failure("path is empty");
        }

        if (path.Any(char.IsControl))
        {
            return Result<string>.Failure("path contains a control character");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return Result<string>.Failure("path must be absolute");
        }

        var segments = path.Split(['\\', '/']);
        if (segments.Any(s => s == ".."))
        {
            return Result<string>.Failure("path contains a '..' segment");
        }

        var full = Path.GetFullPath(path);
        var root = RunDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Failure("path is outside the run folder");
        }

        return Result<string>.Success(full);
    }
}
