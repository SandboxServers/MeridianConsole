using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>
/// Whether the build inputs of the repository are unchanged (D-S239). An agent run builds only from a clean tree, so
/// the run is tied to one commit.
/// </summary>
internal interface IWorkTree
{
    /// <summary>True when the build inputs are unchanged (D-S239); Failure when git cannot tell.</summary>
    Task<Result<bool>> IsCleanAsync(string repoRoot, CancellationToken ct);
}

/// <summary>
/// The git tree check. <c>docs</c> and <c>.claude</c> are excluded: ledgers, reports and agent memory change during a
/// campaign and are not build inputs. Everything else counts, including the root build props and <c>global.json</c>.
/// </summary>
internal sealed class GitWorkTree : IWorkTree
{
    /// <summary>The git arguments of the tree check. The UAT runner brief repeats this pathspec, so a test pins it.</summary>
    internal static readonly string[] StatusArguments =
        ["status", "--porcelain=v1", "--untracked-files=all", "--", ".", ":(exclude)docs", ":(exclude).claude"];

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public async Task<Result<bool>> IsCleanAsync(string repoRoot, CancellationToken ct)
    {
        var status = await CommandRunner.RunAsync("git", StatusArguments, GitTimeout, null, repoRoot, ct: ct);
        if (status.IsFailure)
        {
            return Result<bool>.Failure($"[UAT.TreeCheck] git status failed: {status.Error}");
        }

        if (status.Value.ExitCode != 0)
        {
            return Result<bool>.Failure($"[UAT.TreeCheck] git status failed: exit {status.Value.ExitCode}");
        }

        return Result<bool>.Success(status.Value.StandardOutput.Trim().Length == 0);
    }
}
