using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Http;
using Dhadgar.Shared.Results;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.SelfCheck;

/// <summary>
/// The crash-recovery check of <c>selfcheck --recovery</c> (L55, amendments A1-A4). It starts a child daemon from a
/// temporary copy of HEAD, acquires a lease, brings the stack up with wipe, adds a latency toxic on
/// <c>nodes-postgres</c>, kills the child's process tree, starts a second child on the same home and checks that
/// the toxic, the lease and any I5 orphan are gone. The child's token is fresh per start and never logged.
/// </summary>
public static class SelfCheckRecovery
{
    /// <summary>The proxy the latency toxic is added to.</summary>
    public const string Proxy = "nodes-postgres";

    /// <summary>The toxic type added to the proxy.</summary>
    public const string ToxicName = "latency";

    /// <summary>The bound on how long a child may take to answer <c>GET /status</c> (A3).</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The bound on the cleanup in <c>finally</c>, separate from the run's token (A3).</summary>
    public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>The bound on a diagnostic text: a tool's error text, or a child's first <c>[Lab.</c> line (D3).</summary>
    private const int MaxDiagnosticText = 300;

    private const string Owner = "selfcheck";
    private const string Purpose = "selfcheck recovery";
    private const string BuildConfiguration = "Release";

    /// <summary>
    /// Judges the state observed after the restart. An empty list means the daemon recovered.
    /// </summary>
    /// <param name="faultsEmpty">True when <c>faults_active</c> lists nothing.</param>
    /// <param name="leaseHeld">True when <c>lab_lease_status</c> shows a current lease.</param>
    /// <param name="i5Violated">True when <c>invariants_check</c> reports an I5 violation.</param>
    public static IReadOnlyList<string> Judge(bool faultsEmpty, bool leaseHeld, bool i5Violated)
    {
        var problems = new List<string>();
        if (!faultsEmpty)
        {
            problems.Add("a fault survived the restart");
        }

        if (leaseHeld)
        {
            problems.Add("a lease survived the restart");
        }

        if (i5Violated)
        {
            problems.Add("I5 reports an orphan after the restart");
        }

        return problems;
    }

    /// <summary>
    /// A1: true when another lab daemon holds the per-user mutex <c>Local\MeridianLab-&lt;user&gt;</c>. The product guard
    /// is not edited and nothing renames or bypasses the mutex.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsDaemonRunning()
    {
        using var identity = WindowsIdentity.GetCurrent();
        string name = "Local\\MeridianLab-" + (identity.User?.Value ?? "unknown");
        if (Mutex.TryOpenExisting(name, out Mutex? existing))
        {
            existing.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>
    /// A2: a fresh token of <see cref="LabDefaults.MinTokenBytes"/> random bytes, base64url-encoded. The bytes are zeroed
    /// after encoding. The string is returned to the caller and is never logged or written to a report.
    /// </summary>
    public static string NewToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        try
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Runs the recovery check. Returns the problems found (empty when the daemon recovered), or a failure when the
    /// check could not run. Every path goes through <paramref name="validator"/>.
    /// </summary>
    /// <param name="repoRoot">The repository whose HEAD is copied.</param>
    /// <param name="validator">The path validator for every path used.</param>
    /// <param name="time">The clock for the readiness bound.</param>
    /// <param name="keep">Keep the temporary copy and home instead of deleting them.</param>
    /// <param name="ct">Cancels the run.</param>
    public static async Task<Result<IReadOnlyList<string>>> RunAsync(string repoRoot, IPathValidator validator, TimeProvider time, bool keep, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(time);

        if (!OperatingSystem.IsWindows())
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.Unsupported] --recovery runs on Windows in this build");
        }

        if (ElevationCheck.IsElevated())
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.Elevated] selfcheck --recovery needs a non-elevated session");
        }

