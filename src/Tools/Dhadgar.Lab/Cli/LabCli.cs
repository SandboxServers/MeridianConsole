using System.Buffers.Text;
using System.CommandLine;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;

namespace Dhadgar.Lab.Cli;

/// <summary>
/// The <c>meridian-lab</c> command line: <c>daemon run</c> and <c>token new</c>.
/// Exit codes: 0 clean stop, 2 config refusal (short token, non-loopback bind, elevated, bad port), 3 already running, 1 other failure.
/// </summary>
public static class LabCli
{
    /// <summary>
    /// Parses and runs the command line. Returns the process exit code.
    /// </summary>
    public static Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return BuildRoot().Parse(args).InvokeAsync();
    }

    internal static RootCommand BuildRoot()
    {
        var root = new RootCommand("meridian-lab: lab tooling for the Meridian product. Dev and lab only.");

        var daemon = new Command("daemon", "Runs the loopback lab daemon.");
        var run = new Command("run", "Runs the daemon until Ctrl+C or SIGTERM. The token comes from MERIDIAN_LAB_TOKEN.");
        run.SetAction((_, ct) => RunDaemonAsync(ct));
        daemon.Subcommands.Add(run);
        root.Subcommands.Add(daemon);

        var token = new Command("token", "Token helpers.");
        var tokenNew = new Command("new", "Prints a fresh 32-byte base64url token once.");
        tokenNew.SetAction(_ => WriteNewToken(Console.Out));
        token.Subcommands.Add(tokenNew);
        root.Subcommands.Add(token);

        return root;
    }

    /// <summary>
    /// Starts the daemon with the token from the environment and waits for Ctrl+C or SIGTERM.
    /// </summary>
    internal static async Task<int> RunDaemonAsync(CancellationToken ct)
    {
        var settings = LabDaemon.LoadSettings();
        var token = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token))
        {
            await Console.Error.WriteLineAsync("[Lab.InvalidArgument] " + LabDefaults.TokenEnvVar + " is not set").ConfigureAwait(false);
            return 2;
        }

        var portText = settings["Lab:Port"];
        var port = string.IsNullOrWhiteSpace(portText)
            ? LabDefaults.DaemonPort
            : int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ? parsedPort : -1;
        if (port is < 1 or > 65535)
        {
            await Console.Error.WriteLineAsync("[Lab.InvalidArgument] Lab:Port must be a port number").ConfigureAwait(false);
            return 2;
        }

        var paths = LabPaths.Create(settings);
        var options = new LabDaemonOptions(port, token, paths.Root, RepoRoot: paths.RepoRoot);

        var started = await LabDaemon.StartAsync(options, ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            await Console.Error.WriteLineAsync(started.Error).ConfigureAwait(false);
            return ExitCodeFor(started.Error);
        }

        await using var daemon = started.Value;
        await Console.Out.WriteLineAsync("meridian-lab daemon listening on " + daemon.BaseAddress.GetLeftPart(UriPartial.Authority)).ConfigureAwait(false);
        await WaitForStopAsync(ct).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Maps a <c>[Lab.Code]</c> refusal to the documented exit code.
    /// </summary>
    internal static int ExitCodeFor(string error)
    {
        if (error.StartsWith("[Lab.InvalidArgument]", StringComparison.Ordinal) || error.StartsWith("[Lab.Elevated]", StringComparison.Ordinal))
        {
            return 2;
        }

        if (error.StartsWith("[Lab.AlreadyRunning]", StringComparison.Ordinal))
        {
            return 3;
        }

        return 1;
    }

    /// <summary>
    /// Writes one fresh token. The buffer is zeroed after the write.
    /// </summary>
    internal static int WriteNewToken(TextWriter output)
    {
        var bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        try
        {
            output.WriteLine(Base64Url.EncodeToString(bytes));
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Blocks until Ctrl+C, SIGTERM or cancellation.
    /// </summary>
    private static async Task WaitForStopAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        Console.CancelKeyPress += onCancel;
        using var terminate = OperatingSystem.IsWindows()
            ? null
            : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                stop.Cancel();
            });

        try
        {
            await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stop requested.
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
