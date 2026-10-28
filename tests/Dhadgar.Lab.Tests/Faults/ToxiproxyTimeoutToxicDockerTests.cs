using System.Net.Sockets;
using System.Text;
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

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// Checks the C19 assumption behind the mTLS fallback: a Toxiproxy <c>timeout</c> toxic with <c>timeout = 0</c> added AFTER a
/// connection is open also holds that connection's data. Brings up its own lab stack, Nodes and the Gateway (D-ML84), and probes the
/// Gateway through the <c>agent-ingress</c> proxy. If the second request completes, the assumption is false: delete this test and
/// record "Toxiproxy does not apply new toxics to open connections".
/// </summary>
[Collection(LabDockerGate.Name)]
[Trait("RequiresDocker", "true")]
public sealed class ToxiproxyTimeoutToxicDockerTests
{
    private const string Proxy = "agent-ingress";
    private const string ToxicName = "l51-timeout";
    private const string LinuxContext = "desktop-linux";
    private static readonly byte[] Request = Encoding.ASCII.GetBytes(
        "GET /healthz HTTP/1.1\r\nHost: " + LabServices.ProbeHostHeader + "\r\nConnection: keep-alive\r\n\r\n");

    [Fact]
    public async Task TimeoutToxic_AddedToAnOpenConnection_HoldsTheNextRequest()
    {
        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-l51-timeout-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        // Same wiring as ToxiproxySeamTests.cs:55-87: paths, validator, docker (LinuxContext), vault, toxiproxy, refs, targets, stack,
        // connections, redactor, leases, supervisor, logs, overlay, services ([new GatewayAdapter(), new NodesAdapter()]).
        var paths = LabPaths.FromRoot(home, repoRoot);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var docker = new DockerCli(new DockerOptions(LinuxContext, null));
        using var vault = new SecretVault();
        using var toxi = new ToxiproxyClient();
        var refs = new LabCredentialRefs();
        var targets = new LabTargetRegistry(paths, docker, TimeProvider.System);
        using var stack = new LabStack(docker, targets, toxi, vault, refs, paths, validator, TimeProvider.System);
        var connections = new LabConnectionStrings(vault, refs);
        var redactor = new Redactor(vault);
        var leases = new LeaseBook(TimeProvider.System, new NullLabEvents(), new LeaseOptions());
        using var supervisor = new LabProcessSupervisor(
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        CancellationToken ct = cts.Token;
        try
        {
            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, ct);
            Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);
            Result<LabBuildDto> build = await LabBuilder.BuildAsync(repoRoot,
                LabBuilder.Projects.Where(project => project.Name is "nodes" or "gateway").ToList(),
                docker, paths, validator, TimeProvider.System, ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);
            foreach (string name in new[] { "nodes", "gateway" })   // the Gateway's readiness needs Nodes
            {
                Result<LabServiceStartDto> started = await services.StartAsync(name, build.Value.BuildId, ct);
                Assert.True(started.IsSuccess, started.IsFailure ? started.Error : string.Empty);
            }

            Assert.True(LabHosts.IsLabEndpoint(new Uri($"http://127.0.0.1:{LabPorts.AgentIngress}/healthz")), "agent-ingress is not a lab endpoint");
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", LabPorts.AgentIngress, ct);
            NetworkStream stream = client.GetStream();

            await stream.WriteAsync(Request, ct);
            string first = await ReadSomeAsync(stream, TimeSpan.FromSeconds(10));
            Assert.StartsWith("HTTP/1.1 200", first, StringComparison.Ordinal);

            var spec = new ToxicSpec(ToxicName, "timeout", "downstream", 1.0, new Dictionary<string, long> { ["timeout"] = 0 });
            Result added = await toxi.AddToxicAsync(Proxy, spec, CancellationToken.None);
            Assert.True(added.IsSuccess, added.Error);
            try
            {
                await stream.WriteAsync(Request, ct);
                string second = await ReadSomeAsync(stream, TimeSpan.FromSeconds(3));
                Assert.Equal(string.Empty, second);
            }
            finally
            {
                _ = await toxi.RemoveToxicAsync(Proxy, ToxicName, CancellationToken.None);
            }
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

    private static async Task<string> ReadSomeAsync(NetworkStream stream, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        var buffer = new byte[4096];
        try
        {
            int read = await stream.ReadAsync(buffer, cts.Token);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
