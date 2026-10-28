using System.Diagnostics;

namespace Dhadgar.Lab.Tests.Install;

/// <summary>
/// The install scripts under <c>tools\lab</c> must parse, and must not use constructs that run strings as code
/// or grant the daemon an elevated run level.
/// </summary>
public sealed class ScriptsParseTests
{
    [SkippableTheory]
    [InlineData("install.ps1")]
    [InlineData("daemon.ps1")]
    public async Task Script_Parsed_HasNoParseErrors(string script)
    {
        Skip.IfNot(PwshOnPath(), "pwsh is not on PATH");

        var path = ScriptPath(script);
        var quoted = path.Replace("'", "''", StringComparison.Ordinal);
        var command = "$t = $null; $e = $null; " +
                      "[void][System.Management.Automation.Language.Parser]::ParseFile('" + quoted + "', [ref]$t, [ref]$e); " +
                      "if ($e.Count -gt 0) { $e | ForEach-Object { Write-Output $_.Message }; exit 1 }; exit 0";

        using var process = new Process();
        process.StartInfo.FileName = "pwsh";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(60_000);
        Assert.True(exited, "pwsh did not finish parsing " + script + " within 60 seconds");

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, "parse errors in " + script + ": " + stdout + stderr);
    }

    [Theory]
    [InlineData("install.ps1")]
    [InlineData("daemon.ps1")]
    public void Script_Text_HasNoInvokeExpressionOrHighestRunLevel(string script)
    {
        var text = File.ReadAllText(ScriptPath(script));

        Assert.DoesNotContain("Invoke-Expression", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RunLevel Highest", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string ScriptPath(string script) =>
        Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "tools", "lab", script);

    private static bool PwshOnPath()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe" } : new[] { "pwsh" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                if (File.Exists(Path.Combine(directory, name)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
