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
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Cli;

/// <summary>
/// The <c>meridian-lab</c> command line: <c>daemon run</c> and <c>token new</c>.
/// Exit codes: 0 clean stop, 2 config refusal (short token, non-loopback bind, elevated, bad port), 3 already running, 1 other failure.
/// </summary>
public static class LabCli
{
    private static readonly JsonSerializerOptions StatusJsonOptions = new(JsonSerializerDefaults.Web);

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

        var uat = new Command("uat", "UAT spec runs, through the daemon's MCP endpoint (the uat_run tool).");
        var uatRun = new Command("run", "Runs one UAT spec, or plans it with --plan-only. The token comes from MERIDIAN_LAB_TOKEN.");
        var specs = new Option<string[]>("--specs") { Description = "Spec file (one per run).", AllowMultipleArgumentsPerToken = true };
        var section = new Option<string?>("--section") { Description = "Section id to run." };
        var rows = new Option<string?>("--rows") { Description = "Comma-separated row ids." };
        var planOnly = new Option<bool>("--plan-only") { Description = "Validates and plans the spec without a lease." };
        var label = new Option<string?>("--label") { Description = "Free text label stored with the run." };
        var seed = new Option<ulong?>("--seed") { Description = "Run seed; a fresh one is drawn when omitted." };
        var replay = new Option<string?>("--replay") { Description = "Bundle-relative <run_id>\\repro\\repro.toml. Excludes --specs, --section, --rows and --seed." };
        var runJson = new Option<bool>("--json") { Description = "Prints the raw JSON result." };
        var runPort = new Option<int>("--port") { Description = "Daemon port on 127.0.0.1.", DefaultValueFactory = _ => LabDefaults.DaemonPort };
        foreach (var option in new Option[] { specs, section, rows, planOnly, label, seed, replay, runJson, runPort })
        {
            uatRun.Options.Add(option);
        }

        uatRun.SetAction((parse, ct) => UatRunAsync(
            new UatRunCliOptions(
                parse.GetValue(specs) ?? [],
                parse.GetValue(section),
                parse.GetValue(rows),
                parse.GetValue(planOnly),
                parse.GetValue(label),
                parse.GetValue(seed),
                parse.GetValue(replay),
                parse.GetValue(runJson),
                parse.GetValue(runPort)),
            Console.Out,
            Console.Error,
            ct));
        uat.Subcommands.Add(uatRun);
        root.Subcommands.Add(uat);

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

        var status = JsonSerializer.Deserialize<StatusDto>(body.Value, StatusJsonOptions);
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
    /// <see cref="LabHosts.IsDaemonEndpoint"/>; redirects are not followed and the system proxy is not used.
    /// </summary>
    internal static async Task<Result<string>> GetLoopbackAsync(Uri uri, string token, CancellationToken ct)
    {
        if (!LabHosts.IsDaemonEndpoint(uri))
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
    /// The options of <c>uat run</c> after parsing.
    /// </summary>
    internal sealed record UatRunCliOptions(
        string[] Specs,
        string? Section,
        string? Rows,
        bool PlanOnly,
        string? Label,
        ulong? Seed,
        string? Replay,
        bool Json,
        int Port);

    /// <summary>
    /// Runs one spec through the <c>uat_run</c> MCP tool and prints the seed, the summary table and the bundle path.
    /// Exit codes follow C8 through <see cref="ExitCodeForOverall"/>; 2 for config or plan errors.
    /// </summary>
    internal static async Task<int> UatRunAsync(UatRunCliOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token) || options.Port is < 1 or > 65535)
        {
            await error.WriteLineAsync("[Lab.InvalidArgument] " + LabDefaults.TokenEnvVar + " must be set and --port must be 1-65535").ConfigureAwait(false);
            return 2;
        }

