using System.Text.RegularExpressions;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Text checks on the UAT scripts (P23 security review, N3; P32 build split). The MSI must be built from a fresh publish,
/// so the build script deletes the stale output before it publishes and the wrapper accepts exactly one MSI. The wrapper
/// builds only through Build-AgentWindowsUat.ps1, and that script never calls exit. Plain reads, no build or run.
/// </summary>
public sealed class UatScriptTests
{
    private static string ReadUatScript(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "uat", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{fileName} not found above the test output folder.");
    }

    /// <summary>Removes block comments and line comments, so the help text (which names exit 4 and --no-restore) does not count.</summary>
    private static string StripComments(string script)
    {
        var withoutBlocks = Regex.Replace(script, @"<#.*?#>", "", RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"(?m)#.*$", "");
    }

    [Fact]
    public void MsiPublish_DeletesTheStalePublishFolderBeforePublishing()
    {
        var script = ReadUatScript("Build-AgentWindowsUat.ps1");

        var delete = script.IndexOf(
            @"Add-Remove -Path (Join-Path (Split-Path -Parent $agentProject) 'bin\Release\net10.0\win-x64\publish')", StringComparison.Ordinal);
        var publish = script.IndexOf(
            @"Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore')", StringComparison.Ordinal);

        Assert.True(delete >= 0, "the script must delete stale output");
        Assert.True(publish > delete, "the stale output must be deleted before the MSI publish");
        Assert.Contains("bin\\Release\\net10.0\\win-x64\\publish", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $resolvedStale -Recurse -Force", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleDelete_IsRefusedOutsideTheRepo()
    {
        var script = ReadUatScript("Build-AgentWindowsUat.ps1");

        Assert.Contains("Refusing to delete", script, StringComparison.Ordinal);
        Assert.Contains("StartsWith($repoPrefix", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MsiSelection_AcceptsExactlyOneCandidate()
    {
        var script = ReadUatScript("Invoke-AgentWindowsUat.ps1");

        Assert.Contains("if ($msiCandidates.Count -ne 1)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapper_BuildsOnlyThroughTheBuildScript()
    {
        var raw = ReadUatScript("Invoke-AgentWindowsUat.ps1");
        var code = StripComments(raw);

        Assert.Contains("Build-AgentWindowsUat.ps1", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-DotNet", code, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(code, @"(?m)^\s*&\s*dotnet\b"), "the wrapper must not call dotnet itself");
        Assert.False(Regex.IsMatch(code, @"'(build|publish|restore)',"), "the wrapper must not pass dotnet verbs");
    }

    [Fact]
    public void BuildScript_NeverCallsExit()
    {
        var code = StripComments(ReadUatScript("Build-AgentWindowsUat.ps1"));

        Assert.False(Regex.IsMatch(code, @"(?im)^\s*exit\b"), "the build script must throw, not exit (the wrapper runs it in its own session)");
    }
}
