using System.ComponentModel;
using System.Diagnostics;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>Captured result of a short-lived tool. Output is already redacted.</summary>
internal sealed record CommandOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs a short-lived tool (<c>docker</c>, <c>git</c>, <c>sc.exe</c>, <c>wevtutil</c>, <c>dotnet</c>)
/// with <c>ArgumentList</c>, a timeout, and redacted output (R-ARGS, R-LOG).
/// </summary>
internal static class CommandRunner
{
    /// <summary>Runs the tool and returns its exit code and redacted output, or a failure if it could not run.</summary>
    public static async Task<Result<CommandOutput>> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Redactor? redactor = null,
        string? workingDirectory = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        // Added after the inherited environment: `docker run -e NAME` (no value) reads NAME from this process.
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return Result<CommandOutput>.Failure($"{fileName} did not start");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return Result<CommandOutput>.Failure(ct.IsCancellationRequested
                    ? $"{fileName} was cancelled"
                    : $"{fileName} timed out after {timeout.TotalSeconds:0} s");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return Result<CommandOutput>.Success(new CommandOutput(
                process.ExitCode,
                redactor?.Redact(stdout) ?? stdout,
                redactor?.Redact(stderr) ?? stderr));
        }
        catch (Win32Exception ex)
        {
            return Result<CommandOutput>.Failure($"{fileName} could not be started: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return Result<CommandOutput>.Failure($"{fileName} failed: {ex.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the kill.
        }
    }
}
