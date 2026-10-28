using System.Net;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>
/// Builds Gateway and Nodes from the working tree, starts the lab stack, starts both services, fetches the CA
/// certificate directly from Nodes and checks that the Gateway forwards health and enrollment through agent ingress. Runs only where the Linux Docker engine is available.
/// Touches only <c>meridian-lab-*</c> resources; tears them down in <c>finally</c>.
/// </summary>
[Collection("LabDockerStack")]
public sealed class ServicesDockerTests
{
    private const string LinuxContext = "desktop-linux";

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task BuildUpStart_NodesAndGateway_ServeCaDirectAndForwardThroughIngress()
    {
        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-services-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        var paths = LabPaths.FromRoot(home, repoRoot);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var docker = new DockerCli(new DockerOptions(LinuxContext, null));
        using var vault = new SecretVault();
        using var toxi = new ToxiproxyClient();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Host = LabServices.ProbeHostHeader;
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        CancellationToken ct = cts.Token;
        bool stackUp = false;

        try
        {
            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, ct);
            Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);
            stackUp = true;

            Result<LabBuildDto> build = await LabBuilder.BuildAsync(
                repoRoot,
                LabBuilder.Projects.Where(project => project.Name is "nodes" or "gateway").ToList(),
                docker,
                paths,
                validator,
                TimeProvider.System,
                ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);

            Result<LabServiceStartDto> nodes = await services.StartAsync("nodes", build.Value.BuildId, ct);
            Assert.True(nodes.IsSuccess, nodes.IsFailure ? nodes.Error : string.Empty);

            Result<LabServiceStartDto> gateway = await services.StartAsync("gateway", build.Value.BuildId, ct);
            Assert.True(gateway.IsSuccess, gateway.IsFailure ? gateway.Error : string.Empty);

            // Nodes direct: the CA endpoint is anonymous at Nodes.
            using (HttpResponseMessage ca = await http.GetAsync($"http://127.0.0.1:{LabPorts.Nodes}/agents/ca-certificate", ct))
            {
                Assert.Equal(HttpStatusCode.OK, ca.StatusCode);
                Assert.Contains("BEGIN CERTIFICATE", await ca.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            }

            // Agent ingress reaches the Gateway.
            using (HttpResponseMessage ingress = await http.GetAsync($"http://127.0.0.1:{LabPorts.AgentIngress}/healthz", ct))
            {
                Assert.Equal(HttpStatusCode.OK, ingress.StatusCode);
            }

            // Gateway forwards to Nodes through gateway-nodes, via both host paths. Only Nodes emits invalid_platform.
            foreach (int port in new[] { LabPorts.Gateway, LabPorts.AgentIngress })
            {
                using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                using HttpResponseMessage enroll = await http.PostAsync($"http://127.0.0.1:{port}/api/v1/agents/enroll", body, ct);
                string text = await enroll.Content.ReadAsStringAsync(ct);
                Assert.True(enroll.StatusCode == HttpStatusCode.BadRequest && text.Contains("invalid_platform", StringComparison.Ordinal),
                    $"port {port}: {(int)enroll.StatusCode} {text[..Math.Min(200, text.Length)]}");
            }
        }
        finally
        {
            foreach (string name in new[] { "gateway", "nodes" })
            {
                _ = await services.StopAsync(name, CancellationToken.None);
            }

            if (stackUp)
            {
                _ = await stack.DownAsync(wipe: true, CancellationToken.None);
            }

            Directory.Delete(home, recursive: true);
        }
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
}
