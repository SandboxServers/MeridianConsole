using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam test S4: a fresh agent host with a token enrolls through the Gateway, persists its identity, connects and
/// heartbeats. A second agent host over the same state directory and certificate store reuses that identity and
/// connects without enrolling again (#101).
/// </summary>
public sealed class AgentBootstrapSeamTests : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-bootstrap-seam-ca-{Guid.NewGuid()}");
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), $"agent-bootstrap-seam-state-{Guid.NewGuid()}");

    public AgentBootstrapSeamTests()
    {
        Directory.CreateDirectory(_caDirectory);
        Directory.CreateDirectory(_stateDirectory);
    }

    public void Dispose()
    {
        TryDelete(_caDirectory);
        TryDelete(_stateDirectory);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task FreshAgent_EnrollsPersistsConnectsAndHeartbeats_ThenRestartReusesIdentity()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var orgId = Guid.NewGuid();
        var token = await harness.CreateEnrollmentTokenAsync(orgId);
        var store = new InMemoryCertificateStore();

        // Run 1: enrolls with the token. The Nodes clock has not been advanced yet (D-S234).
        var run1Enrollments = new EnrollmentCounter();
        Node firstNode;
        using (var host = BuildAgentHost(harness, store, new FixedTokenSource(token), run1Enrollments))
        {
            await host.StartAsync();
            var nodes = await WaitForNodesAsync(harness, orgId, list => list.Any(n => n.LastHeartbeat is not null));
            await host.StopAsync();

            Assert.Single(nodes);
            firstNode = nodes[0];
        }

        Assert.Equal(1, run1Enrollments.Calls);
        Assert.True(File.Exists(StateFilePath), "agent-state.json was not written by the first run.");
        Assert.Equal(firstNode.Id, ReadStoredNodeId());

        // Between runs the Nodes clock moves 2 minutes. Budget: at most 4 minutes over the whole test (D-S234),
        // so the Nodes CA's NotBefore stays inside the agent's 5-minute skew window.
        harness.NodesBase.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var heartbeatBeforeRestart = firstNode.LastHeartbeat;

        // Run 2: same state directory and store, a token source that is empty (D-S232). The agent must reuse its identity.
        var run2Enrollments = new EnrollmentCounter();
        using (var host = BuildAgentHost(harness, store, new FixedTokenSource(string.Empty), run2Enrollments))
        {
            await host.StartAsync();
            var nodes = await WaitForNodesAsync(
                harness,
                orgId,
                list => list.Any(n => n.LastHeartbeat > heartbeatBeforeRestart));
            await host.StopAsync();

            Assert.Single(nodes);
            Assert.Equal(firstNode.Id, nodes[0].Id);
        }

        Assert.Equal(0, run2Enrollments.Calls);
    }

    private string StateFilePath => Path.Combine(_stateDirectory, "agent-state.json");

    private Guid ReadStoredNodeId()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(StateFilePath));
        return document.RootElement.GetProperty("nodeId").GetGuid();
    }

    /// <summary>
    /// Builds a real agent host from <c>AddAgentCore</c>, with only the platform and transport seams replaced:
    /// the shared certificate store, the Gateway client factory, a long-polling hub transport that presents the
    /// certificate through the Gateway, and the token source. The agent's own hosted services are kept:
    /// <c>AgentBootstrapService</c> and <c>HeartbeatService</c>. Neither needs Windows or a real process,
    /// so nothing is removed.
    /// </summary>
    private IHost BuildAgentHost(
        GatewayNodesHarness harness,
        InMemoryCertificateStore store,
        IEnrollmentTokenSource tokens,
        EnrollmentCounter enrollments)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{AgentOptions.SectionName}:ControlPlane:Endpoint"] = "https://localhost",
            [$"{AgentOptions.SectionName}:StateDirectory"] = _stateDirectory,
            [$"{AgentOptions.SectionName}:ControlPlane:HeartbeatIntervalSeconds"] = "1"
        });

        var services = builder.Services;

        // The real host binds these sections in AgentHostBuilderExtensions; AddAgentCore does not bind them.
        services.AddOptions<AgentOptions>().Bind(builder.Configuration.GetSection(AgentOptions.SectionName));
        services.AddOptions<ControlPlaneOptions>().Bind(builder.Configuration.GetSection($"{AgentOptions.SectionName}:ControlPlane"));
        services.AddOptions<SecurityOptions>().Bind(builder.Configuration.GetSection($"{AgentOptions.SectionName}:Security"));

        services.AddAgentCore();

        // The Windows and Linux hosts register IProcessManager. HealthReporter only takes it and never calls it,
        // and the bootstrap and heartbeat never start a process, so a substitute that is never called is enough.
        services.RemoveAll<IProcessManager>();
        services.AddSingleton(Substitute.For<IProcessManager>());

        services.RemoveAll<ICertificateStore>();
        services.AddSingleton<ICertificateStore>(store);

        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton<IHttpClientFactory>(new TestServerClientFactory(harness.GatewayServer, stripApiPrefix: false));

        services.RemoveAll<ControlPlaneHubTransport>();
        services.AddSingleton(new ControlPlaneHubTransport
        {
            HttpMessageHandlerFactory = AgentSeamHarness.CertificateInjectingHandlerFactory(harness.GatewayServer),
            Transports = HttpTransportType.LongPolling
        });

        services.RemoveAll<IEnrollmentTokenSource>();
        services.AddSingleton(tokens);

        services.RemoveAll<IEnrollmentService>();
        services.AddSingleton<IEnrollmentService>(sp => new CountingEnrollmentService(
            ActivatorUtilities.CreateInstance<Dhadgar.Agent.Core.Authentication.EnrollmentService>(sp),
            enrollments));

        return builder.Build();
    }

    /// <summary>
    /// Polls the Nodes database for the organisation's nodes until <paramref name="condition"/> holds, or fails after 15 s.
    /// </summary>
    private static async Task<List<Node>> WaitForNodesAsync(
        GatewayNodesHarness harness,
        Guid orgId,
        Func<List<Node>, bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        List<Node> nodes = [];
        do
        {
            using var scope = harness.Nodes.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            nodes = await db.Nodes.AsNoTracking().Where(n => n.OrganizationId == orgId).ToListAsync();
            if (condition(nodes))
            {
                return nodes;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Timed out after {WaitTimeout.TotalSeconds} s waiting for the Nodes database (org {orgId}).");
        return nodes;
    }

    /// <summary>Returns one fixed token, or the empty string when no token is configured (D-S232).</summary>
    private sealed class FixedTokenSource(string token) : IEnrollmentTokenSource
    {
        public Result<string> GetToken() => Result<string>.Success(token);
    }

    /// <summary>Counts <c>EnrollAsync</c> calls.</summary>
    private sealed class EnrollmentCounter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Increment() => Interlocked.Increment(ref _calls);
    }

    /// <summary>Wraps the real enrollment service and counts how often the agent enrolls.</summary>
    private sealed class CountingEnrollmentService(IEnrollmentService inner, EnrollmentCounter counter) : IEnrollmentService
    {
        public Task<Result<EnrollmentResult>> EnrollAsync(string enrollmentToken, CancellationToken cancellationToken = default)
        {
            counter.Increment();
            return inner.EnrollAsync(enrollmentToken, cancellationToken);
        }

        public bool IsEnrolled => inner.IsEnrolled;

        public Task<Result<CertificateRenewalResult>> RenewCertificateAsync(CancellationToken cancellationToken = default) =>
            inner.RenewCertificateAsync(cancellationToken);
    }
}
