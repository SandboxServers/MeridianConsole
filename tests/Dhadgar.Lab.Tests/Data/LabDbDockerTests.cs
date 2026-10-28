using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <c>db_query</c> against the real lab Postgres behind the <c>desktop-linux</c> context. Each test brings the stack up and
/// tears it down with wipe, so only lab-labelled containers are touched.
/// </summary>
public sealed class LabDbDockerTests
{
    private const string LinuxContext = "desktop-linux";

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task Query_Insert_IsReadOnly()
    {
        await using var lab = await DockerLab.StartAsync();

        // The write needs a table to name; CREATE is refused by the same read-only check and is the write we can make here.
        Result<DbQueryResult> result = await lab.Db.QueryAsync("nodes", "create table lab_probe (id int)", new Dictionary<string, JsonElement>(), 10, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.ReadOnly]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task Query_ParameterValue_StaysInert()
    {
        await using var lab = await DockerLab.StartAsync();
        var parameters = new Dictionary<string, JsonElement> { ["v"] = JsonSerializer.SerializeToElement("'; drop table x; --") };

        Result<DbQueryResult> result = await lab.Db.QueryAsync("nodes", "select @v::text as v", parameters, 10, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1, result.Value.RowCount);
        Assert.Equal("'; drop table x; --", result.Value.Rows[0]["v"].GetString());
    }

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task Query_NodesProxyDisabled_StillAnswers()
    {
        await using var lab = await DockerLab.StartAsync();
        await lab.DisableProxyAsync("nodes-postgres");

        Result<DbQueryResult> result = await lab.Db.QueryAsync("nodes", "select 1 as one", new Dictionary<string, JsonElement>(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1, result.Value.RowCount);
    }

    /// <summary>
    /// A stack brought up for one test; <see cref="DisposeAsync"/> tears it down with wipe.
    /// </summary>
    private sealed class DockerLab : IAsyncDisposable
    {
        private readonly string _home;
        private readonly SecretVault _vault;
        private readonly ToxiproxyClient _toxi;
        private readonly LabStack _stack;

        private DockerLab(string home, SecretVault vault, ToxiproxyClient toxi, LabStack stack, LabDb db)
        {
            _home = home;
            _vault = vault;
            _toxi = toxi;
            _stack = stack;
            Db = db;
        }

        public LabDb Db { get; }

        public static async Task<DockerLab> StartAsync()
        {
            string repoRoot = FindRepoRoot();
            string home = Path.Combine(Path.GetTempPath(), "meridian-lab-db-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(home);
            var paths = LabPaths.FromRoot(home, repoRoot);
            var docker = new DockerCli(new DockerOptions(LinuxContext, null));
            var vault = new SecretVault();
            var toxi = new ToxiproxyClient();
            var refs = new LabCredentialRefs();
            var targets = new LabTargetRegistry(paths, docker, TimeProvider.System);
            var stack = new LabStack(docker, targets, toxi, vault, refs, paths, new PathValidator(NullLogger<PathValidator>.Instance), TimeProvider.System);

            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, CancellationToken.None);
            if (up.IsFailure)
            {
                await DisposeCoreAsync(home, vault, toxi, stack);
                throw new InvalidOperationException("The lab stack did not come up: " + up.Error);
            }

            return new DockerLab(home, vault, toxi, stack, new LabDb(new LabConnectionStrings(vault, refs)));
        }

        /// <summary>
        /// Disables one Toxiproxy proxy through the loopback API port. Re-enabled by the teardown's wipe.
        /// </summary>
        public async Task DisableProxyAsync(string proxy)
        {
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{LabPorts.ToxiproxyApi}/"),
            };
            using var content = new StringContent("{\"enabled\":false}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync("proxies/" + proxy, content, CancellationToken.None);
            Assert.True(response.IsSuccessStatusCode, "Toxiproxy refused to disable " + proxy);
        }

        public async ValueTask DisposeAsync()
        {
            _ = await _stack.DownAsync(wipe: true, CancellationToken.None);
            await DisposeCoreAsync(_home, _vault, _toxi, _stack);
        }

        private static Task DisposeCoreAsync(string home, SecretVault vault, ToxiproxyClient toxi, LabStack stack)
        {
            stack.Dispose();
            toxi.Dispose();
            vault.Dispose();
            Directory.Delete(home, recursive: true);
            return Task.CompletedTask;
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
}
