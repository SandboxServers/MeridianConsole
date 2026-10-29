using System.Diagnostics;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// The lab mutants (<c>kind = "lab"</c>, L55) apply at HEAD and touch only the files they declare under <c>src/Tools/</c>.
/// The self-check applies them in a copy; this test is the early warning that a lab mutant has gone stale. Needs <c>git</c>
/// on the PATH; it runs in CI and never needs Docker.
/// </summary>
public sealed class LabMutantPatchesApplyTests
{
    private static readonly string[] RequiredLabIds =
    [
        "LM1-", "LM2-", "LM3-", "LM4-", "LM5-", "LM6-", "LM7-", "LM8-",
    ];

    [Fact]
    public void LabMutants_CoverLM1ToLM8_WithTestDetectors()
    {
        IReadOnlyList<MutantSpec> labs = LoadLabMutants();

        foreach (string prefix in RequiredLabIds)
        {
            MutantSpec spec = Assert.Single(labs, s => s.Id.StartsWith(prefix, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(spec.Detector.Test), $"mutant {spec.Id}: a lab detector names a test");
            Assert.Null(spec.Detector.Tool);
        }
    }

    [Fact]
    public void LabMutantPatches_ApplyCheckAtHead_AndTouchOnlyDeclaredFiles()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        IReadOnlyList<MutantSpec> labs = LoadLabMutants();

        foreach (MutantSpec spec in labs)
        {
            string patch = Path.Combine(spec.Folder, MutantSpecLoader.PatchFileName);
            (int exitCode, string output) = RunGit(root, "apply", "--check", patch);

            Assert.True(
                exitCode == 0,
                $"mutant {spec.Id}: git apply --check failed; the patch is stale. Follow the refresh steps in tools/lab/mutants/README.md. {output}");

            string text = File.ReadAllText(patch);
            foreach (string touched in TouchedPaths(text))
            {
                Assert.StartsWith("src/Tools/", touched, StringComparison.Ordinal);
                Assert.Contains(touched, spec.Files);
            }
        }
    }

    private static List<MutantSpec> LoadLabMutants()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(Path.Combine(root, "tools", "lab", "mutants"), root, validator);
        Assert.True(specs.IsSuccess, specs.Error);

        return specs.Value!.Where(s => s.Kind == MutantKind.Lab).ToList();
    }

    /// <summary>The repo-relative paths named by the <c>diff --git a/&lt;path&gt; b/&lt;path&gt;</c> headers of a patch.</summary>
    private static IEnumerable<string> TouchedPaths(string patchText)
    {
        foreach (string line in patchText.Split('\n'))
        {
            const string Header = "diff --git a/";
            if (!line.StartsWith(Header, StringComparison.Ordinal))
            {
                continue;
            }

            int split = line.IndexOf(" b/", Header.Length, StringComparison.Ordinal);
            if (split < 0)
            {
                continue;
            }

            yield return line.Substring(Header.Length, split - Header.Length).TrimEnd('\r');
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
