using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>Result of a service tool: the target, its state and the image it runs (null when stopped).</summary>
/// <param name="TargetId">Registry id (<c>c:&lt;service&gt;</c>).</param>
/// <param name="State">One of <c>up</c>, <c>stopped</c>, <c>absent</c>.</param>
/// <param name="Image">The image the container runs, or null when nothing runs.</param>
public sealed record LabServiceStartDto(string TargetId, string State, string? Image);

/// <summary>
/// Starts, stops and restarts the lab-owned service containers behind the proxies (L09). Every container carries
/// <c>--label meridian-lab=1</c>, runs on <c>meridian-lab-net</c>, and gets its environment from the adapter plus the
/// config overlay. A start refuses unless the stack is up.
/// </summary>
public sealed class LabServices
{
    /// <summary>How long a started service has to answer 200 on its health path.</summary>
    public const int ReadinessTimeoutSeconds = 90;

    /// <summary>
    /// Host header for readiness probes. Gateway's <c>AllowedHosts</c> lists <c>localhost</c> but not the IP literal, so
    /// probing <c>127.0.0.1</c> with its own Host header is refused with 400 by host filtering.
    /// </summary>
    public const string ProbeHostHeader = "localhost";

    /// <summary>The shape of a container state: a lowercase status word, a space and an integer exit code.</summary>
    private static readonly Regex ContainerStateShape = new("^[a-z]+ -?\\d+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Seconds docker waits before killing a stopping container.</summary>
    public const string StopGrace = "10";

    /// <summary>Container port every service listens on (<c>ASPNETCORE_URLS=http://+:8080</c>).</summary>
    public const int ContainerPort = 8080;

    private const string ImageRepository = "meridian-lab";
    private const string LatestFile = "latest.txt";

    private readonly IReadOnlyDictionary<string, ILabServiceAdapter> _adapters;
    private readonly IDockerCli _docker;
    private readonly ILabTargetRegistry _targets;
    private readonly LabStack _stack;
    private readonly LabConnectionStrings _connections;
    private readonly LabCredentialRefs _credentials;
    private readonly SecretVault _vault;
    private readonly ContainerLogFollower _logs;
    private readonly ConfigOverlay _overlay;
    private readonly IPathValidator _validator;
    private readonly LabPaths _paths;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the service controller over the registered adapters and the lab collaborators.
    /// </summary>
    public LabServices(
        IEnumerable<ILabServiceAdapter> adapters,
        IDockerCli docker,
        ILabTargetRegistry targets,
        LabStack stack,
        LabConnectionStrings connections,
        LabCredentialRefs credentials,
        SecretVault vault,
        ContainerLogFollower logs,
        ConfigOverlay overlay,
        IPathValidator validator,
        LabPaths paths,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(adapter => adapter.Name, StringComparer.Ordinal);
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Service names the lab manages (C10 services table).</summary>
    public IReadOnlyList<string> ServiceNames => _adapters.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Looks up the adapter for a service name. Fails with <c>[Lab.InvalidArgument]</c> for a name the lab does not manage.
    /// </summary>
    public Result<ILabServiceAdapter> Adapter(string service)
    {
        if (string.IsNullOrWhiteSpace(service) || !_adapters.TryGetValue(service, out ILabServiceAdapter? adapter))
        {
            return Result<ILabServiceAdapter>.Failure("[Lab.InvalidArgument] unknown service");
        }

        return Result<ILabServiceAdapter>.Success(adapter);
    }

    /// <summary>The adapter's readiness URL on the loopback host.</summary>
    internal static Uri ReadinessUri(ILabServiceAdapter adapter) =>
        new($"http://127.0.0.1:{adapter.HostPort}{adapter.HealthPath}", UriKind.Absolute);

    /// <summary>True when the adapter's readiness URL is a lab service endpoint or the sink (D-ML147).</summary>
    internal static bool IsReadinessEndpoint(ILabServiceAdapter adapter)
    {
        Uri uri = ReadinessUri(adapter);
        return LabHosts.IsLabEndpoint(uri) || LabHosts.IsSinkEndpoint(uri);
    }

    /// <summary>
    /// Starts a service from a build (the latest build when <paramref name="buildId"/> is null). Replaces a lab-owned
    /// container of the same name, registers <c>c:&lt;service&gt;</c>, follows its logs and waits for its health path.
    /// </summary>
    public async Task<Result<LabServiceStartDto>> StartAsync(string service, string? buildId, CancellationToken ct)
    {
        Result<ILabServiceAdapter> adapter = Adapter(service);
        if (adapter.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(adapter.Error);
        }

        if (!IsReadinessEndpoint(adapter.Value!))
        {
            return Result<LabServiceStartDto>.Failure("[Lab.Untrusted] readiness URL is not a lab endpoint");
        }

        Result stack = await RequireStackUpAsync(ct).ConfigureAwait(false);
        if (stack.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(stack.Error);
        }

        Result<string> build = ResolveBuild(buildId);
        if (build.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(build.Error);
        }

        ILabServiceAdapter target = adapter.Value!;
        string image = $"{ImageRepository}/{target.Project}:{build.Value}";
        string containerName = target.Container;
        string targetId = $"c:{target.Name}";

        Result<bool> owned = await OwnedAsync(containerName, ct).ConfigureAwait(false);
        if (owned.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(owned.Error);
        }

        if (owned.Value)
        {
            Result removed = Require(await _docker.RunAsync(DockerEngine.Linux, ["rm", "-f", containerName], ct).ConfigureAwait(false), $"could not replace {containerName}");
            if (removed.IsFailure)
            {
                return Result<LabServiceStartDto>.Failure(removed.Error);
            }
        }

        Result<IReadOnlyDictionary<string, string>> env = ComposeEnvironment(target, build.Value);
        if (env.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(env.Error);
        }

        Result<string> envFile = WriteEnvFile(env.Value!);
        if (envFile.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(envFile.Error);
        }

        string containerId;
        try
        {
            var args = new List<string> { "run", "-d", "--name", containerName };
            args.AddRange(RunOptions(target));
            if (target is NodesAdapter)
            {
                args.Add("-v");
                args.Add($"{Path.Combine(_paths.State, "nodes-ca")}:{NodesAdapter.ContainerCaPath}");
            }

            args.Add("--env-file");
            args.Add(envFile.Value!);
            args.Add(image);

            Result<DockerOutput> run = await _docker.RunAsync(DockerEngine.Linux, args, ct).ConfigureAwait(false);
            Result started = Require(run, $"could not start {containerName}");
            if (started.IsFailure)
            {
                return Result<LabServiceStartDto>.Failure(started.Error);
            }

            containerId = run.Value!.StdOut.Trim();
        }
        finally
        {
            DeleteQuietly(envFile.Value!);
        }

        _targets.Remove(targetId);
        Result registered = _targets.Register(targetId, LabTargetKind.Container, containerName, containerId).IsSuccess
            ? Result.Success()
            : Result.Failure("[Lab.Docker] the service target could not be registered");
        if (registered.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(registered.Error);
        }

        Result followed = await _logs.FollowAsync(target.Name, containerName, ct).ConfigureAwait(false);
        if (followed.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(followed.Error);
        }

        Result ready = await WaitReadyAsync(target, _time, ct, _docker).ConfigureAwait(false);
        return ready.IsFailure
            ? Result<LabServiceStartDto>.Failure(ready.Error)
            : Result<LabServiceStartDto>.Success(new LabServiceStartDto(targetId, "up", image));
    }

    /// <summary>
    /// Stops a lab-owned service container. A service that is not running is reported as <c>absent</c>, not an error.
    /// </summary>
    public async Task<Result<LabServiceStartDto>> StopAsync(string service, CancellationToken ct)
    {
        Result<ILabServiceAdapter> adapter = Adapter(service);
        if (adapter.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(adapter.Error);
        }

        string targetId = $"c:{adapter.Value!.Name}";
        Result<bool> owned = await OwnedAsync(adapter.Value.Container, ct).ConfigureAwait(false);
        if (owned.IsFailure)
        {
            return Result<LabServiceStartDto>.Failure(owned.Error);
        }

        if (!owned.Value)
        {
            return Result<LabServiceStartDto>.Success(new LabServiceStartDto(targetId, "absent", null));
        }

        Result stopped = Require(
            await _docker.RunAsync(DockerEngine.Linux, ["stop", "--time", StopGrace, adapter.Value.Container], ct).ConfigureAwait(false),
            $"could not stop {adapter.Value.Container}");
        return stopped.IsFailure
            ? Result<LabServiceStartDto>.Failure(stopped.Error)
            : Result<LabServiceStartDto>.Success(new LabServiceStartDto(targetId, "stopped", null));
    }

    /// <summary>
    /// Stops and starts a service. Recreating the container is what applies a changed config overlay.
    /// </summary>
    public async Task<Result<LabServiceStartDto>> RestartAsync(string service, string? buildId, CancellationToken ct)
    {
        Result<LabServiceStartDto> stopped = await StopAsync(service, ct).ConfigureAwait(false);
        return stopped.IsFailure
            ? stopped
            : await StartAsync(service, buildId, ct).ConfigureAwait(false);
    }

    private async Task<Result> RequireStackUpAsync(CancellationToken ct)
    {
        Result<StackStatusDto> status = await _stack.StatusAsync(ct).ConfigureAwait(false);
        return status.IsSuccess && status.Value!.State == "up"
            ? Result.Success()
            : Result.Failure("[Lab.NotFound] stack is down");
    }

    /// <summary>
    /// True when a container with the name exists and carries <c>meridian-lab=1</c>; false when no container has the name.
    /// A container with the name that the lab does not own is refused.
    /// </summary>
    private async Task<Result<bool>> OwnedAsync(string containerName, CancellationToken ct)
    {
        Result<DockerOutput> inspect = await _docker.RunAsync(
            DockerEngine.Linux,
            ["container", "inspect", "--format", $"{{{{index .Config.Labels \"{LabDefaults.ContainerLabel}\"}}}}", containerName],
            ct).ConfigureAwait(false);
        if (inspect.IsFailure)
        {
            return Result<bool>.Failure(inspect.Error);
        }

        if (inspect.Value!.ExitCode != 0)
        {
            return Result<bool>.Success(false);
        }

        return inspect.Value.StdOut.Trim() == "1"
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("[Lab.Docker] the container name is not lab-owned");
    }

    private Result<string> ResolveBuild(string? buildId)
    {
        if (buildId is null)
        {
            Result<string> latest = _validator.ValidatePath(Path.Combine(_paths.Builds, LatestFile), [_paths.Builds]);
            if (latest.IsFailure)
            {
                return Result<string>.Failure(latest.Error);
            }

            if (!File.Exists(latest.Value))
            {
                return Result<string>.Failure("[Lab.NotFound] no build yet; run lab_build first");
            }

            buildId = File.ReadAllText(latest.Value).Trim();
        }

        return SafeIds.IsSafe(buildId)
            ? Result<string>.Success(buildId)
            : Result<string>.Failure("[Lab.InvalidArgument] build id is not valid");
    }

    /// <summary>
    /// The adapter's environment plus the config overlay (overlay keys become <c>__</c> names and win).
    /// Connection strings come from the vault-backed <see cref="LabConnectionStrings"/>, never from the adapter.
    /// </summary>
    private Result<IReadOnlyDictionary<string, string>> ComposeEnvironment(ILabServiceAdapter adapter, string buildId)
    {
        var connections = new Dictionary<string, string>(StringComparer.Ordinal);
        if (adapter.Name == "nodes")
        {
            Result<string> postgres = _connections.ServiceDb("nodes");
            if (postgres.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(postgres.Error);
            }

            connections["nodes"] = postgres.Value!;
        }

        if (adapter.Name == "gateway")
        {
            Result<string> redis = _connections.Redis("gateway");
            if (redis.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(redis.Error);
            }

            connections["gateway-redis"] = redis.Value!;
        }

        if (adapter.Name is "notifications" or "discord")
        {
            Result<string> db = _connections.ServiceDb(adapter.Name);
            if (db.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(db.Error);
            }

            connections[adapter.Name] = db.Value!;
        }

        string? adminKey = null;
        if (adapter.Name is "notifications" or "discord")
        {
            Result<string> key = AdminApiKey(adapter.Name == "notifications" ? LabCredentialRefs.NotificationsAdmin : LabCredentialRefs.DiscordAdmin);
            if (key.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(key.Error);
            }

            adminKey = key.Value!;
        }

        Result<string> password = RabbitMqPassword();
        if (password.IsFailure)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure(password.Error);
        }

        Result<IReadOnlyDictionary<string, string>> overlay = _overlay.Read(adapter);
        if (overlay.IsFailure)
        {
            return overlay;
        }

        var ctx = new LabServiceContext(
            buildId,
            connections,
            Path.Combine(_paths.State, "nodes-ca"),
            "lab",
            password.Value!,
            AdminApiKey: adminKey);
        Result<IReadOnlyDictionary<string, string>> composed = adapter.Environment(ctx);
        if (composed.IsFailure)
        {
            return composed;
        }

        var env = new Dictionary<string, string>(composed.Value!, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in ConfigOverlay.ToEnvironment(overlay.Value!))
        {
            env[entry.Key] = entry.Value;
        }

        // L21: I1 reads the unhandled-request lines, which the Hosting.Diagnostics category logs at Information.
        env["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information";

        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }

    private Result<string> AdminApiKey(string credential)
    {
        string? reference = _credentials.Get(credential);
        if (reference is null)
        {
            return Result<string>.Failure("[Lab.NotFound] the stack has no admin keys; run stack_up first");
        }

        return _vault.Use(reference, value => value.ToString());
    }

    private Result<string> RabbitMqPassword()
    {
        string? reference = _credentials.Get(LabCredentialRefs.RabbitMq);
        if (reference is null)
        {
            return Result<string>.Failure("[Lab.NotFound] the stack has no credentials; run stack_up first");
        }

        return _vault.Use(reference, value => value.ToString());
    }

    /// <summary>
    /// The <c>docker run</c> flags a lab service needs besides its name, env file and image. Every service is published on
    /// loopback only; the sink (L36) also gets a 128 MiB memory cap.
    /// </summary>
    internal static IReadOnlyList<string> RunOptions(ILabServiceAdapter target)
    {
        var options = new List<string>
        {
            "--label", $"{LabDefaults.ContainerLabel}=1",
            "--restart", "no",
            "--network", LabDefaults.NetworkName,
            "--tmpfs", "/tmp:rw,size=64m",
            "-p", $"127.0.0.1:{target.HostPort}:{ContainerPort}",
        };
        if (target is SinkLabAdapter)
        {
            options.Add("--memory");
            options.Add("128m");
        }

        return options;
    }

    /// <summary>
    /// Polls the adapter's health path on its loopback port until it answers <see cref="ILabServiceAdapter.ReadyStatusCode"/>,
    /// or fails with <c>[Lab.Timeout]</c> once <see cref="ReadinessTimeoutSeconds"/> have passed on <paramref name="time"/>.
    /// <c>StartAsync</c> checks the URL with <c>IsReadinessEndpoint</c> before any docker call.
    /// Internal so the tests can drive the wait with a fake listener and clock.
    /// </summary>
    internal static async Task<Result> WaitReadyAsync(
        ILabServiceAdapter adapter,
        TimeProvider time,
        CancellationToken ct,
        IDockerCli? docker = null)
    {
        var uri = new Uri($"http://127.0.0.1:{adapter.HostPort}{adapter.HealthPath}", UriKind.Absolute);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        long started = time.GetTimestamp();
        var deadline = TimeSpan.FromSeconds(ReadinessTimeoutSeconds);
        string lastStatus = "no answer";
        string failingChecks = string.Empty;
        while (true)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Host = ProbeHostHeader;
                using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode == adapter.ReadyStatusCode)
                {
                    return Result.Success();
                }

                lastStatus = $"HTTP {(int)response.StatusCode}";
                failingChecks = FailingHealthChecks(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            catch (HttpRequestException)
            {
                // Not listening yet; retry until the deadline.
                lastStatus = "connection refused or reset";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // The per-attempt timeout elapsed; retry until the deadline.
                lastStatus = "attempt timed out";
            }

            if (time.GetElapsedTime(started) >= deadline)
            {
                string containerState = docker is null
                    ? "unknown"
                    : await ContainerStateAsync(docker, adapter.Container, CancellationToken.None).ConfigureAwait(false);
                return Result.Failure(
                    ReadinessTimeoutMessage(adapter.Name, lastStatus, failingChecks, containerState, (int)adapter.ReadyStatusCode));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes <c>KEY=value</c> lines to a fresh file under <see cref="LabPaths.State"/>. The caller deletes it.
    /// Overlay values are checked for control characters on read and write (<see cref="ConfigOverlay"/>), so a line is one variable.
    /// Each pair goes through the same zeroing writer as <see cref="LabStack"/>, so no secret stays in a managed string buffer.
    /// </summary>
    private Result<string> WriteEnvFile(IReadOnlyDictionary<string, string> env)
    {
        Result<string> validated = _validator.ValidatePath(Path.Combine(_paths.State, $"env-{Path.GetRandomFileName()}"), [_paths.State]);
        if (validated.IsFailure)
        {
            return validated;
        }

        string path = validated.Value!;
        try
        {
            Directory.CreateDirectory(_paths.State);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                // Owner-only on POSIX hosts; Windows ACLs already restrict the per-user state folder.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            foreach (KeyValuePair<string, string> entry in env)
            {
                LabStack.WriteEntry(stream, entry.Key, entry.Value.AsSpan());
            }

            return Result<string>.Success(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(path);
            return Result<string>.Failure("[Lab.Io] the container env file could not be written");
        }
    }

    /// <summary>
    /// Names of the checks that are not <c>Healthy</c> in an ASP.NET health JSON body. Names only: descriptions,
    /// exceptions and data never leave this method (R-LOG). Returns empty for any other body.
    /// </summary>
    internal static string FailingHealthChecks(string body)
    {
        if (!body.TrimStart().StartsWith('{'))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("checks", out JsonElement checks)
                || checks.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var names = new List<string>();
            foreach (JsonProperty entry in checks.EnumerateObject())
            {
                bool healthy = entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("status", out JsonElement status)
                    && status.ValueKind == JsonValueKind.String
                    && status.GetString() == "Healthy";
                if (!healthy)
                {
                    names.Add(entry.Name);
                }
            }

            return string.Join(", ", names);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Reads the container's <c>status exitcode</c> from <c>docker container inspect</c> for a readiness timeout. A Docker failure
    /// or an unexpected answer reads as <c>unknown</c> (see <see cref="ParseContainerState"/>).
    /// </summary>
    private static async Task<string> ContainerStateAsync(IDockerCli docker, string containerName, CancellationToken ct)
    {
        Result<DockerOutput> call = await docker.RunAsync(
            DockerEngine.Linux,
            ["container", "inspect", "--format", "{{.State.Status}} {{.State.ExitCode}}", containerName],
            ct).ConfigureAwait(false);
        return ParseContainerState(call);
    }

    /// <summary>
    /// The container state text for a readiness timeout: the trimmed stdout when it is <c>status exitcode</c> (lowercase status,
    /// integer exit code), otherwise <c>unknown</c>. Docker error text never reaches the message.
    /// </summary>
    internal static string ParseContainerState(Result<DockerOutput> call)
    {
        if (call.IsFailure || call.Value!.ExitCode != 0)
        {
            return "unknown";
        }

        string state = call.Value!.StdOut.Trim();
        return ContainerStateShape.IsMatch(state) ? state : "unknown";
    }

    /// <summary>
    /// The readiness timeout message. It names the service, the last probe status, the failing ServiceDefaults checks (names
    /// only) and the container state, so a timeout explains itself (D-ML151).
    /// </summary>
    internal static string ReadinessTimeoutMessage(string name, string lastStatus, string failingChecks, string containerState, int readyStatus = 200)
    {
        string checks = failingChecks.Length == 0 ? string.Empty : $"; failing checks: {failingChecks}";
        return $"[Lab.Timeout] {name} did not answer {readyStatus} within {ReadinessTimeoutSeconds} s; "
            + $"last status {lastStatus}{checks}; container {containerState}";
    }

    private static Result Require(Result<DockerOutput> call, string message) =>
        call.IsFailure
            ? Result.Failure(call.Error)
            : call.Value!.ExitCode == 0
                ? Result.Success()
                : Result.Failure($"[Lab.Docker] {message}");

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the file is in the lab's own state folder and holds no value after the run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above.
        }
    }
}
