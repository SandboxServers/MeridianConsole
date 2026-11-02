using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
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
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Stack;

public sealed class LabStackTests : IDisposable
{
    private readonly string _root;
    private readonly FakeDocker _docker = new();
    private readonly SecretVault _vault = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public LabStackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-lab-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _vault.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp root is disposable.
        }
    }

    [Fact]
    public async Task DownAsync_NoRegisteredTargets_IssuesNoStopOrRemoveCalls()
    {
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.DownAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_docker.Calls, c => c.Contains("stop") || (c.Contains("rm") && !c.Contains("network")));
    }

    [Fact]
    public async Task UpAsync_FakeDocker_CreatesInfrastructureOnLoopbackOnly()
    {
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.NotEmpty(_docker.Calls);
        var publish = new Regex(@"^(?<bind>[^:]+):\d+:\d+$");
        foreach (string arg in _docker.Calls.SelectMany(c => c).Where(a => publish.IsMatch(a)))
        {
            Assert.StartsWith("127.0.0.1:", arg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UpAsync_FakeDocker_CreatesAndRegistersEveryStackProxy()
    {
        // D-ML150: stack_up creates every C10 proxy except L41's agent-ingress-tls, so p:<name> exists for faults from the start.
        using LabStack stack = CreateStack(out LabTargetRegistry registry);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        HashSet<string> proxyTargets = registry.All
            .Where(target => target.Kind == LabTargetKind.Proxy)
            .Select(target => target.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ProxyDefinition proxy in ProxyCatalog.StackProxies)
        {
            Assert.Contains($"p:{proxy.Name}", proxyTargets);
        }

        Assert.Contains("p:identity-postgres", proxyTargets);
        Assert.Contains("p:notifications-rabbitmq", proxyTargets);
        Assert.Contains("p:gateway-identity", proxyTargets);
        Assert.DoesNotContain("p:agent-ingress-tls", proxyTargets);
    }

    [Fact]
    public async Task UpAsync_FakeDocker_NoCallCarriesAPasswordValue()
    {
        using LabStack stack = CreateStack(out _);

        _ = await stack.UpAsync(wipe: false, CancellationToken.None);

        foreach (IReadOnlyList<string> call in _docker.Calls)
        {
            Assert.DoesNotContain(call, a => a.Contains("password=", StringComparison.OrdinalIgnoreCase));
            string joined = string.Join(' ', call);
            Assert.DoesNotMatch(@"--requirepass\s+[^\\$]", joined);
            Assert.DoesNotMatch(@"password=[^\s;,]", joined);
        }
    }

    [Fact]
    public async Task UpAsync_ExistingContainerWithoutLabel_IsNotStarted()
    {
        // A container that only matches the name prefix (no meridian-lab=1 label) is foreign: it must not be started or registered.
        _docker.ContainerInspectOutput = "abc123 ";
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not lab-owned", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_docker.Calls, c => c.Contains("start"));
    }

    [Fact]
    public async Task UpAsync_SweepsOldEnvFiles_AndKeepsFreshOnes()
    {
        // An env file older than ten minutes is a crash leftover and is swept; a fresh one may belong to a svc_start in flight.
        string state = LabPaths.FromRoot(_root, repoRoot: FindRepoRoot()).State;
        _ = Directory.CreateDirectory(state);
        string old = Path.Combine(state, "env-old");
        string fresh = Path.Combine(state, "env-fresh");
        await File.WriteAllTextAsync(old, "A=1", CancellationToken.None);
        await File.WriteAllTextAsync(fresh, "B=2", CancellationToken.None);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        using LabStack stack = CreateStack(out _);

        _ = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task UpAsync_WipeWithOrphanedLabelledContainer_RemovesOnlyTheLabPrefixedName()
    {
        // R-N1: a labelled container the registry does not know is removed by wipe, but only under the lab prefix.
        _docker.PsOutput = "meridian-lab-postgres\npostgres";
        using LabStack stack = CreateStack(out _);

        _ = await stack.UpAsync(wipe: true, CancellationToken.None);

        Assert.Contains(_docker.Calls, c => string.Join(' ', c) == "rm -f -v meridian-lab-postgres");
        Assert.DoesNotContain(_docker.Calls, c => c.Contains("rm") && c.Contains("postgres") && !c.Contains("meridian-lab-postgres"));
    }

    [Fact]
    public async Task UpAsync_WithoutWipeOnEmptyRegistry_RefusesWhileLabelledContainersExist()
    {
        // R-N1: without wipe the daemon does not start a stack on top of containers it does not know.
        _docker.PsOutput = "meridian-lab-postgres\npostgres";
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("labelled lab containers exist that this daemon does not know; run stack_up with wipe", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_docker.Calls, c => c.Contains("run") || c.Contains("start"));
    }

    [Fact]
    public async Task UpAsync_WithoutWipeAndNoLabelledContainers_IsNotRefused()
    {
        // The orphan check must not refuse a clean host: an empty listing lets the stack come up.
        _docker.PsOutput = string.Empty;
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
    }

    [Fact]
    public async Task UpAsync_PostgresReadiness_ProbesOverTcp()
    {
        // F112: the readiness probe must reach Postgres over TCP, not the Unix socket the init-time server listens on.
        using LabStack stack = CreateStack(out _);

        _ = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.Contains(_docker.Calls, c => c.Contains("pg_isready") && c.Contains("-h") && c.Contains("127.0.0.1"));
    }

    [Fact]
    public async Task UpAsync_CreateDatabaseFails_ErrorNamesThePsqlError()
    {
        // D-ML124: a failed database statement keeps psql's first ERROR or FATAL line in the error.
        _docker.Answer = args => args.Any(a => a.Contains("CREATE DATABASE meridian_lab_nodes OWNER lab_owner", StringComparison.Ordinal))
            ? new DockerOutput(1, string.Empty, "psql: error: connection to server on socket \"/var/run/postgresql/.s.PGSQL.5432\" failed: FATAL:  the database system is shutting down")
            : null;
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("could not create meridian_lab_nodes", result.Error, StringComparison.Ordinal);
        Assert.Contains("the database system is shutting down", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpAsync_CreateRoleFails_ErrorCarriesNoStderr()
    {
        // D-ML124: role statements never carry stderr, because psql can echo the expanded password.
        _docker.Answer = args => args.Any(a => a.Contains("CREATE ROLE", StringComparison.Ordinal))
            ? new DockerOutput(1, string.Empty, "ERROR:  syntax error at or near \"pw-sentinel\"\nLINE 1: CREATE ROLE lab_owner LOGIN PASSWORD 'pw-sentinel'")
            : null;
        using LabStack stack = CreateStack(out _);

        Result<StackStatusDto> result = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("could not create role lab_owner", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("pw-sentinel", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("LINE 1", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("syntax error", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEnvFile_SecondSecretMissing_DeletesThePartialFile()
    {
        // The first secret is written, the second ref is not in the vault: the failure must leave no env-* file in State (N1).
        using LabStack stack = CreateStack(out _);
        string state = LabPaths.FromRoot(_root, repoRoot: FindRepoRoot()).State;
        string present = _vault.Add(SecretKind.EnrollmentToken, "first-secret-value".AsSpan());

        // EnvEntry is a private nested record; it is built by reflection because the writer is private.
        Type entryType = typeof(LabStack).GetNestedType("EnvEntry", BindingFlags.NonPublic) ?? throw new InvalidOperationException("EnvEntry not found");
        var entries = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
        entries.Add(Activator.CreateInstance(entryType, "FIRST", present, null));
        entries.Add(Activator.CreateInstance(entryType, "SECOND", "ref-not-in-vault", null));
        MethodInfo write = typeof(LabStack).GetMethod("WriteEnvFile", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("WriteEnvFile not found");

        Result<string> outcome = Assert.IsType<Result<string>>(write.Invoke(stack, [entries]));

        Assert.True(outcome.IsFailure);
        Assert.Empty(Directory.GetFiles(state, "env-*"));
    }

    [Fact]
    public async Task UpAsync_WritesExchangePublicKey_MatchingTheVaultPrivateKey()
    {
        using LabStack stack = CreateStack(out _, out LabCredentialRefs refs);

        Result<StackStatusDto> up = await stack.UpAsync(wipe: false, CancellationToken.None);

        Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);
        string file = Path.Combine(LabPaths.FromRoot(_root, repoRoot: FindRepoRoot()).State, "identity", "exchange-public.pem");
        string publicPem = await File.ReadAllTextAsync(file, CancellationToken.None);
        Assert.Contains("BEGIN PUBLIC KEY", publicPem, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", publicPem, StringComparison.Ordinal);
        Result<string> privatePem = new LabConnectionStrings(_vault, refs).ExchangePrivateKeyPem();
        Assert.True(privatePem.IsSuccess, privatePem.IsFailure ? privatePem.Error : string.Empty);
        using var priv = ECDsa.Create();
        priv.ImportFromPem(privatePem.Value!);
        using var pub = ECDsa.Create();
        pub.ImportFromPem(publicPem);
        Assert.Equal(priv.ExportSubjectPublicKeyInfo(), pub.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public async Task StartBetterAuth_FakeDocker_SecretsOnlyInTheEnvFile_AndFileDeleted()
    {
        using LabStack stack = CreateStack(out LabTargetRegistry registry, out LabCredentialRefs refs);
        Result<StackStatusDto> up = await stack.UpAsync(wipe: false, CancellationToken.None);
        Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);

        // Set only after UpAsync: its own container inspects must keep the defaults.
        string? envPath = null;
        string envText = string.Empty;
        IReadOnlyList<string>? runArgs = null;
        _docker.Answer = args =>
        {
            if (args.Contains("run") && args.Any(a => a.Contains("meridian-lab-betterauth", StringComparison.Ordinal)))
            {
                runArgs = args.ToArray();
                int at = args.ToList().IndexOf("--env-file");
                if (at >= 0)
                {
                    envPath = args[at + 1];
                    envText = File.ReadAllText(envPath);
                }

                return new DockerOutput(125, string.Empty, "refused by the test");
            }

            return args.Contains("container") && args.Contains("inspect") && args.Any(a => a.Contains(".Config.Labels", StringComparison.Ordinal))
                ? new DockerOutput(1, string.Empty, "No such container")
                : null;
        };

        LabPaths paths = LabPaths.FromRoot(_root, repoRoot: FindRepoRoot());
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var redactor = new Redactor(_vault);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        using var supervisor = new LabProcessSupervisor(
            new LogPathGuard(paths, validator),
            redactor,
            leases,
            _time,
            new NullRawLineObserver(),
            new NoFaults());
        var logs = new ContainerLogFollower(supervisor, new ConfigurationBuilder().Build(), _time, paths);
        var services = new LabServices(
            [new BetterAuthLabAdapter()],
            _docker,
            registry,
            stack,
            new LabConnectionStrings(_vault, refs),
            refs,
            _vault,
            logs,
            new ConfigOverlay(paths, validator),
            validator,
            paths,
            _time);

        Result<LabServiceStartDto> result = await services.StartAsync("betterauth", "a9c623a1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("could not start", result.Error, StringComparison.Ordinal);
        Assert.NotNull(envPath);
        Assert.False(File.Exists(envPath), "the env file must be deleted after docker run");
        Assert.Contains("EXCHANGE_TOKEN_PRIVATE_KEY=-----BEGIN PRIVATE KEY-----", envText, StringComparison.Ordinal);
        Assert.Contains("SECRETS_SERVICE_URL=http://127.0.0.1:8099", envText, StringComparison.Ordinal);
        Assert.NotNull(runArgs);
        Assert.Contains("--env-file", runArgs);
        string joined = string.Join(' ', runArgs);
        string joinedNoNewlines = joined.Replace("\n", string.Empty, StringComparison.Ordinal);
        foreach ((string reference, int length) in _vault.Snapshot())
        {
            if (length < 8)
            {
                continue;
            }

            Result<string> value = _vault.Use(reference, v => v.ToString());
            Assert.True(value.IsSuccess);
            Assert.DoesNotContain(value.Value!, joined, StringComparison.Ordinal);
            Assert.DoesNotContain(value.Value!, joinedNoNewlines, StringComparison.Ordinal);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The LabStack returned to the caller owns the ToxiproxyClient and its fake handler; the test disposes the stack.")]
    private LabStack CreateStack(out LabTargetRegistry registry) => CreateStack(out registry, out _);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The LabStack returned to the caller owns the ToxiproxyClient and its fake handler; the test disposes the stack.")]
    private LabStack CreateStack(out LabTargetRegistry registry, out LabCredentialRefs refs)
    {
        refs = new LabCredentialRefs();
        var paths = LabPaths.FromRoot(_root, repoRoot: FindRepoRoot());
        registry = new LabTargetRegistry(paths, _docker, _time);
        var toxi = new ToxiproxyClient(new FakeToxiproxyHandler());
        return new LabStack(
            _docker,
            registry,
            toxi,
            _vault,
            refs,
            paths,
            new PathValidator(NullLogger<PathValidator>.Instance),
            _time);
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

    /// <summary>
    /// Records the argument lists of every docker call and answers success with empty output.
    /// </summary>
    private sealed class FakeDocker : IDockerCli
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>The stdout of <c>container inspect</c>: the id and the lab label value. The default is a lab-owned container.</summary>
        public string ContainerInspectOutput { get; set; } = "fake-id 1";

        /// <summary>The stdout of <c>ps</c>: one container name per line, as listed by the labelled-container query.</summary>
        public string PsOutput { get; set; } = string.Empty;

        /// <summary>Optional override, consulted first: a non-null result answers the call, null falls through to the defaults.</summary>
        public Func<IReadOnlyList<string>, DockerOutput?>? Answer { get; set; }

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            DockerOutput? overridden = Answer?.Invoke(args);
            if (overridden is not null)
            {
                return Task.FromResult(Result<DockerOutput>.Success(overridden));
            }

            // redis-cli ping must answer PONG for the readiness probe to pass; every other probe exits 0.
            string stdout = args.Contains("ping")
                ? "PONG"
                : args.Contains("ps") ? PsOutput
                : args.Contains("container") && args.Contains("inspect") ? ContainerInspectOutput : string.Empty;
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(0, stdout, string.Empty)));
        }
    }

    private sealed class FakeToxiproxyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpStatusCode status = request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
        }
    }
}
