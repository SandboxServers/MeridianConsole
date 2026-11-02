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
using Npgsql;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <c>db_query</c> against the real lab Postgres behind the <c>desktop-linux</c> context. Each test brings the stack up and
/// tears it down with wipe, so only lab-labelled containers are touched.
/// </summary>
[Collection(LabDockerGate.Name)]
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
        await DockerLab.DisableProxyAsync("nodes-postgres");

        Result<DbQueryResult> result = await lab.Db.QueryAsync("nodes", "select 1 as one", new Dictionary<string, JsonElement>(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1, result.Value.RowCount);
    }

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task DbQuery_TokenColumns_Masked()
    {
        const string secret = "lab-l35m-canary-token-7f3a9c";
        await using var lab = await DockerLab.StartAsync();
        await DockerLab.CreateFixturesAsync(lab.Connections, secret);

        // The base column, an alias, a join and an expression: each carries the secret, none may return it.
        string[] betterAuthQueries =
        {
            "SELECT token FROM session",
            "SELECT token AS t FROM session",
            "SELECT s.token FROM session s JOIN session t ON t.id = s.id",
            "SELECT token || '' AS x FROM session",
        };
        foreach (string sql in betterAuthQueries)
        {
            Result<DbQueryResult> result = await lab.Db.QueryAsync("betterauth", sql, new Dictionary<string, JsonElement>(), 10, CancellationToken.None);

            AssertMaskedWithoutSecret(result, secret, sql);
        }

        Result<DbQueryResult> identity = await lab.Db.QueryAsync("identity", "SELECT \"TokenHash\" FROM refresh_tokens", new Dictionary<string, JsonElement>(), 10, CancellationToken.None);

        AssertMaskedWithoutSecret(identity, secret, "identity refresh_tokens");
    }

    private static void AssertMaskedWithoutSecret(Result<DbQueryResult> result, string secret, string label)
    {
        Assert.True(result.IsSuccess, label + ": " + (result.IsFailure ? result.Error : string.Empty));
        Assert.Equal(1, result.Value.RowCount);

        foreach (Dictionary<string, JsonElement> row in result.Value.Rows)
        {
            foreach (KeyValuePair<string, JsonElement> cell in row)
            {
                Assert.DoesNotContain(secret, cell.Value.ToString(), StringComparison.Ordinal);
            }
        }

        Assert.Equal(ColumnMask.Masked, result.Value.Rows[0].Values.First().GetString());
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

        private DockerLab(string home, SecretVault vault, ToxiproxyClient toxi, LabStack stack, LabConnectionStrings connections, LabDb db)
        {
            _home = home;
            _vault = vault;
            _toxi = toxi;
            _stack = stack;
            Connections = connections;
            Db = db;
        }

        public LabDb Db { get; }

        /// <summary>The connection-string builder, so a test can write fixture tables as the service owner.</summary>
        public LabConnectionStrings Connections { get; }

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

            var connections = new LabConnectionStrings(vault, refs);
            return new DockerLab(home, vault, toxi, stack, connections, new LabDb(connections));
        }

        /// <summary>
        /// Disables one Toxiproxy proxy through the loopback API port. Re-enabled by the teardown's wipe.
        /// </summary>
        public static async Task DisableProxyAsync(string proxy)
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{LabPorts.ToxiproxyApi}/"),
            };
            using var content = new StringContent("{\"enabled\":false}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync("proxies/" + proxy, content, CancellationToken.None);
            Assert.True(response.IsSuccessStatusCode, "Toxiproxy refused to disable " + proxy);
        }

        /// <summary>
        /// Writes the fixture tables as each database's owner, with one row holding <paramref name="secret"/>, and grants the reader SELECT.
        /// The stack wipe at teardown discards them.
        /// </summary>
        public static async Task CreateFixturesAsync(LabConnectionStrings connections, string secret)
        {
            await RunAsOwnerAsync(connections, "betterauth", "CREATE TABLE session (id integer PRIMARY KEY, token text NOT NULL)");
            await RunAsOwnerAsync(connections, "betterauth", "INSERT INTO session (id, token) VALUES (1, @secret)", secret);
            await RunAsOwnerAsync(connections, "betterauth", "GRANT SELECT ON session TO " + LabCredentialRefs.LabReader);

            await RunAsOwnerAsync(connections, "identity", "CREATE TABLE refresh_tokens (\"Id\" integer PRIMARY KEY, \"TokenHash\" character varying(128) NOT NULL)");
            await RunAsOwnerAsync(connections, "identity", "INSERT INTO refresh_tokens (\"Id\", \"TokenHash\") VALUES (1, @secret)", secret);
            await RunAsOwnerAsync(connections, "identity", "GRANT SELECT ON refresh_tokens TO " + LabCredentialRefs.LabReader);
        }

        private static async Task RunAsOwnerAsync(LabConnectionStrings connections, string database, string sql, string? secret = null)
        {
            Result<string> connection = connections.OwnerDirect(database);
            Assert.True(connection.IsSuccess, "The owner connection to " + database + " was refused: " + (connection.IsFailure ? connection.Error : string.Empty));

            await using var conn = new NpgsqlConnection(connection.Value);
            await conn.OpenAsync(CancellationToken.None);

#pragma warning disable CA2100 // Fixture SQL is a constant written in this file; the secret is a bound parameter.
            await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
            if (secret is not null)
            {
                _ = cmd.Parameters.AddWithValue("secret", secret);
            }

            _ = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
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
