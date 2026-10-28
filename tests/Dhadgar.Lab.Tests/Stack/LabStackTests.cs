using System.Net;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The LabStack returned to the caller owns the ToxiproxyClient and its fake handler; the test disposes the stack.")]
    private LabStack CreateStack(out LabTargetRegistry registry)
    {
        var paths = LabPaths.FromRoot(_root, repoRoot: FindRepoRoot());
        registry = new LabTargetRegistry(paths, _docker, _time);
        var toxi = new ToxiproxyClient(new FakeToxiproxyHandler());
        return new LabStack(
            _docker,
            registry,
            toxi,
            _vault,
            new LabCredentialRefs(),
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

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            // redis-cli ping must answer PONG for the readiness probe to pass; every other probe exits 0.
            string stdout = args.Contains("ping")
                ? "PONG"
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
