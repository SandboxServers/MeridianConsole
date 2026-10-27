using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Text checks on the UAT wrapper script (P23 security review, N3). The MSI must be built from a fresh publish, so the
/// script deletes the stale output before it publishes and accepts exactly one MSI. Plain reads, no build or run.
/// </summary>
public sealed class UatScriptTests
{
    private static string ReadWrapper()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "uat", "Invoke-AgentWindowsUat.ps1");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Invoke-AgentWindowsUat.ps1 not found above the test output folder.");
    }

    [Fact]
    public void MsiPublish_DeletesTheStalePublishFolderBeforePublishing()
    {
        var script = ReadWrapper();

        var delete = script.IndexOf("Remove-Item -LiteralPath $resolvedStale -Recurse -Force", StringComparison.Ordinal);
        var publish = script.IndexOf(
            "Invoke-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64')", StringComparison.Ordinal);

        Assert.True(delete >= 0, "the script must delete stale output");
        Assert.True(publish > delete, "the stale output must be deleted before the MSI publish");
        Assert.Contains("bin\\Release\\net10.0\\win-x64\\publish", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleDelete_IsRefusedOutsideTheRepo()
    {
        var script = ReadWrapper();

        Assert.Contains("Refusing to delete", script, StringComparison.Ordinal);
        Assert.Contains("StartsWith($repoPrefix", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MsiSelection_AcceptsExactlyOneCandidate()
    {
        var script = ReadWrapper();

        Assert.Contains("if ($msiCandidates.Count -ne 1)", script, StringComparison.Ordinal);
    }
}
