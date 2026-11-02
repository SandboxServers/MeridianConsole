using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L7 on the real stack: the lab's <see cref="ToxiproxyClient"/> drives a real Toxiproxy in front of a real Nodes and Gateway.
/// S-L15 (F114, D-ML128) cuts <c>nodes-postgres</c> while Nodes' pool is idle, heals it, and asserts that the first enroll after the heal answers 401, not 500.
/// A 1000 ms latency toxic on <c>gateway-nodes</c> slows the Gateway probe; disabling <c>nodes-postgres</c> makes the Nodes DB path
/// (unknown-token enroll on the direct port) answer 5xx and healing restores 401; disabling <c>nodes-rabbitmq</c> makes Nodes
/// <c>/readyz</c> report <c>masstransit</c> Degraded or Unhealthy naming the proxy, and healing restores a fully healthy report (D-ML72); reset removes the latency toxic. Observation is on direct
/// ports only (R-OBSERVE) except the latency probe. The Gateway's CA route is never probed (D-ML63). D-ML70 moved the readiness leg
/// onto RabbitMQ because Nodes /readyz has no Postgres check (F67). Touches only <c>meridian-lab-*</c> resources and tears them down in <c>finally</c>.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class ToxiproxySeamTests
{
    private const string LinuxContext = "desktop-linux";
    private const string EnrollPath = "/api/v1/agents/enroll";
    private const string NodesEnrollPath = "/agents/enroll";          // Nodes direct (Gateway strips /api/v1)
    private const string LatencyToxic = "l19-latency";
    private const int LatencyMs = 1000;
    private const int PgWindowSeconds = 30, PgPollSeconds = 5;        // at most 7 direct enroll calls per wait
    private const int RabbitDownSeconds = 60, RabbitHealSeconds = 120; // heartbeat fallback and MassTransit reconnect backoff
    private const int BaselineSeconds = 60;

    private readonly ITestOutputHelper _output;

    public ToxiproxySeamTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Seam", "S-L7")]
    [Trait("RequiresDocker", "true")]
    public async Task ProxyFaults_ReachGatewayNodesDbAndRabbitPaths_ThenResetClearsToxics()
    {
        using SeamRig rig = CreateRig("toxi-seam");
        string repoRoot = rig.RepoRoot;
        string home = rig.Home;
        LabStack stack = rig.Stack;
        LabServices services = rig.Services;
        ToxiproxyClient toxi = rig.Toxi;
        HttpClient http = rig.Http;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        CancellationToken ct = cts.Token;

        try
        {
            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, ct);
            Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);

            Result<LabBuildDto> build = await LabBuilder.BuildAsync(
                repoRoot,
                LabBuilder.Projects.Where(project => project.Name is "nodes" or "gateway").ToList(),
                rig.Docker,
                rig.Paths,
                rig.Validator,
                TimeProvider.System,
                ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);

            Result<LabServiceStartDto> nodes = await services.StartAsync("nodes", build.Value.BuildId, ct);
            Assert.True(nodes.IsSuccess, nodes.IsFailure ? nodes.Error : string.Empty);

            Result<LabServiceStartDto> gateway = await services.StartAsync("gateway", build.Value.BuildId, ct);
            Assert.True(gateway.IsSuccess, gateway.IsFailure ? gateway.Error : string.Empty);

            // Baseline: Nodes is ready and the probe reaches the DB lookup (401, not 400).
            (int? Status, string Body) lastReady = (null, string.Empty);
            var baseClock = Stopwatch.StartNew();
            bool baseReady = await WaitUntilAsync(
                async () => IsFullyReady(lastReady = await ReadyzAsync(http, ct)),
                BaselineSeconds,
                2,
                ct);
            _output.WriteLine($"S-L7 timing rabbit-baseline-healthy={baseClock.ElapsedMilliseconds} ms");
            Assert.True(
                baseReady,
                $"Nodes /readyz not fully healthy {BaselineSeconds} s after start (masstransit must be Healthy before the fault). Body: {lastReady.Body}");
            ProbeResult baseline = await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct);
            Assert.Equal(401, baseline.Status);

            // Latency toxic on the Gateway-to-Nodes hop; it stays until the reset.
            var latency = new ToxicSpec(
                LatencyToxic,
                "latency",
                "downstream",
                1.0,
                new Dictionary<string, long> { ["latency"] = LatencyMs });
            Result added = await toxi.AddToxicAsync("gateway-nodes", latency, ct);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error : string.Empty);

            ProbeResult slow = await ProbeAsync(http, EnrollUri(), HttpMethod.Post, "{}", ct);
            Assert.Equal(400, slow.Status);
            Assert.True(
                slow.ElapsedMs >= LatencyMs,
                $"Enrollment probe took {slow.ElapsedMs} ms under a {LatencyMs} ms latency toxic.");

            // Nodes /readyz has no Postgres check (F67, D-ML71); the DB path is asserted instead. Add the readyz assertion here when D-ML71 lands.
            var pgClock = Stopwatch.StartNew();
            Result disablePg = await toxi.SetEnabledAsync("nodes-postgres", false, ct);
            Assert.True(disablePg.IsSuccess, disablePg.IsFailure ? disablePg.Error : string.Empty);
            Assert.False(
                await CanConnectAsync(ProxyPort("nodes-postgres"), ct),
                "The nodes-postgres listener still accepts new connections after it was disabled.");
            int? lastPg = null;
            bool pgFailed = await WaitUntilAsync(
                async () =>
                {
                    lastPg = (await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct)).Status;
                    return lastPg is >= 500 and < 600;
                },
                PgWindowSeconds,
                PgPollSeconds,
                ct);
            long pgDownMs = pgClock.ElapsedMilliseconds;
            _output.WriteLine($"S-L7 timing pg-disable-to-5xx={pgDownMs} ms");
            Assert.True(
                pgFailed,
                $"Nodes enroll still {lastPg} {PgWindowSeconds} s after nodes-postgres was disabled ({pgDownMs} ms); the DB path did not see the proxy fault.");

            pgClock.Restart();
            Result healPg = await toxi.SetEnabledAsync("nodes-postgres", true, ct);
            Assert.True(healPg.IsSuccess, healPg.IsFailure ? healPg.Error : string.Empty);
            int? lastHeal = null;
            bool pgHealed = await WaitUntilAsync(
                async () =>
                {
                    lastHeal = (await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct)).Status;
                    return lastHeal == 401;
                },
                PgWindowSeconds,
                PgPollSeconds,
                ct);
            long pgHealMs = pgClock.ElapsedMilliseconds;
            _output.WriteLine($"S-L7 timing pg-heal-to-401={pgHealMs} ms");
            Assert.True(pgHealed, $"Nodes enroll returned {lastHeal}, not 401, {PgWindowSeconds} s after nodes-postgres was enabled ({pgHealMs} ms).");

            // Degraded maps to HTTP 200 on /readyz (F68, D-ML73); the body is asserted, never the status code.
            string brokerMarker = $"{GatewayAdapter.ToxiproxyHost}:{ProxyPort("nodes-rabbitmq")}";
            var rabbitClock = Stopwatch.StartNew();
            Result disableRabbit = await toxi.SetEnabledAsync("nodes-rabbitmq", false, ct);
            Assert.True(disableRabbit.IsSuccess, disableRabbit.IsFailure ? disableRabbit.Error : string.Empty);
            bool degraded = await WaitUntilAsync(
                async () => IsRabbitDegraded(lastReady = await ReadyzAsync(http, ct), brokerMarker),
                RabbitDownSeconds,
                2,
                ct);
            long rabbitDownMs = rabbitClock.ElapsedMilliseconds;
            _output.WriteLine($"S-L7 timing rabbit-disable-to-degraded={rabbitDownMs} ms");
            Assert.True(
                degraded,
                $"Nodes /readyz did not report masstransit Degraded/Unhealthy naming {brokerMarker} within {RabbitDownSeconds} s of disabling nodes-rabbitmq ({rabbitDownMs} ms). Last HTTP {lastReady.Status}. Body: {lastReady.Body}");

            rabbitClock.Restart();
            Result healRabbit = await toxi.SetEnabledAsync("nodes-rabbitmq", true, ct);
            Assert.True(healRabbit.IsSuccess, healRabbit.IsFailure ? healRabbit.Error : string.Empty);
            bool readyAgain = await WaitUntilAsync(
                async () => IsFullyReady(lastReady = await ReadyzAsync(http, ct)),
                RabbitHealSeconds,
                2,
                ct);
            long rabbitHealMs = rabbitClock.ElapsedMilliseconds;
            _output.WriteLine($"S-L7 timing rabbit-heal-to-healthy={rabbitHealMs} ms");
            Assert.True(
                readyAgain,
                $"Nodes /readyz not fully healthy {RabbitHealSeconds} s after nodes-rabbitmq was enabled ({rabbitHealMs} ms). Last HTTP {lastReady.Status}. Body: {lastReady.Body}");

            // Reset: the latency toxic is really there, and reset removes it; every proxy is enabled.
            Assert.True(
                await ProxyHasToxicAsync(http, "gateway-nodes", LatencyToxic, ct),
                "The latency toxic is not listed on gateway-nodes before the reset.");
            Result reset = await toxi.ResetAsync(ct);
            Assert.True(reset.IsSuccess, reset.IsFailure ? reset.Error : string.Empty);
            await AssertNoToxicsAsync(http, ct);
        }
        finally
        {
            foreach (string name in new[] { "gateway", "nodes" })
            {
                _ = await services.StopAsync(name, CancellationToken.None);
            }

            _ = await stack.DownAsync(wipe: true, CancellationToken.None);

            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    [Trait("Seam", "S-L15")]
    [Trait("RequiresDocker", "true")]
    public async Task PostgresCutWhileIdle_FirstEnrollAfterHeal_Answers401()
    {
        using SeamRig rig = CreateRig("pg-cut-seam");
        string home = rig.Home;
        LabStack stack = rig.Stack;
        LabServices services = rig.Services;
        ToxiproxyClient toxi = rig.Toxi;
        HttpClient http = rig.Http;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        CancellationToken ct = cts.Token;

        try
        {
            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, ct);
            Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);

            // Nodes only: the pool is the seam, so no Gateway is needed.
            Result<LabBuildDto> build = await LabBuilder.BuildAsync(
                rig.RepoRoot,
                LabBuilder.Projects.Where(project => project.Name is "nodes").ToList(),
                rig.Docker,
                rig.Paths,
                rig.Validator,
                TimeProvider.System,
                ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);
            _output.WriteLine($"S-L15 build {build.Value.BuildId}");

            Result<LabServiceStartDto> nodes = await services.StartAsync("nodes", build.Value.BuildId, ct);
            Assert.True(nodes.IsSuccess, nodes.IsFailure ? nodes.Error : string.Empty);

            // Baseline: an unknown-token enroll reaches the DB lookup and answers 401.
            int? lastBaseline = null;
            bool baselineReady = await WaitUntilAsync(
                async () =>
                {
                    lastBaseline = (await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct)).Status;
                    return lastBaseline == 401;
                },
                BaselineSeconds,
                5,
                ct);
            Assert.True(baselineReady, $"Nodes did not answer 401 to an unknown-token enroll within {BaselineSeconds} s; last answer {lastBaseline}.");

            // Warm the pool: four parallel probes take four pooled Postgres connections, then the pool sits idle.
            ProbeResult[] warm = await Task.WhenAll(
                Enumerable.Range(0, 4).Select(_ => ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct)));
            foreach (ProbeResult probe in warm)
            {
                Assert.Equal(401, probe.Status);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            // Cut Postgres while Nodes is idle. No Nodes request is sent during the cut.
            Result disablePg = await toxi.SetEnabledAsync("nodes-postgres", false, ct);
            Assert.True(disablePg.IsSuccess, disablePg.IsFailure ? disablePg.Error : string.Empty);
            Assert.False(
                await CanConnectAsync(ProxyPort("nodes-postgres"), ct),
                "The nodes-postgres listener still accepts new connections after it was disabled.");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            // Heal. The wait probes the proxy listener only, never Nodes.
            Result healPg = await toxi.SetEnabledAsync("nodes-postgres", true, ct);
            Assert.True(healPg.IsSuccess, healPg.IsFailure ? healPg.Error : string.Empty);
            bool proxyUp = await WaitUntilAsync(() => CanConnectAsync(ProxyPort("nodes-postgres"), ct), 10, 1, ct);
            Assert.True(proxyUp, "The nodes-postgres listener did not accept connections 10 s after it was enabled.");

            // The first request after the heal is the one that must not 500 (F114, NR-1).
            ProbeResult first = await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct);
            _output.WriteLine($"S-L15 timing first-enroll-after-heal status={first.Status} elapsed={first.ElapsedMs} ms");
            Assert.True(
                first.Status == 401,
                $"First enroll after an idle Postgres cut answered {first.Status}, not 401 (F114 product half, D-ML128).");

            for (int i = 2; i <= 6; i++)
            {
                ProbeResult probe = await ProbeAsync(http, NodesEnrollUri(), HttpMethod.Post, UnknownTokenEnrollBody(), ct);
                _output.WriteLine($"S-L15 timing probe-{i}={probe.ElapsedMs} ms status={probe.Status}");
                Assert.True(probe.Status == 401, $"Enroll probe {i} after the heal answered {probe.Status}, not 401.");
            }
        }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None); // let the follower flush the last lines
            WriteNodesLogTail(rig.Supervisor, 60);
            _ = await services.StopAsync("nodes", CancellationToken.None);
            _ = await stack.DownAsync(wipe: true, CancellationToken.None);

            Directory.Delete(home, recursive: true);
        }
    }

    private static Uri ReadyzUri() => new($"http://127.0.0.1:{LabPorts.Nodes}/readyz");

    private static Uri NodesEnrollUri() => new($"http://127.0.0.1:{LabPorts.Nodes}{NodesEnrollPath}");

    // Product contract DTO, never a local copy (D-ML7). A fresh random token is never minted, so the DB lookup misses it.
    private static string UnknownTokenEnrollBody() => JsonSerializer.Serialize(
        new Dhadgar.Contracts.Nodes.EnrollNodeRequest(
            Token: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Platform: "linux",
            Hardware: new Dhadgar.Contracts.Nodes.HardwareInventoryRequest("lab-s-l7-probe", null, 1, 1, 1, null),
            PublicKey: "AAAA"),
        JsonSerializerOptions.Web);

    private static int ProxyPort(string name) => ProxyCatalog.Find(name)?.ListenPort
        ?? throw new InvalidOperationException($"Proxy {name} is not in the C10 catalog.");

    private static async Task<(int? Status, string Body)> ReadyzAsync(HttpClient http, CancellationToken ct)
    {
        Uri uri = ReadyzUri();
        Assert.True(LabHosts.IsLabEndpoint(uri), $"Probe target is not a lab endpoint: {uri}");
        try
        {
            using HttpResponseMessage response = await http.GetAsync(uri, ct);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException)
        {
            return (null, string.Empty);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, string.Empty);
        }
    }

    private readonly record struct ReadyBody(string Top, string MassTransit, string MassTransitRaw);

    private static ReadyBody ParseReady(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string top = root.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? "null" : "missing";
            if (!root.TryGetProperty("checks", out JsonElement checks) || !checks.TryGetProperty("masstransit", out JsonElement mt))
            {
                return new ReadyBody(top, "missing", string.Empty);
            }

            string status = mt.TryGetProperty("status", out JsonElement ms) ? ms.GetString() ?? "null" : "missing";
            return new ReadyBody(top, status, mt.GetRawText());
        }
        catch (JsonException)
        {
            return new ReadyBody("unparseable", "unparseable", string.Empty);
        }
    }

    private static bool IsFullyReady((int? Status, string Body) r)
    {
        ReadyBody b = ParseReady(r.Body);
        return r.Status == 200 && b.Top == "ok" && b.MassTransit == "Healthy";
    }

    private static bool IsRabbitDegraded((int? Status, string Body) r, string brokerMarker)
    {
        ReadyBody b = ParseReady(r.Body);
        return r.Status is 200 or 503
            && b.Top != "ok"
            && (b.MassTransit is "Degraded" or "Unhealthy")
            && b.MassTransitRaw.Contains(brokerMarker, StringComparison.Ordinal);
    }

    private static async Task<bool> ProxyHasToxicAsync(HttpClient http, string proxy, string toxicName, CancellationToken ct)
    {
        var uri = new Uri($"http://127.0.0.1:{LabPorts.ToxiproxyApi}/proxies");
        Assert.True(LabHosts.IsLabInfraEndpoint(uri), "Toxiproxy API is not an infra endpoint.");
        using HttpResponseMessage response = await http.GetAsync(uri, ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty(proxy, out JsonElement entry))
        {
            return false;
        }

        foreach (JsonElement toxic in entry.GetProperty("toxics").EnumerateArray())
        {
            if (toxic.GetProperty("name").GetString() == toxicName)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken ct)
    {
        using var client = new System.Net.Sockets.TcpClient();
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync("127.0.0.1", port, attempt.Token);
            return client.Connected;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the last <paramref name="count"/> captured Nodes log lines (already redacted) to the test output. S-L15's record
    /// needs them: the red shows the dead-connection 500, and the green shows EF's retry (D-ML144).
    /// </summary>
    private void WriteNodesLogTail(LabProcessSupervisor supervisor, int count)
    {
        Result<IReadOnlyList<LogLine>> lines = supervisor.ReadLines("nodes");
        if (lines.IsFailure)
        {
            _output.WriteLine($"S-L15 nodes-log unavailable: {lines.Error}");
            return;
        }

        foreach (LogLine line in lines.Value!.TakeLast(count))
        {
            _output.WriteLine($"S-L15 nodes-log {line.Timestamp:O} {line.Level} {line.Category} {line.Message}");
        }
    }

    private static Uri EnrollUri() => new($"http://127.0.0.1:{LabPorts.Gateway}{EnrollPath}");

    private static async Task<ProbeResult> ProbeAsync(HttpClient http, Uri uri, HttpMethod method, string? body, CancellationToken ct)
    {
        Assert.True(LabHosts.IsLabEndpoint(uri), $"Probe target is not a lab endpoint: {uri}");
        using var request = new HttpRequestMessage(method, uri);
        if (uri.Port == LabPorts.Gateway)
        {
            // The Gateway's AllowedHosts refuse the loopback IP literal with 400 before routing; use the lab probe host.
            request.Headers.Host = LabServices.ProbeHostHeader;
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var clock = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            clock.Stop();
            return new ProbeResult((int)response.StatusCode, clock.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return new ProbeResult(null, clock.ElapsedMilliseconds);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(null, clock.ElapsedMilliseconds);
        }
    }

    private static async Task AssertNoToxicsAsync(HttpClient http, CancellationToken ct)
    {
        var uri = new Uri($"http://127.0.0.1:{LabPorts.ToxiproxyApi}/proxies");
        Assert.True(LabHosts.IsLabInfraEndpoint(uri), "Toxiproxy API is not an infra endpoint.");
        using HttpResponseMessage response = await http.GetAsync(uri, ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        int proxies = 0;
        foreach (JsonProperty proxy in document.RootElement.EnumerateObject())
        {
            proxies++;
            Assert.Equal(0, proxy.Value.GetProperty("toxics").GetArrayLength());
            Assert.True(proxy.Value.GetProperty("enabled").GetBoolean(), $"Proxy {proxy.Name} is still disabled after reset.");
        }

        Assert.True(proxies > 0, "Toxiproxy lists no proxies.");
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int windowSeconds, int pollSeconds, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(windowSeconds))
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
        }

        return false;
    }

    /// <summary>
    /// The lab object graph shared by the S-L7 and S-L15 seams: a real stack, Toxiproxy, the Nodes and Gateway services and a direct-port HTTP client.
    /// Disposing it releases the process-local objects; the caller stops the services, takes the stack down and deletes <see cref="Home"/>.
    /// </summary>
    private sealed class SeamRig : IDisposable
    {
        private readonly SecretVault _vault;
        private readonly ToxiproxyClient _toxi;
        private readonly LabProcessSupervisor _supervisor;
        private readonly SocketsHttpHandler _handler;
        private readonly HttpClient _http;

        public SeamRig(
            string repoRoot,
            string home,
            DockerCli docker,
            LabPaths paths,
            PathValidator validator,
            SecretVault vault,
            ToxiproxyClient toxi,
            LabStack stack,
            LabServices services,
            LabProcessSupervisor supervisor,
            SocketsHttpHandler handler,
            HttpClient http)
        {
            RepoRoot = repoRoot;
            Home = home;
            Docker = docker;
            Paths = paths;
            Validator = validator;
            _vault = vault;
            _toxi = toxi;
            Stack = stack;
            Services = services;
            _supervisor = supervisor;
            _handler = handler;
            _http = http;
        }

        public string RepoRoot { get; }

        public string Home { get; }

        public DockerCli Docker { get; }

        public LabPaths Paths { get; }

        public PathValidator Validator { get; }

        public LabStack Stack { get; }

        public LabServices Services { get; }

        public ToxiproxyClient Toxi => _toxi;

        public HttpClient Http => _http;

        public LabProcessSupervisor Supervisor => _supervisor;

        public void Dispose()
        {
            _http.Dispose();
            _handler.Dispose();
            _supervisor.Dispose();
            Stack.Dispose();
            _toxi.Dispose();
            _vault.Dispose();
        }
    }

    /// <summary>Builds the S-L7 and S-L15 object graph under a fresh <c>meridian-lab-{prefix}-*</c> home directory.</summary>
    private static SeamRig CreateRig(string prefix)
    {
        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-" + prefix + "-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        var paths = LabPaths.FromRoot(home, repoRoot);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var docker = new DockerCli(new DockerOptions(LinuxContext, null));
        var vault = new SecretVault();
        var toxi = new ToxiproxyClient();
        var refs = new LabCredentialRefs();
        var targets = new LabTargetRegistry(paths, docker, TimeProvider.System);
        var stack = new LabStack(docker, targets, toxi, vault, refs, paths, validator, TimeProvider.System);
        var connections = new LabConnectionStrings(vault, refs);
        var redactor = new Redactor(vault);
        var leases = new LeaseBook(TimeProvider.System, new NullLabEvents(), new LeaseOptions());
        var supervisor = new LabProcessSupervisor(
            new LogPathGuard(paths, validator),
            redactor,
            leases,
            TimeProvider.System,
            new NullRawLineObserver(),
            new NoFaults());
        var logs = new ContainerLogFollower(supervisor, new ConfigurationBuilder().Build(), TimeProvider.System, paths);
        var overlay = new ConfigOverlay(paths, validator);
        var services = new LabServices(
            [new GatewayAdapter(), new NodesAdapter()],
            docker,
            targets,
            stack,
            connections,
            refs,
            vault,
            logs,
            overlay,
            validator,
            paths,
            TimeProvider.System);

        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
        return new SeamRig(repoRoot, home, docker, paths, validator, vault, toxi, stack, services, supervisor, handler, http);
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "tools", "lab", "images", "rabbitmq.Dockerfile")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Repository root with tools/lab/images was not found above the test binaries.");
    }

    private readonly record struct ProbeResult(int? Status, long ElapsedMs);
}
