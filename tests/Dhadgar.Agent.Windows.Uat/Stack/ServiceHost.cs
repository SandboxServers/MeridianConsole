using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// The run's TLS PFX for the Gateway (P22). Set only in service and MSI modes; null in process mode and <c>-StackOnly</c>.
/// </summary>
/// <param name="PfxPath">The run-scoped <c>&lt;runDir&gt;\tls\gateway.pfx</c>.</param>
/// <param name="Password">The PFX password; it goes only into the Gateway environment.</param>
internal sealed record GatewayTls(string PfxPath, string Password);

/// <summary>The ports and passwords one run's services are started with (P19).</summary>
/// <param name="Tls">The Gateway's run-scoped certificate (P22), or null for the user's dev certificate.</param>
internal sealed record StackSettings(
    int PostgresPort,
    int RabbitMqPort,
    int NodesPort,
    int GatewayPort,
    string PostgresPassword,
    string RabbitMqPassword,
    string CaPassword,
    GatewayTls? Tls = null);

/// <summary>
/// Runs Nodes and the Gateway as children of the run (P19). Both are started from their published images
/// (<see cref="RunPaths.StackImages"/>) so cleanup may kill them. Each one's environment follows the stack table
/// in the UAT contract and contains no <c>Agent__*</c> key.
/// </summary>
internal sealed class ServiceHost : IDisposable
{
    /// <summary>How long Nodes may take to report healthy.</summary>
    public static readonly TimeSpan NodesHealthBudget = TimeSpan.FromSeconds(90);

    /// <summary>How long the Gateway may take to report healthy.</summary>
    public static readonly TimeSpan GatewayHealthBudget = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly RunPaths _paths;
    private readonly JobObject _job;
    private readonly Redactor _redactor;
    private readonly CleanupManifest _manifest;
    private ChildProcess? _nodes;
    private ChildProcess? _gateway;

    /// <summary>Creates the host for one run.</summary>
    public ServiceHost(RunPaths paths, JobObject job, Redactor redactor, CleanupManifest manifest)
    {
        _paths = paths;
        _job = job;
        _redactor = redactor;
        _manifest = manifest;
    }

    /// <summary>The Nodes process id, once started.</summary>
    public int? NodesProcessId => _nodes?.ProcessId;

    /// <summary>The Gateway process id, once started.</summary>
    public int? GatewayProcessId => _gateway?.ProcessId;

    /// <summary>The Nodes base address (plain HTTP on loopback).</summary>
    public static Uri NodesBase(int port) => new($"http://127.0.0.1:{port}/");

    /// <summary>The Gateway base address (HTTPS on localhost, the user's trusted dev certificate).</summary>
    public static Uri GatewayBase(int port) => new($"https://localhost:{port}/");

