using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat;

/// <summary>
/// Parsed driver command line. Verbs: <c>preflight</c>, <c>stack-only</c>, <c>run</c>, <c>cleanup</c>.
/// </summary>
internal sealed record UatArgs(
    string Verb,
    string RepoRoot,
    string RunId,
    string? Mode,
    bool AcceptMachineChanges,
    string? RunDir)
{
    private static readonly Regex RunIdPattern = new(@"^\d{8}-\d{6}-[0-9a-f]{4}$", RegexOptions.CultureInvariant);
    private static readonly string[] Verbs = ["preflight", "stack-only", "run", "cleanup"];
    private static readonly string[] Modes = ["process", "service", "msi"];

    /// <summary>Parses the driver arguments. Unknown arguments and missing required switches fail.</summary>
    public static Result<UatArgs> Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return Result<UatArgs>.Failure("usage: <preflight|stack-only|run|cleanup> --repo-root <path> [--run-id <id>]");
        }

        var verb = args[0];
        if (!Verbs.Contains(verb, StringComparer.Ordinal))
        {
            return Result<UatArgs>.Failure($"unknown verb '{verb}'");
        }

        string? repoRoot = null;
        string? runId = null;
        string? mode = null;
        string? runDir = null;
        var accept = false;

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            switch (token)
            {
                case "--accept-machine-changes":
                    accept = true;
                    break;
                case "--repo-root" or "--run-id" or "--mode" or "--run-dir":
                    if (i + 1 >= args.Length)
                    {
                        return Result<UatArgs>.Failure($"{token} needs a value");
                    }

                    var value = args[++i];
                    switch (token)
                    {
                        case "--repo-root": repoRoot = value; break;
                        case "--run-id": runId = value; break;
                        case "--mode": mode = value; break;
                        default: runDir = value; break;
                    }

                    break;
                default:
                    return Result<UatArgs>.Failure($"unknown argument '{token}'");
            }
        }

        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Result<UatArgs>.Failure("--repo-root is required");
        }

        if (mode is not null && (verb != "run" || !Modes.Contains(mode, StringComparer.Ordinal)))
        {
            return Result<UatArgs>.Failure("--mode is only valid for run: process, service or msi");
        }

        if (accept && verb != "run")
        {
            return Result<UatArgs>.Failure("--accept-machine-changes is only valid for run");
        }

        if (verb == "run" && mode is null)
        {
            return Result<UatArgs>.Failure("run needs --mode process|service|msi");
        }

        if (verb == "run" && !accept)
        {
            return Result<UatArgs>.Failure("run changes this machine: pass --accept-machine-changes");
        }

        if (runDir is not null && verb != "cleanup")
        {
            return Result<UatArgs>.Failure("--run-dir is only valid for cleanup");
        }

        if (verb == "cleanup")
        {
            if (runDir is null)
            {
                return Result<UatArgs>.Failure("cleanup needs --run-dir <path>");
            }

            runId ??= Path.GetFileName(Path.GetFullPath(runDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        if (string.IsNullOrEmpty(runId))
        {
            return Result<UatArgs>.Failure("--run-id is required");
        }

        if (!RunIdPattern.IsMatch(runId))
        {
            return Result<UatArgs>.Failure("run id must match yyyyMMdd-HHmmss-xxxx (lowercase hex)");
        }

        return Result<UatArgs>.Success(new UatArgs(verb, repoRoot, runId, mode, accept, runDir));
    }
}
