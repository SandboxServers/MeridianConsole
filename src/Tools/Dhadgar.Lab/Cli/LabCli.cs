using System.Buffers.Text;
using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Shared.Results;

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

        var status = new Command("status", "Prints the daemon status from GET /status on 127.0.0.1. The token comes from MERIDIAN_LAB_TOKEN.");
        var statusJson = new Option<bool>("--json") { Description = "Prints the raw JSON body." };
        var statusPort = new Option<int>("--port") { Description = "Daemon port on 127.0.0.1.", DefaultValueFactory = _ => LabDefaults.DaemonPort };
        status.Options.Add(statusJson);
        status.Options.Add(statusPort);
        status.SetAction((parse, ct) => StatusAsync(parse.GetValue(statusPort), parse.GetValue(statusJson), Console.Out, Console.Error, ct));
        root.Subcommands.Add(status);

        return root;
    }

    /// <summary>
    /// Calls <c>GET /status</c> on the loopback daemon with the token from the environment and prints the result.
    /// Exit codes: 0 printed, 1 daemon unreachable or refused, 2 bad port or missing token.
    /// </summary>
    internal static async Task<int> StatusAsync(int port, bool json, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token) || port is < 1 or > 65535)
        {
            await error.WriteLineAsync("[Lab.InvalidArgument] " + LabDefaults.TokenEnvVar + " must be set and --port must be 1-65535").ConfigureAwait(false);
            return 2;
        }

        var uri = LoopbackUri(port, LabDefaults.StatusPath);
        var body = await GetLoopbackAsync(uri, token, ct).ConfigureAwait(false);
        if (body.IsFailure)
        {
            await error.WriteLineAsync(body.Error).ConfigureAwait(false);
            return 1;
        }

        if (json)
        {
            await output.WriteLineAsync(body.Value).ConfigureAwait(false);
            return 0;
        }

        var status = JsonSerializer.Deserialize<StatusDto>(body.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (status is null)
        {
            await error.WriteLineAsync("[Lab.Status] the daemon returned an unreadable status body").ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync($"daemon {status.Version} pid {status.Pid} started {status.StartedAt:O}").ConfigureAwait(false);
        await output.WriteLineAsync("stack " + status.StackState).ConfigureAwait(false);
        await output.WriteLineAsync(status.Lease.Held ? $"lease held by {status.Lease.Owner} ({status.Lease.Purpose})" : "lease free").ConfigureAwait(false);
        foreach (var process in status.Processes)
        {
            await output.WriteLineAsync($"process {process.Name} {process.State} pid {process.Pid} restarts {process.Restarts}").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"active faults {status.ActiveFaults} running job {status.RunningJob ?? "-"}").ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Builds an <c>http://127.0.0.1:&lt;port&gt;</c> URI for one path. The CLI talks only to loopback (R-HOST).
    /// </summary>
    internal static Uri LoopbackUri(int port, string path) =>
        new("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + path, UriKind.Absolute);

    /// <summary>
    /// Sends one authenticated GET to a lab endpoint and returns the body. The endpoint must pass
    /// <see cref="LabHosts.IsLabEndpoint"/>; redirects are not followed and the system proxy is not used.
    /// </summary>
    internal static async Task<Result<string>> GetLoopbackAsync(Uri uri, string token, CancellationToken ct)
    {
        if (!LabHosts.IsLabEndpoint(uri))
        {
            return Result<string>.Failure("[Lab.Refused] the target is not a lab endpoint");
        }

        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Failure("[Lab.Status] HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            return Result<string>.Success(text);
        }
        catch (HttpRequestException)
        {
            return Result<string>.Failure("[Lab.Unavailable] the daemon is not reachable on 127.0.0.1");
        }
    }

    /// <summary>
    /// Starts the daemon with the token from the environment and waits for Ctrl+C or SIGTERM.
    /// </summary>
    internal static async Task<int> RunDaemonAsync(CancellationToken ct)
    {
        var settings = LabDaemon.LoadSettings();
        var token = TakeTokenFromEnvironment();
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
    /// Reads the daemon token from the environment and removes it from the process environment, so no child process
    /// the daemon starts later inherits it. The string is returned as read; the caller owns it from here.
    /// </summary>
    internal static string? TakeTokenFromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        Environment.SetEnvironmentVariable(LabDefaults.TokenEnvVar, null);
        return token;
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