    /// <summary>The Nodes environment, per the UAT contract's stack table.</summary>
    public static IReadOnlyDictionary<string, string> NodesEnvironment(StackSettings s) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{s.NodesPort}",
            ["ConnectionStrings__Postgres"] =
                $"Host=127.0.0.1;Port={s.PostgresPort};Database={DockerStack.DatabaseName};Username=postgres;Password={s.PostgresPassword}",
            ["RabbitMq__Host"] = "127.0.0.1",
            ["RabbitMq__Username"] = "uat",
            ["RabbitMq__Password"] = s.RabbitMqPassword,
            ["RabbitMq__VirtualHost"] = "/",
            ["Mtls__ForwardedCertificate__Enabled"] = "true",
            ["Mtls__ForwardedCertificate__TrustedProxies__0"] = "127.0.0.1/32",
            ["Mtls__ForwardedCertificate__TrustedProxies__1"] = "::1/128",
            ["Mtls__RenewalGraceMinutes"] = "5",
        };

    /// <summary>The Nodes environment with the CA paths added (they need the run folder).</summary>
    public static IReadOnlyDictionary<string, string> NodesEnvironment(StackSettings s, RunPaths paths)
    {
        var env = new Dictionary<string, string>(NodesEnvironment(s), StringComparer.Ordinal)
        {
            ["Nodes__CaStoragePath"] = paths.Ca,
            ["Nodes__CaKeyPassword"] = s.CaPassword,
        };
        return env;
    }

    /// <summary>
    /// The Gateway environment, per the UAT contract's stack table. Service and MSI modes add the run's TLS PFX
    /// (<c>Kestrel__Certificates__Default__*</c>, P22); process mode and <c>-StackOnly</c> never do.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GatewayEnvironment(StackSettings s)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = $"https://localhost:{s.GatewayPort}",
            ["AgentIngress__ClientCertificateMode"] = "AllowCertificate",
            ["ReverseProxy__Clusters__nodes__Destinations__d1__Address"] = $"http://127.0.0.1:{s.NodesPort}/",
        };
        if (s.Tls is not null)
        {
            env["Kestrel__Certificates__Default__Path"] = s.Tls.PfxPath;
            env["Kestrel__Certificates__Default__Password"] = s.Tls.Password;
        }

        return env;
    }

    /// <summary>The Nodes child spec: the image from <see cref="RunPaths.StackImages"/>, its folder, and the Nodes environment (N5).</summary>
    internal static ChildSpec NodesSpec(StackSettings settings, RunPaths paths) =>
        new("nodes", paths.StackImages[0], [], Path.GetDirectoryName(paths.StackImages[0])!, NodesEnvironment(settings, paths));

    /// <summary>The Gateway child spec: the image from <see cref="RunPaths.StackImages"/>, its folder, and the Gateway environment (N5).</summary>
    internal static ChildSpec GatewaySpec(StackSettings settings, RunPaths paths) =>
        new("gateway", paths.StackImages[1], [], Path.GetDirectoryName(paths.StackImages[1])!, GatewayEnvironment(settings));

    /// <summary>
    /// Starts Nodes, waits for its <c>/healthz</c>, then starts the Gateway and waits for its own. A child that
    /// exits early fails with its exit code and the tail of its log.
    /// </summary>
    public async Task<Result> StartAsync(StackSettings settings, CancellationToken ct)
    {
        var nodes = ChildProcess.Start(NodesSpec(settings, _paths), _job, _redactor, _manifest);
        if (nodes.IsFailure)
        {
            return Result.Failure($"nodes did not start: {nodes.Error}");
        }

        _nodes = nodes.Value;
        var nodesHealthy = await WaitHealthyAsync("nodes", _nodes, NodesBase(settings.NodesPort), NodesHealthBudget, ct);
        if (nodesHealthy.IsFailure)
        {
            return nodesHealthy;
        }

        var gateway = ChildProcess.Start(GatewaySpec(settings, _paths), _job, _redactor, _manifest);
        if (gateway.IsFailure)
        {
            return Result.Failure($"gateway did not start: {gateway.Error}");
        }

        _gateway = gateway.Value;
        return await WaitHealthyAsync("gateway", _gateway, GatewayBase(settings.GatewayPort), GatewayHealthBudget, ct);
    }

    /// <summary>Stops the Gateway, then Nodes, and disposes both. Cleanup removes anything left.</summary>
    public async Task StopAsync()
    {
        if (_gateway is not null)
        {
            await _gateway.StopAsync();
            _gateway.Dispose();
            _gateway = null;
        }

        if (_nodes is not null)
        {
            await _nodes.StopAsync();
            _nodes.Dispose();
            _nodes = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gateway?.Dispose();
        _nodes?.Dispose();
    }

    private async Task<Result> WaitHealthyAsync(string name, ChildProcess child, Uri baseAddress, TimeSpan budget, CancellationToken ct)
    {
        var healthUri = new Uri(baseAddress, "healthz");
        using var client = GatewayClients.Create();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < budget)
        {
            ct.ThrowIfCancellationRequested();
            if (child.ExitCode is int code)
            {
                return Result.Failure($"{name} exited with code {code} before it was healthy:\n{LogTail(name)}");
            }

            try
            {
                using var response = await client.GetAsync(healthUri, ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return Result.Success();
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet; keep polling.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Probe timed out; keep polling.
            }

            await Task.Delay(PollInterval, ct);
        }

        return Result.Failure($"{name} did not return 200 from {healthUri.AbsolutePath} within {budget.TotalSeconds} s");
    }

    /// <summary>The last 30 lines of a child's log. The log is already redacted when written.</summary>
    private string LogTail(string name)
    {
        var path = Path.Combine(_paths.Logs, name + ".log");
        if (!File.Exists(path))
        {
            return "(no log)";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Split('\n');
        return string.Join('\n', lines.Select(_redactor.Redact).TakeLast(30));
    }
}
