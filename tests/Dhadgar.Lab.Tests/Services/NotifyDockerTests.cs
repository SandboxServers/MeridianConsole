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
/// Builds Notifications and Discord from the working tree, starts the lab stack and both services, and checks the
/// asserted states: Notifications answers <c>/healthz</c>, and Discord answers <c>/readyz</c> 503 while its bot is absent
/// (D-ML41, F26). Runs only where the Linux Docker engine is available. Touches only <c>meridian-lab-*</c> resources.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class NotifyDockerTests
{
    private const string LinuxContext = "desktop-linux";

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task BuildUpStart_NotificationsAndDiscord_StartAndReportTheAssertedStates()
    {
        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-notify-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        var paths = LabPaths.FromRoot(home, repoRoot);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var docker = new DockerCli(new DockerOptions(LinuxContext, null));
        using var vault = new SecretVault();
        using var toxi = new ToxiproxyClient();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
            [new GatewayAdapter(), new NotificationsLabAdapter(), new DiscordLabAdapter()],
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
                LabBuilder.Projects.Where(project => project.Name is "notifications" or "discord").ToList(),
                docker,
                paths,
                validator,
                TimeProvider.System,
                ct);
            Assert.True(build.IsSuccess, build.IsFailure ? build.Error : string.Empty);

            Result<LabServiceStartDto> notifications = await services.StartAsync("notifications", build.Value.BuildId, ct);
            Assert.True(notifications.IsSuccess, notifications.IsFailure ? notifications.Error : string.Empty);

            Result<LabServiceStartDto> discord = await services.StartAsync("discord", build.Value.BuildId, ct);
            Assert.True(discord.IsSuccess, discord.IsFailure ? discord.Error : string.Empty);

            // Notifications is up behind its direct host port.
            using (HttpResponseMessage health = await http.GetAsync($"http://127.0.0.1:{LabPorts.Notifications}/healthz", ct))
            {
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            }

            // Discord has no bot in the lab (D-ML41): readiness is the asserted 503.
            using (HttpResponseMessage ready = await http.GetAsync($"http://127.0.0.1:{LabPorts.Discord}/readyz", ct))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            }
        }
        finally
        {
            foreach (string name in new[] { "discord", "notifications" })
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
