using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Docker;

/// <summary>
/// The docker contexts the lab may use (C10). Read from <c>labsettings.json</c> under <c>Lab:Docker</c>.
/// </summary>
/// <param name="LinuxContext">The Linux engine context; defaults to <c>desktop-linux</c>.</param>
/// <param name="WindowsContext">The Windows engine context; null until the L00 worknote names it.</param>
public sealed record DockerOptions(string LinuxContext, string? WindowsContext)
{
    /// <summary>
    /// Reads the contexts from configuration, applying the C10 defaults.
    /// </summary>
    public static DockerOptions FromConfiguration(IConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var linux = settings["Lab:Docker:LinuxContext"];
        var windows = settings["Lab:Docker:WindowsContext"];
        return new DockerOptions(
            string.IsNullOrWhiteSpace(linux) ? "desktop-linux" : linux,
            string.IsNullOrWhiteSpace(windows) ? null : windows);
    }
}

/// <summary>
/// Runs the docker CLI as a child process. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/> only.
/// </summary>
public sealed class DockerCli : IDockerCli
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);
    private static readonly Regex ContextName = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    private readonly DockerOptions _options;

    /// <summary>
    /// Creates the CLI wrapper over the configured contexts.
    /// </summary>
    public DockerCli(DockerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        var context = engine == DockerEngine.Linux ? _options.LinuxContext : _options.WindowsContext;
        if (string.IsNullOrEmpty(context) || !ContextName.IsMatch(context))
        {
            return Result<DockerOutput>.Failure("[Lab.Docker] docker context is not configured for this engine");
        }

        if (args.Count == 0)
        {
            return Result<DockerOutput>.Failure("[Lab.Docker] no docker command given");
        }

        var startInfo = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--context");
        startInfo.ArgumentList.Add(context);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        LabProcessStart.StripToken(startInfo);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return Result<DockerOutput>.Failure("[Lab.Docker] docker did not start");
            }
        }
        catch (Win32Exception)
        {
            return Result<DockerOutput>.Failure("[Lab.Docker] docker is not installed or not on PATH");
        }

        // Nothing is ever typed at a docker prompt; closing stdin stops a stray prompt from hanging the run.
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RunTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return Result<DockerOutput>.Failure("[Lab.Docker] docker timed out");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return Result<DockerOutput>.Success(new DockerOutput(process.ExitCode, stdout, stderr));
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