        var replayGiven = options.Replay is not null;
        var specGiven = options.Specs.Length > 0 || options.Section is not null || options.Rows is not null || options.Seed is not null;
        if ((replayGiven && specGiven) || (!replayGiven && options.Specs.Length != 1))
        {
            await error.WriteLineAsync("[Lab.InvalidArgument] give exactly one --specs file, or --replay alone (it excludes --specs, --section, --rows and --seed)").ConfigureAwait(false);
            return 2;
        }

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (replayGiven)
        {
            arguments["replay"] = options.Replay;
        }
        else
        {
            var specPath = Path.GetFullPath(options.Specs[0]);
            arguments["spec"] = Path.GetFileName(specPath);
            arguments["specs_dir"] = Path.GetDirectoryName(specPath);
            if (options.Section is not null)
            {
                arguments["sections"] = new[] { options.Section };
            }

            if (options.Rows is not null)
            {
                arguments["rows"] = options.Rows.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            if (options.Seed is { } seed)
            {
                arguments["seed"] = seed;
            }
        }

        if (options.PlanOnly)
        {
            arguments["plan_only"] = true;
        }

        if (options.Label is not null)
        {
            arguments["label"] = options.Label;
        }

        var call = await CallLabToolAsync(options.Port, token, "uat_run", arguments, ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            await error.WriteLineAsync(call.Error).ConfigureAwait(false);
            return ExitCodeFor(call.Error);
        }

        using var result = call.Value!;
        var root = result.RootElement;
        var overall = root.TryGetProperty("overall", out var overallNode) ? overallNode.GetString() : null;
        if (options.Json)
        {
            await output.WriteLineAsync(root.GetRawText()).ConfigureAwait(false);
            return options.PlanOnly ? 0 : ExitCodeForOverall(overall);
        }

        var runId = root.TryGetProperty("run_id", out var runIdNode) ? runIdNode.GetString() ?? "-" : "-";
        await output.WriteLineAsync($"run {runId} seed {(root.TryGetProperty("seed", out var seedNode) ? seedNode.GetRawText() : "-")} overall {overall ?? "-"}").ConfigureAwait(false);
        if (root.TryGetProperty("rows", out var rowsNode) && rowsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsNode.EnumerateArray())
            {
                var grade = row.TryGetProperty("grade", out var gradeNode) ? gradeNode.GetRawText() : "-";
                var reason = row.TryGetProperty("reason", out var reasonNode) ? reasonNode.GetString() : null;
                await output.WriteLineAsync($"{(row.TryGetProperty("row_id", out var rowId) ? rowId.GetString() : "-")} {grade} {reason}").ConfigureAwait(false);
            }
        }

        // The daemon names the bundle folder (C4); the printed path is the real folder, and nothing is printed when there is none.
        if (!options.PlanOnly
            && root.TryGetProperty("bundle", out var bundleNode)
            && bundleNode.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(bundleNode.GetString()))
        {
            var bundle = Path.Combine(LabPaths.Create(LabDaemon.LoadSettings()).UatRuns, Path.GetFileName(bundleNode.GetString()!));
            await output.WriteLineAsync("bundle " + bundle).ConfigureAwait(false);
        }

        return options.PlanOnly ? 0 : ExitCodeForOverall(overall);
    }

    /// <summary>
    /// Maps <c>overall</c> (C8) to the CLI exit code: 0 pass, 1 fail, 4 incomplete, 5 pass_with_known, 2 anything else.
    /// </summary>
    internal static int ExitCodeForOverall(string? overall) => overall switch
    {
        "pass" => 0,
        "fail" => 1,
        "incomplete" => 4,
        "pass_with_known" => 5,
        _ => 2,
    };

    /// <summary>
    /// Renders a tool error as <c>[code] message</c> from the MCP error envelope. Text that is not that envelope is returned unchanged;
    /// blank text becomes <c>[Lab.Tool] &lt;tool&gt; failed</c>.
    /// </summary>
    internal static string ToolErrorText(string toolName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "[Lab.Tool] " + toolName + " failed";
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return "[" + code.GetString() + "] " + message.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: the text is the message as the daemon sent it.
        }

        return text;
    }

    /// <summary>
    /// Calls one MCP tool on the loopback daemon's <c>/mcp</c> endpoint and returns the JSON text of its result.
    /// The endpoint must pass <see cref="LabHosts.IsDaemonEndpoint"/>; redirects are not followed and no proxy is used.
    /// </summary>
    internal static async Task<Result<JsonDocument>> CallLabToolAsync(int port, string token, string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var uri = LoopbackUri(port, LabDefaults.McpPath);
        if (!LabHosts.IsDaemonEndpoint(uri))
        {
            return Result<JsonDocument>.Failure("[Lab.Refused] the target is not a lab endpoint");
        }

        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(20) };
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = uri,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
            };
            await using var transport = new HttpClientTransport(transportOptions, http);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken: ct).ConfigureAwait(false);
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            if (result.IsError == true)
            {
                return Result<JsonDocument>.Failure(ToolErrorText(toolName, text));
            }

            return Result<JsonDocument>.Success(JsonDocument.Parse(text));
        }
        catch (HttpRequestException)
        {
            return Result<JsonDocument>.Failure("[Lab.Unavailable] the daemon is not reachable on 127.0.0.1");
        }
        catch (JsonException)
        {
            return Result<JsonDocument>.Failure("[Lab.Tool] " + toolName + " returned a result that is not JSON");
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
        if (error.StartsWith("[Lab.InvalidArgument]", StringComparison.Ordinal) || error.StartsWith("[Lab.Elevated]", StringComparison.Ordinal) || error.StartsWith("[Lab.NotFound]", StringComparison.Ordinal))
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
