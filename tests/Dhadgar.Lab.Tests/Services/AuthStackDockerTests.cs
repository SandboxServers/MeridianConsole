using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Xunit.Abstractions;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>Builds and starts Identity, BetterAuth, Panel, Nodes and Gateway in the lab and checks each one answers its health path. Runs only where the Linux Docker engine is available; touches only <c>meridian-lab-*</c> resources.</summary>
[Collection(LabDockerGate.Name)]
public sealed class AuthStackDockerTests
{
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";
    private readonly ITestOutputHelper _output;

    public AuthStackDockerTests(ITestOutputHelper output) => _output = output;

    [SkippableFact(Timeout = 1_500_000)]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task AuthStack_SignUpThroughGateway()
    {
        string linuxContext = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        (int versionExit, _) = RunDocker(linuxContext, ["version", "--format", "{{.Server.Version}}"]);
        Skip.IfNot(versionExit == 0, "docker is not reachable on context " + linuxContext);

        (int psExit, string labelled) = RunDocker(linuxContext, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        Assert.True(psExit == 0, "docker ps failed");
        if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
        {
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the test's stack_up wipe remove them");
        }

        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-services-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        var paths = LabPaths.FromRoot(home, repoRoot);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var docker = new DockerCli(new DockerOptions(linuxContext, null));
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
            [new GatewayAdapter(), new NodesAdapter(), new IdentityLabAdapter(), new BetterAuthLabAdapter(), new PanelLabAdapter()],
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
                LabBuilder.Projects.Where(project => project.Name is "nodes" or "gateway" or "identity" or "betterauth" or "panel").ToList(),
                docker,
                paths,
                validator,
                TimeProvider.System,
                ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);

            foreach (string name in new[] { "nodes", "identity", "betterauth", "panel", "gateway" })
            {
                Result<LabServiceStartDto> started = await services.StartAsync(name, build.Value.BuildId, ct);
                Assert.True(started.IsSuccess, started.IsFailure ? started.Error : string.Empty);
            }

            foreach ((int port, string path) in new[] { (LabPorts.Identity, "/healthz"), (LabPorts.BetterAuth, "/healthz"), (LabPorts.Panel, "/login"), (LabPorts.Gateway, "/healthz") })
            {
                using HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{port}{path}", ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // D-ML152: Identity must not touch Azure Key Vault or the Azure credential chain in the lab.
            Result<IReadOnlyList<LogLine>> identityLines = supervisor.ReadLines("identity");
            Assert.True(identityLines.IsSuccess, identityLines.IsFailure ? identityLines.Error : string.Empty);
            Assert.DoesNotContain(identityLines.Value!, line =>
                line.Message.Contains("DefaultAzureCredential", StringComparison.Ordinal)
                || line.Message.Contains("Azure.Identity", StringComparison.Ordinal)
                || line.Message.Contains("vault.azure.net", StringComparison.Ordinal)
                || line.Category.StartsWith("Azure.", StringComparison.Ordinal));

            string email = $"lab-{Guid.NewGuid():N}@lab.invalid";
            string password = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
            using var signUp = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{LabPorts.Gateway}/api/v1/betterauth/sign-up/email")
            {
                Content = JsonContent.Create(new { email, password, name = "lab" }),
            };
            signUp.Headers.Add("Origin", BetterAuthLabAdapter.TrustedOrigin);
            using HttpResponseMessage signedUp = await http.SendAsync(signUp, ct);
            // The body carries a session token: never print it.
            Assert.True(signedUp.StatusCode == HttpStatusCode.OK, $"sign-up through the Gateway answered {(int)signedUp.StatusCode}");
        }
        finally
        {
            // Let the log followers flush before the tails are read; the tails are written before any container stops.
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            ServiceLogTail.Write(_output, supervisor, ["nodes", "identity", "betterauth", "panel", "gateway"], 80);

            foreach (string name in new[] { "gateway", "panel", "betterauth", "identity", "nodes" })
            {
                _ = await services.StopAsync(name, CancellationToken.None);
            }

            if (stackUp)
            {
                _ = await stack.DownAsync(wipe: true, CancellationToken.None);
            }

            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Best effort: the temp home is disposable.
            }
        }
    }

    private static (int ExitCode, string Output) RunDocker(string context, string[] args)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--context");
        psi.ArgumentList.Add(context);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty);
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

