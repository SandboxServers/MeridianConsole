using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Nodes;
using Dhadgar.Lab.Tools.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L2 on the real stack: the lab mints an enrollment token through the Nodes adapter, the Gateway forwards the enrollment
/// (201), a second use of the same token is refused (401), and <c>stack_reset_data</c> empties <c>enrollment_tokens</c>
/// while leaving the migrations history intact. Runs only where the Linux Docker engine is available; touches only
/// <c>meridian-lab-*</c> resources and tears them down in <c>finally</c>.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class NodesSeamDockerTests
{
    private const string LinuxContext = "desktop-linux";

    [Fact]
    [Trait("Seam", "S-L2")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task MintedToken_EnrollsThroughGateway_ThenIsRefused_AndResetClearsIt()
    {
        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-nodes-seam-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
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

        var httpServices = new ServiceCollection();
        _ = httpServices.AddHttpClient(LabHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(LabHttp.CreateHandler)
            .ConfigureHttpClient(client => client.Timeout = LabHttp.RequestTimeout);
        using ServiceProvider httpProvider = httpServices.BuildServiceProvider();
        var http = new LabHttp(httpProvider.GetRequiredService<IHttpClientFactory>(), vault, redactor, TimeProvider.System);

        var nodesAdapter = new NodesLabAdapter(
            NodesLabContexts.ForOwner(connections),
            vault,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        var mint = new NodesTokenMintTool(nodesAdapter);
        var probe = new NodesEnrollProbeTool(http, vault);
        var reset = new StackResetDataTool(new LabDataReset(connections));
        var database = new LabDb(connections);
        var context = new LabCallContext("call-nodes-seam", null, "test");

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

            // Mint: the result carries a vault reference and never the plain token.
            Result<JsonElement> minted = await mint.InvokeAsync(default, context, ct);
            Assert.True(minted.IsSuccess, minted.Error);
            string tokenRef = minted.Value.GetProperty("token_ref").GetString() ?? string.Empty;
            Assert.StartsWith("tok-", tokenRef, StringComparison.Ordinal);

            // First enrollment through the Gateway: 201 with a node id.
            JsonElement first = await ProbeAsync(probe, tokenRef, context, ct);
            Assert.True(first.GetProperty("status").GetInt32() == 201, first.GetRawText());
            Assert.True(Guid.TryParse(first.GetProperty("node_id").GetString(), out Guid nodeId) && nodeId != Guid.Empty);

            // Second use of the same token: refused.
            JsonElement second = await ProbeAsync(probe, tokenRef, context, ct);
            Assert.Equal(401, second.GetProperty("status").GetInt32());

            // The enrollment token row exists before the reset and the migrations history is populated.
            long tokensBefore = await CountAsync(database, "enrollment_tokens", ct);
            long migrationsBefore = await CountAsync(database, "\"__EFMigrationsHistory\"", ct);
            Assert.True(tokensBefore >= 1, $"enrollment_tokens before reset: {tokensBefore}");
            Assert.True(migrationsBefore >= 1, $"__EFMigrationsHistory before reset: {migrationsBefore}");

            // Reset every lab database: the tokens go, the migrations history stays.
            Result<JsonElement> resetResult = await reset.InvokeAsync(default, context, ct);
            Assert.True(resetResult.IsSuccess, resetResult.Error);

            Assert.Equal(0, await CountAsync(database, "enrollment_tokens", ct));
            Assert.Equal(migrationsBefore, await CountAsync(database, "\"__EFMigrationsHistory\"", ct));
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

    private static async Task<JsonElement> ProbeAsync(NodesEnrollProbeTool probe, string tokenRef, LabCallContext context, CancellationToken ct)
    {
        using JsonDocument args = JsonDocument.Parse($"{{\"token_ref\":\"{tokenRef}\",\"platform\":\"linux\",\"via\":\"gateway\"}}");
        Result<JsonElement> result = await probe.InvokeAsync(args.RootElement, context, ct);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value.Clone();
    }

    private static async Task<long> CountAsync(LabDb database, string table, CancellationToken ct)
    {
        Result<DbQueryResult> result = await database.QueryAsync(
            "nodes",
            $"select count(*) as n from {table}",
            new Dictionary<string, JsonElement>(),
            1,
            ct);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value.Rows[0]["n"].GetInt64();
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