        if (IsDaemonRunning())
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.Busy] stop the running lab daemon first");
        }

        string tempRoot = Path.GetTempPath();
        Result<string> workRoot = validator.ValidatePath(Path.Combine(tempRoot, SelfCheckRunner.TempFolderName, "recovery-" + Guid.NewGuid().ToString("N")), [tempRoot]);
        if (workRoot.IsFailure)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.InvalidArgument] the temporary folder is not valid");
        }

        string copy = Path.Combine(workRoot.Value!, "copy");
        string home = Path.Combine(workRoot.Value!, "home");
        try
        {
            Directory.CreateDirectory(home);
            await SelfCheckRunner.ExtractHeadAsync(repoRoot, workRoot.Value!, copy, ct).ConfigureAwait(false);
            return await RunInCopyAsync(copy, home, time, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.ProcessFailed] the recovery copy could not be prepared");
        }
        catch (IOException)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.ProcessFailed] the recovery copy could not be prepared");
        }
        finally
        {
            if (!keep)
            {
                SelfCheckRunner.DeleteQuietly(workRoot.Value!);
            }
        }
    }

    private static async Task<Result<IReadOnlyList<string>>> RunInCopyAsync(string copy, string home, TimeProvider time, CancellationToken ct)
    {
        string csproj = Path.Combine(copy, "src", "Tools", "Dhadgar.Lab", "Dhadgar.Lab.csproj");
        (int buildExit, _) = await SelfCheckRunner.RunProcessAsync("dotnet", ["build", csproj, "-c", BuildConfiguration], null, ct).ConfigureAwait(false);
        if (buildExit != 0)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.ProcessFailed] the lab daemon did not build");
        }

        string dll = Path.Combine(copy, "src", "Tools", "Dhadgar.Lab", "bin", BuildConfiguration, "net10.0", "meridian-lab.dll");
        if (!File.Exists(dll))
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.NotFound] the lab daemon binary was not found");
        }

        Daemon? first = null;
        Daemon? second = null;
        try
        {
            Result<Daemon> started = await StartDaemonAsync(dll, home, time, "the lab daemon", ct).ConfigureAwait(false);
            if (started.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(started.Error);
            }

            first = started.Value!;

            Result<string> lease = await AcquireLeaseAsync(first.Token, ct).ConfigureAwait(false);
            if (lease.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(lease.Error);
            }

            Result<JsonDocument> up = await CallToolAsync(first.Token, "stack_up", new Dictionary<string, object?> { ["wipe"] = true, ["lease_id"] = lease.Value }, ct).ConfigureAwait(false);
            if (up.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(up.Error);
            }

            up.Value!.Dispose();
            Result<JsonDocument> toxic = await CallToolAsync(first.Token, "fault_toxic_add", ToxicArguments(lease.Value!), ct).ConfigureAwait(false);
            if (toxic.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(toxic.Error);
            }

            toxic.Value!.Dispose();

            // The crash: the child's whole tree goes, leaving the toxic and the lease behind on the stack.
            first.Kill();
            first.Dispose();
            first = null;

            Result<Daemon> restarted = await StartDaemonAsync(dll, home, time, "the restarted lab daemon", ct).ConfigureAwait(false);
            if (restarted.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(restarted.Error);
            }

            second = restarted.Value!;

            return await JudgeRestartAsync(second.Token, ct).ConfigureAwait(false);
        }
        finally
        {
            // A3: the child's stack comes down with wipe, under its own bounded token; then the children are killed.
            using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            cleanup.CancelAfter(CleanupTimeout);
            Daemon? live = second ?? first;
            if (live is not null && !live.Process.HasExited)
            {
                await TearDownAsync(live.Token, cleanup.Token).ConfigureAwait(false);
            }

            first?.Dispose();
            second?.Dispose();
        }
    }

    private static Dictionary<string, object?> ToxicArguments(string leaseId) => new()
    {
        ["proxy"] = Proxy,
        ["toxic"] = ToxicName,
        ["stream"] = "downstream",
        ["attributes"] = new Dictionary<string, long> { ["latency"] = 5000 },
        ["heal_after_s"] = 600,
        ["lease_id"] = leaseId,
    };

    private static async Task<Result<IReadOnlyList<string>>> JudgeRestartAsync(string token, CancellationToken ct)
    {
        Result<JsonDocument> faults = await CallToolAsync(token, "faults_active", new Dictionary<string, object?>(), ct).ConfigureAwait(false);
        Result<JsonDocument> lease = await CallToolAsync(token, "lab_lease_status", new Dictionary<string, object?>(), ct).ConfigureAwait(false);
        Result<JsonDocument> invariants = await CallToolAsync(token, "invariants_check", new Dictionary<string, object?>(), ct).ConfigureAwait(false);
        using (faults.IsSuccess ? faults.Value : null)
        using (lease.IsSuccess ? lease.Value : null)
        using (invariants.IsSuccess ? invariants.Value : null)
        {
            if (faults.IsFailure || lease.IsFailure || invariants.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure("[Lab.Tool] a recovery probe failed");
            }

            bool faultsEmpty = faults.Value!.RootElement.TryGetProperty("faults", out JsonElement list)
                && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == 0;
            bool leaseHeld = lease.Value!.RootElement.TryGetProperty("current", out JsonElement current)
                && current.ValueKind != JsonValueKind.Null;
            bool i5 = HasInvariantId(invariants.Value!.RootElement, "I5");
            return Result<IReadOnlyList<string>>.Success(Judge(faultsEmpty, leaseHeld, i5));
        }
    }

    private static bool HasInvariantId(JsonElement node, string id)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in node.EnumerateObject())
                {
                    if (property.NameEquals("invariant_id") && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == id)
                    {
                        return true;
                    }

                    if (HasInvariantId(property.Value, id))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (JsonElement item in node.EnumerateArray())
                {
                    if (HasInvariantId(item, id))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static async Task TearDownAsync(string token, CancellationToken ct)
    {
        Result<string> lease = await AcquireLeaseAsync(token, ct).ConfigureAwait(false);
        if (lease.IsFailure)
        {
            return;
        }

        Result<JsonDocument> down = await CallToolAsync(token, "stack_down", new Dictionary<string, object?> { ["wipe"] = true, ["lease_id"] = lease.Value }, ct).ConfigureAwait(false);
        down.Value?.Dispose();
        Result<JsonDocument> released = await CallToolAsync(token, "lab_lease_release", new Dictionary<string, object?> { ["lease_id"] = lease.Value }, ct).ConfigureAwait(false);
        released.Value?.Dispose();
    }

    private static async Task<Result<string>> AcquireLeaseAsync(string token, CancellationToken ct)
    {
        Result<JsonDocument> call = await CallToolAsync(token, "lab_lease_acquire", new Dictionary<string, object?> { ["owner"] = Owner, ["purpose"] = Purpose }, ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return Result<string>.Failure(call.Error);
        }

        using JsonDocument document = call.Value!;
        return document.RootElement.TryGetProperty("lease_id", out JsonElement id) && id.ValueKind == JsonValueKind.String
            ? Result<string>.Success(id.GetString()!)
            : Result<string>.Failure("[Lab.Tool] lab_lease_acquire returned no lease");
    }

    /// <summary>The failure text of a child that exited before it answered <c>/status</c>: its exit code and its first <c>[Lab.</c> line.</summary>
    /// <param name="code">The exit code of the child.</param>
    /// <param name="line">The child's first <c>[Lab.</c> stderr line, or null when it wrote none.</param>
    public static string ExitedEarly(int code, string? line) =>
        "[Lab.ProcessFailed] the lab daemon exited with code " + code.ToString(CultureInfo.InvariantCulture) + " before answering /status: " + (line ?? "no [Lab.] line");

    /// <summary>The failure text of a tool that reported an error: the tool name and its text, cut to 300 characters.</summary>
    /// <param name="tool">The tool that failed.</param>
    /// <param name="text">The text the tool returned.</param>
    public static string ToolFailure(string tool, string text) =>
        "[Lab.Tool] " + tool + " failed: " + (text.Length > MaxDiagnosticText ? text[..MaxDiagnosticText] : text);

    /// <summary>
    /// Starts one child daemon and polls <c>GET /status</c> until it answers. A child that exits first fails with its exit code and
    /// first <c>[Lab.</c> line; a live child that never answers fails with <c>[Lab.Timeout]</c> and the last status failure.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The daemon is returned to the caller, which owns and disposes it; a failed child is disposed in finally.")]
    private static async Task<Result<Daemon>> StartDaemonAsync(string dll, string home, TimeProvider time, string what, CancellationToken ct)
    {
        string token = NewToken();
        Daemon? daemon = Daemon.Start(dll, home, token);
        bool ready = false;
        try
        {
            if (daemon is null)
            {
                return Result<Daemon>.Failure("[Lab.ProcessFailed] " + what + " could not be started");
            }

            DateTimeOffset deadline = time.GetUtcNow() + ReadyTimeout;
            string? lastError = null;
            while (time.GetUtcNow() < deadline && !daemon.Process.HasExited)
            {
                Result<JsonDocument> status = await GetStatusAsync(token, ct).ConfigureAwait(false);
                if (status.IsSuccess)
                {
                    status.Value!.Dispose();
                    ready = true;
                    return Result<Daemon>.Success(daemon);
                }

                lastError = status.Error;
                await Task.Delay(PollInterval, time, ct).ConfigureAwait(false);
            }

            if (daemon.Process.HasExited)
            {
                return Result<Daemon>.Failure(ExitedEarly(daemon.Process.ExitCode, daemon.FirstLabLine));
            }

            string timeout = "[Lab.Timeout] " + what + " did not answer /status in time";
            return Result<Daemon>.Failure(lastError is null ? timeout : timeout + ": " + lastError);
        }
        finally
        {
            // The caller owns the daemon only when it answered; a timed-out child is killed here.
            if (!ready)
            {
                daemon?.Dispose();
            }
        }
    }

    private static async Task<Result<JsonDocument>> GetStatusAsync(string token, CancellationToken ct)
    {
        Uri uri = LabCliUri(LabDefaults.StatusPath);
        if (!LabHosts.IsDaemonEndpoint(uri))
        {
            return Result<JsonDocument>.Failure("[Lab.Refused] the target is not a lab endpoint");
        }

        try
        {
            using HttpMessageHandler handler = LabHttp.CreateHandler();
            using var http = new HttpClient(handler) { Timeout = LabHttp.RequestTimeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result<JsonDocument>.Failure("[Lab.Unavailable] /status did not answer 200");
            }

            return Result<JsonDocument>.Success(JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));
        }
        catch (HttpRequestException)
        {
            return Result<JsonDocument>.Failure("[Lab.Unavailable] the daemon is not reachable on 127.0.0.1");
        }
        catch (JsonException)
        {
            return Result<JsonDocument>.Failure("[Lab.Unavailable] /status returned no JSON");
        }
    }

    /// <summary>Calls one MCP tool on the child's <c>/mcp</c> endpoint and returns its JSON result. The caller disposes it.</summary>
    private static async Task<Result<JsonDocument>> CallToolAsync(string token, string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        Uri uri = LabCliUri(LabDefaults.McpPath);
        if (!LabHosts.IsDaemonEndpoint(uri))
        {
            return Result<JsonDocument>.Failure("[Lab.Refused] the target is not a lab endpoint");
        }

        try
        {
            using HttpMessageHandler handler = LabHttp.CreateHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            var options = new HttpClientTransportOptions
            {
                Endpoint = uri,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
            };
            await using var transport = new HttpClientTransport(options, http);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
            CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: ct).ConfigureAwait(false);
            string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            if (result.IsError == true)
            {
                return Result<JsonDocument>.Failure(ToolFailure(tool, text));
            }

            return Result<JsonDocument>.Success(JsonDocument.Parse(text));
        }
        catch (HttpRequestException)
        {
            return Result<JsonDocument>.Failure("[Lab.Unavailable] the daemon is not reachable on 127.0.0.1");
        }
        catch (JsonException)
        {
            return Result<JsonDocument>.Failure("[Lab.Tool] " + tool + " returned no JSON");
        }
    }

    private static Uri LabCliUri(string path) =>
        new("http://127.0.0.1:" + LabDefaults.DaemonPort.ToString(CultureInfo.InvariantCulture) + path, UriKind.Absolute);

    /// <summary>
    /// One child daemon. <c>daemon run</c> has no port option, so the child binds <see cref="LabDefaults.DaemonPort"/>;
    /// the A1 mutex check keeps it from colliding with a user's daemon. Output is drained and dropped, never logged.
    /// </summary>
    private sealed class Daemon : IDisposable
    {
        private readonly object _stderrGate = new();
        private volatile string? _firstLabLine;

        private Daemon(Process process, string token)
        {
            Process = process;
            Token = token;
        }

        /// <summary>Keeps the first <c>[Lab.</c> stderr line; later lines are dropped unread.</summary>
        private void KeepFirstLabLine(string? line)
        {
            if (line is null || !line.StartsWith("[Lab.", StringComparison.Ordinal))
            {
                return;
            }

            lock (_stderrGate)
            {
                if (_firstLabLine is null)
                {
                    string clean = new(line.Where(c => !char.IsControl(c)).ToArray());
                    _firstLabLine = clean.Length > MaxDiagnosticText ? clean[..MaxDiagnosticText] : clean;
                }
            }
        }

        public Process Process { get; }

        public string Token { get; }

        /// <summary>
        /// The child's first stderr line that starts with <c>[Lab.</c>, control characters removed and cut to 300 characters;
        /// null when none came. Written once.
        /// </summary>
        public string? FirstLabLine => _firstLabLine;

        public static Daemon? Start(string dll, string home, string token)
        {
            var process = new Process();
            ProcessStartInfo info = process.StartInfo;
            info.FileName = "dotnet";
            info.ArgumentList.Add(dll);
            info.ArgumentList.Add("daemon");
            info.ArgumentList.Add("run");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            // A2: the fresh token replaces any inherited MERIDIAN_LAB_TOKEN; the home is the temporary folder.
            info.Environment[LabDefaults.TokenEnvVar] = token;
            info.Environment[LabDefaults.HomeEnvVar] = home;

            try
            {
                process.OutputDataReceived += static (_, _) => { };
                if (!process.Start())
                {
                    process.Dispose();
                    return null;
                }

                // The stderr handler is attached before BeginErrorReadLine, so no line is missed.
                var daemon = new Daemon(process, token);
                process.ErrorDataReceived += (_, args) => daemon.KeepFirstLabLine(args.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return daemon;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                process.Dispose();
                return null;
            }
        }

        /// <summary>Kills the whole process tree (A3).</summary>
        public void Kill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(TimeSpan.FromSeconds(30));
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and the kill.
            }
        }

        public void Dispose()
        {
            Kill();
            Process.Dispose();
        }
    }
}
