using System.Diagnostics;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// The early warning for stale mutants (D-ML51): every committed <c>change.patch</c> must still apply to the repository
/// at HEAD. Needs <c>git</c> on the PATH; it runs in CI and never needs Docker.
/// </summary>
public sealed class MutantPatchesApplyTests
{
    [Fact]
    public void EveryMutantPatch_AppliesCheckAtHead()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(Path.Combine(root, "tools", "lab", "mutants"), root, validator);
        Assert.True(specs.IsSuccess, specs.Error);
        Assert.NotEmpty(specs.Value!);

        foreach (MutantSpec spec in specs.Value!)
        {
            string patch = Path.Combine(spec.Folder, MutantSpecLoader.PatchFileName);
            (int exitCode, string output) = RunGit(root, "apply", "--check", patch);

            Assert.True(
                exitCode == 0,
                $"mutant {spec.Id}: git apply --check failed; the patch is stale. Follow the refresh steps in tools/lab/mutants/README.md. {output}");
        }
    }

    private static (int ExitCode, string Output) RunGit(string repoRoot, params string[] args)
    {
        using var process = new Process();
        ProcessStartInfo info = process.StartInfo;
        info.FileName = "git";
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(repoRoot);
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(1)), "git apply --check did not finish within 1 minute");
        return (process.ExitCode, (stdout + stderr).Trim());
    }
}
