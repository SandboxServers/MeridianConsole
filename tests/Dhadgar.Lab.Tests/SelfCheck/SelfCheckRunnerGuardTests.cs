using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The lab-mutant path of <c>selfcheck</c> (pass 2, N2 and N3): the patch scope check runs before <c>git apply</c>, and no child
/// process inherits the daemon token.
/// </summary>
public sealed class SelfCheckRunnerGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "selfcheck-guard-" + Guid.NewGuid().ToString("N"));

    public SelfCheckRunnerGuardTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MutantSpec Spec(string patch, params string[] files)
    {
        File.WriteAllText(Path.Combine(_folder, MutantSpecLoader.PatchFileName), patch);
        return new MutantSpec(
            "lab-scope-guard",
            MutantKind.Lab,
            new string('a', 40),
            "scope guard",
            files,
            [],
            new MutantDetector(null, null, "Dhadgar.Lab.Tests.Faults.FaultBookTests.LeaseEnd_HealsAllFaults", null),
            _folder);
    }

    [Fact]
    public void PatchScopeRow_BuildFileNotListed_IsInvalidBeforeGitApply()
    {
        MutantSpec spec = Spec(
            "diff --git a/Directory.Build.targets b/Directory.Build.targets\nnew file mode 100644\n--- /dev/null\n+++ b/Directory.Build.targets\n@@ -0,0 +1 @@\n+<Project />\n",
            "src/Dhadgar.Nodes/Foo.cs");

        SelfCheckRow? row = SelfCheckRunner.PatchScopeRow(spec);

        Assert.NotNull(row);
        Assert.Equal(SelfCheckJudge.Invalid, row!.Verdict);
        Assert.Contains("files does not list", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchScopeRow_QuotedHunkOnlyHeader_IsInvalid()
    {
        MutantSpec spec = Spec(
            "diff --git \"a/Directory.Build.targets\" \"b/Directory.Build.targets\"\nnew file mode 100644\n@@ -0,0 +1 @@\n+<Project />\n",
            "src/Dhadgar.Nodes/Foo.cs");

        SelfCheckRow? row = SelfCheckRunner.PatchScopeRow(spec);

        Assert.NotNull(row);
        Assert.Equal(SelfCheckJudge.Invalid, row!.Verdict);
    }

    [Fact]
    public void PatchScopeRow_ListedFile_PassesTheScopeCheck()
    {
        MutantSpec spec = Spec(
            "diff --git a/src/Dhadgar.Nodes/Foo.cs b/src/Dhadgar.Nodes/Foo.cs\n--- a/src/Dhadgar.Nodes/Foo.cs\n+++ b/src/Dhadgar.Nodes/Foo.cs\n@@ -1 +1 @@\n-a\n+b\n",
            "src/Dhadgar.Nodes/Foo.cs");

        Assert.Null(SelfCheckRunner.PatchScopeRow(spec));
    }

    [Fact]
    public async Task RunProcessAsync_ChildDoesNotInheritTheDaemonToken()
    {
        const string secret = "lab-token-must-not-reach-a-child";
        string? previous = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        Environment.SetEnvironmentVariable(LabDefaults.TokenEnvVar, secret);
        try
        {
            (string fileName, string[] args) = OperatingSystem.IsWindows()
                ? ("cmd", new[] { "/c", "echo %" + LabDefaults.TokenEnvVar + "%" })
                : ("sh", new[] { "-c", "printf %s \"$" + LabDefaults.TokenEnvVar + "\"" });

            (int exitCode, string output) = await SelfCheckRunner.RunProcessAsync(fileName, args, null, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LabDefaults.TokenEnvVar, previous);
        }
    }
}
