using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>What to start. <see cref="Name"/> names the log file under the run's <c>logs</c> folder.</summary>
internal sealed record ChildSpec(
    string Name,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// A child process of the run, inside the driver's job object. Its output is redacted and written to
/// <c>&lt;runDir&gt;\logs\&lt;name&gt;.log</c>. Start writes the <c>process</c> manifest entry before the job assignment.
/// </summary>
internal sealed partial class ChildProcess : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly Task _pumps;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private ChildProcess(Process process, StreamWriter log, Redactor redactor)
    {
        _process = process;
        _log = log;
        _pumps = Task.WhenAll(
            PumpAsync(process.StandardOutput, redactor),
            PumpAsync(process.StandardError, redactor));
    }

    /// <summary>The child's process id.</summary>
    public int ProcessId => _process.Id;

    /// <summary>The child's exit code once it has exited; null while it runs.</summary>
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    /// <summary>
    /// Starts the child with <c>ArgumentList</c>, removes agent enrollment variables from its environment,
    /// records it in the manifest, and assigns it to <paramref name="job"/>.
    /// </summary>
    public static Result<ChildProcess> Start(ChildSpec spec, JobObject job, Redactor redactor, CleanupManifest manifest)
    {
        if (!SafeName().IsMatch(spec.Name))
        {
            return Result<ChildProcess>.Failure("child name must be letters, digits or dashes");
        }

        var logsDir = Path.Combine(Path.GetDirectoryName(manifest.Path)!, "logs");

        var psi = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory,
        };
        foreach (var argument in spec.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        foreach (var key in psi.Environment.Keys.Cast<string>().ToList())
        {
            if (key.StartsWith("Agent__", StringComparison.OrdinalIgnoreCase)
                || key.Equals("DHADGAR_Agent__EnrollmentToken", StringComparison.OrdinalIgnoreCase))
            {
                psi.Environment.Remove(key);
            }
        }

        foreach (var (key, value) in spec.Environment)
        {
            psi.Environment[key] = value;
        }

        // The log is opened before the child starts: if it cannot be opened, no child is left running without a pump.
        StreamWriter? log = null;
        Process? process = null;
        try
        {
            Directory.CreateDirectory(logsDir);
            log = new StreamWriter(Path.Combine(logsDir, spec.Name + ".log"), append: true, Encoding.UTF8)
            {
                AutoFlush = true,
            };

            process = Process.Start(psi);
            if (process is null)
            {
                log.Dispose();
                return Result<ChildProcess>.Failure($"{spec.Name} did not start");
            }

            var entry = new ProcessEntry(process.Id, process.StartTime.ToUniversalTime(), spec.Name);
            var recorded = manifest.Append(entry);
            if (recorded.IsFailure)
            {
                KillQuietly(process);
                process.Dispose();
                log.Dispose();
                return Result<ChildProcess>.Failure(recorded.Error);
            }

            var assigned = job.AssignProcess(process);
            if (assigned.IsFailure)
            {
                KillQuietly(process);
                process.Dispose();
                log.Dispose();
                return Result<ChildProcess>.Failure(assigned.Error);
            }

            ChildProcess? child = new ChildProcess(process, log, redactor);
            try
            {
                var result = Result<ChildProcess>.Success(child);
                child = null; // The caller owns the child through the result; finally must not dispose it.
                return result;
            }
            finally
            {
                child?.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (process is not null)
            {
                KillQuietly(process); // Started but not recorded or assigned: nothing else would stop it.
                process.Dispose();
            }

            log?.Dispose();
            return Result<ChildProcess>.Failure($"{spec.Name} could not be started: {ex.Message}");
        }
    }

    /// <summary>Kills the process tree and waits up to 15 seconds for it to exit.</summary>
    public async Task<Result> StopAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            using var cts = new CancellationTokenSource(StopTimeout);
            await _process.WaitForExitAsync(cts.Token);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure($"process {ProcessId} did not exit within 15 s");
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
            return Result.Success();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _pumps.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Pump failures are already visible in the log; disposal must not throw.
        }

        _log.Dispose();
        _process.Dispose();
        _writeGate.Dispose();
    }

    private async Task PumpAsync(StreamReader reader, Redactor redactor)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            var redacted = redactor.Redact(line);
            await _writeGate.WaitAsync();
            try
            {
                await _log.WriteLineAsync(redacted);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }

    private static void KillQuietly(Process process)
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
            // Already gone.
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
