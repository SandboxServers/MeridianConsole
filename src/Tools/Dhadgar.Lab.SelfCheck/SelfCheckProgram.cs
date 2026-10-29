using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.SelfCheck;

/// <summary>
/// The entry point of <c>selfcheck [--only &lt;id&gt;] [--keep] [--recovery]</c> (L55). Exit code 0 only when every lab
/// mutant is <c>caught</c>. A named class, not top-level statements, so the test project can reference this assembly
/// beside the Nodes assembly (whose entry type is also <c>Program</c>).
/// </summary>
public static class SelfCheckProgram
{
    /// <summary>Runs the command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    public static int Main(string[] args)
    {
        Result<SelfCheckOptions> parsed = SelfCheckOptions.Parse(args);
        if (!parsed.IsSuccess)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        SelfCheckOptions options = parsed.Value!;
        if (options.Recovery)
        {
            // The crash-recovery check arrives in step 4 of L55.
            Console.Error.WriteLine("selfcheck: --recovery is not implemented yet");
            return 2;
        }

        LabPaths paths = LabPaths.Create(new ConfigurationBuilder().Build());
        string? repoRoot = string.IsNullOrWhiteSpace(paths.RepoRoot)
            ? SelfCheckRunner.FindRepoRoot(AppContext.BaseDirectory)
            : paths.RepoRoot;
        if (repoRoot is null)
        {
            Console.Error.WriteLine("[Lab.NotFound] the repository root is not configured and was not found");
            return 2;
        }

        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);
        Result<IReadOnlyList<SelfCheckRow>> result = SelfCheckRunner
            .RunAsync(options, repoRoot, paths, validator, TimeProvider.System, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (result.IsFailure)
        {
            Console.Error.WriteLine(result.Error);
            return 2;
        }

        IReadOnlyList<SelfCheckRow> rows = result.Value!;
        int caught = rows.Count(r => r.Verdict == SelfCheckJudge.Caught);
        Console.WriteLine($"selfcheck: caught {caught} of {rows.Count}");
        return caught == rows.Count ? 0 : 1;
    }
}
