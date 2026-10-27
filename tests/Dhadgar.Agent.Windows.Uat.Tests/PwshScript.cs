using System.Diagnostics;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Shared helpers for tests that run the UAT PowerShell scripts under pwsh (P30f, P32). Only the script's own
/// behaviour is under test; the callers choose the arguments.
/// </summary>
internal static class PwshScript
{
    /// <summary>Runs pwsh -NoProfile -NonInteractive -File <script> <args> and returns the exit code, stdout and stderr.</summary>
    internal static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string pwsh, string script, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(pwsh)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    internal static string? ResolvePwsh()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe" } : new[] { "pwsh" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dhadgar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Dhadgar.sln not found above the test output folder.");
    }
}
