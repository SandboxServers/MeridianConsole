using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Parse checks on the UAT wrapper scripts (P30f). A PowerShell parse error stops a script before it runs, which blocks
/// the elevated UAT runner, so every script under scripts/uat must parse with zero errors. Only pwsh's parser runs; the
/// scripts themselves are never executed.
/// </summary>
public sealed class ScriptParseTests
{
    /// <summary>Helper run by pwsh: parses the file named by its first argument and prints each parse error.</summary>
    private const string ParseHelper = """
        param([string]$Path)
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
        foreach ($e in $errors) {
            Write-Output ('{0}:{1}:{2} {3}' -f $e.Extent.File, $e.Extent.StartLineNumber, $e.Extent.StartColumnNumber, $e.Message)
        }
        exit $errors.Count
        """;

    [SkippableFact]
    public async Task UatScripts_ParseWithoutErrors()
    {
        var pwsh = PwshScript.ResolvePwsh();
        Skip.If(pwsh is null, "pwsh is not on PATH.");

        var scripts = Directory.GetFiles(Path.Combine(PwshScript.FindRepoRoot(), "scripts", "uat"), "*.ps1");
        Assert.NotEmpty(scripts);

        var helperDir = Path.Combine(Path.GetTempPath(), "dhadgar-parse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDir);
        try
        {
            var helper = Path.Combine(helperDir, "ParseCheck.ps1");
            await File.WriteAllTextAsync(helper, ParseHelper);

            var failures = new List<string>();
            foreach (var script in scripts)
            {
                var (exitCode, output) = await RunParserAsync(pwsh!, helper, script);
                if (exitCode != 0)
                {
                    failures.Add($"{Path.GetFileName(script)} (exit {exitCode}):{Environment.NewLine}{output.TrimEnd()}");
                }
            }

            Assert.True(failures.Count == 0, "PowerShell parse errors:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
        finally
        {
            Directory.Delete(helperDir, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunParserAsync(string pwsh, string helper, string script)
    {
        var (exitCode, stdout, stderr) = await PwshScript.RunAsync(pwsh, helper, new[] { script }, TimeSpan.FromMinutes(2));
        return (exitCode, stdout + stderr);
    }
}
