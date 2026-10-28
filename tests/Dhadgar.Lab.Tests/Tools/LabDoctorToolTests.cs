using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// lab_doctor: the result is <c>{ok, checks:[{name, status, detail}]}</c>, the Docker check runs on the Linux engine through
/// <see cref="IDockerCli"/>, and each verdict (ports, token, repo root, docker-users) follows the L12 rules.
/// </summary>
public sealed class LabDoctorToolTests
{
    private static readonly LabCallContext Context = new("call-test", null, "test");
    private static readonly string[] Statuses = ["ok", "warn", "fail", "info"];

    [Fact]
    public async Task Docker_UsesLinuxEngineAndVersionArgs()
    {
        using var vault = new SecretVault();
        var docker = new RecordingDocker();
        var tool = CreateTool(vault, docker, LabPaths.FromRoot(Path.GetTempPath()), new DaemonTokenInfo(32));

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEmpty(docker.Calls);
        Assert.Equal(DockerEngine.Linux, docker.Calls[0].Engine);
        Assert.Equal(new[] { "version", "--format", "{{.Server.Os}}" }, docker.Calls[0].Args);
    }

    [Fact]
    public async Task Result_Serialises_OkAndStatusPerCheck()
    {
        using var vault = new SecretVault();
        var tool = CreateTool(vault, new RecordingDocker(), LabPaths.FromRoot(Path.GetTempPath()), new DaemonTokenInfo(32));

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.GetProperty("ok").ValueKind is JsonValueKind.True or JsonValueKind.False);
        foreach (JsonElement check in result.Value.GetProperty("checks").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, check.GetProperty("name").ValueKind);
            Assert.Contains(check.GetProperty("status").GetString(), Statuses);
            Assert.Equal(JsonValueKind.String, check.GetProperty("detail").ValueKind);
        }
    }

    [Fact]
    public void Ok_IsFalse_WhenAnyCheckFails()
    {
        var failing = new List<DoctorCheck>
        {
            new("os", DoctorStatus.Info, "Win32NT"),
            new("token", DoctorStatus.Fail, "16 bytes (min 32)"),
        };
        var degraded = new List<DoctorCheck>
        {
            new("os", DoctorStatus.Info, "Win32NT"),
            new("docker-users", DoctorStatus.Warn, "not a member; the engine answered anyway"),
        };

        Assert.False(LabDoctorResult.From(failing).Ok);
        Assert.True(LabDoctorResult.From(degraded).Ok);
    }

    [Fact]
    public async Task Output_HasNoNotCheckedText()
    {
        using var vault = new SecretVault();
        var tool = CreateTool(vault, new RecordingDocker(), LabPaths.FromRoot(Path.GetTempPath()), new DaemonTokenInfo(32));

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("not checked", result.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ports_HeldByForeignListener_Fails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        DoctorCheck check = DoctorPorts.EvaluatePorts([port], DoctorPorts.Probe, new HashSet<int>());

        Assert.Equal(DoctorStatus.Fail, check.Status);
        Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), check.Detail);
    }

    [Fact]
    public void Ports_HeldByLabContainer_IsOk()
    {
        DoctorCheck check = DoctorPorts.EvaluatePorts([15432], _ => PortState.InUse, new HashSet<int> { 15432 });

        Assert.Equal(DoctorStatus.Ok, check.Status);
    }

    [Fact]
    public void Ports_DockerUnreachable_Warns()
    {
        DoctorCheck check = DoctorPorts.EvaluatePorts([15432], _ => PortState.InUse, labOwned: null);

        Assert.Equal(DoctorStatus.Warn, check.Status);
    }

    [Fact]
    public void Token_ReportsLengthNeverValue()
    {
        DoctorCheck ok = LabDoctorTool.TokenCheck(new DaemonTokenInfo(32));
        DoctorCheck tooShort = LabDoctorTool.TokenCheck(new DaemonTokenInfo(16));

        Assert.Equal(DoctorStatus.Ok, ok.Status);
        Assert.Equal("32 bytes (min 32)", ok.Detail);
        Assert.Equal(DoctorStatus.Fail, tooShort.Status);
        Assert.Equal("16 bytes (min 32)", tooShort.Detail);
    }

    [Fact]
    public void RepoRoot_Unset_Fails()
    {
        DoctorCheck check = LabDoctorTool.RepoRootCheck(string.Empty);

        Assert.Equal(DoctorStatus.Fail, check.Status);
        Assert.Equal("Lab:RepoRoot is not set", check.Detail);
    }

    [Fact]
    public void RepoRoot_Missing_Fails()
    {
        string missing = Path.Combine(Path.GetTempPath(), "doctor-missing-" + Guid.NewGuid().ToString("N"));

        DoctorCheck check = LabDoctorTool.RepoRootCheck(missing);

        Assert.Equal(DoctorStatus.Fail, check.Status);
        Assert.DoesNotContain(missing, check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RepoRoot_Checkout_IsOk()
    {
        string root = Path.Combine(Path.GetTempPath(), "doctor-checkout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Dhadgar.sln"), string.Empty);

            DoctorCheck check = LabDoctorTool.RepoRootCheck(root);

            Assert.Equal(DoctorStatus.Ok, check.Status);
            Assert.Equal("exists", check.Detail);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact]
    public async Task DockerUsers_OnWindows_IsNeverInfo()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "docker-users is a Windows check");
        using var vault = new SecretVault();
        var tool = CreateTool(vault, new RecordingDocker(), LabPaths.FromRoot(Path.GetTempPath()), new DaemonTokenInfo(32));

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        JsonElement check = result.Value.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "docker-users");
        Assert.NotEqual(DoctorStatus.Info, check.GetProperty("status").GetString());
    }

    private static LabDoctorTool CreateTool(SecretVault vault, IDockerCli docker, LabPaths paths, DaemonTokenInfo token) =>
        new(new Redactor(vault), docker, paths, token);

    private sealed class RecordingDocker : IDockerCli
    {
        public List<(DockerEngine Engine, string[] Args)> Calls { get; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add((engine, args.ToArray()));
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(0, "linux\n", string.Empty)));
        }
    }
}
