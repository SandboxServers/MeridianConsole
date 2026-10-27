using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Service;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>The <c>sc.exe</c> wrapper with a fake tool runner: exact arguments, refusals before any call, and <c>qc</c> parsing.</summary>
public class ServiceControlTests : IDisposable
{
    private const string QcSample = """
        [SC] QueryServiceConfig SUCCESS

        SERVICE_NAME: DhadgarAgent
                TYPE               : 10  WIN32_OWN_PROCESS
                START_TYPE         : 3   DEMAND_START
                ERROR_CONTROL      : 1   NORMAL
                BINARY_PATH_NAME   : C:\repo\.uat\agent-windows\run-1\agent\Dhadgar.Agent.Windows.exe
                LOAD_ORDER_GROUP   :
                TAG                : 0
                DISPLAY_NAME       : Meridian Console Agent
                DEPENDENCIES       :
                SERVICE_START_NAME : LocalSystem
        """;

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-service-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly List<(string File, IReadOnlyList<string> Args)> _calls = [];
    private string _output = string.Empty;

    public ServiceControlTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.AgentPublish);
        _manifest = new CleanupManifest(_paths.Manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private ServiceControl CreateControl() =>
        new(_paths, _manifest, Run, TimeSpan.Zero);

    private Task<Dhadgar.Shared.Results.Result<CommandOutput>> Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        _calls.Add((file, args));
        return Task.FromResult(Dhadgar.Shared.Results.Result<CommandOutput>.Success(new CommandOutput(0, _output, string.Empty)));
    }

    [Fact]
    public async Task Create_ValidPath_PassesExactScArgumentList()
    {
        var exe = Path.Combine(_paths.AgentPublish, "Dhadgar.Agent.Windows.exe");

        var result = await CreateControl().Create(exe);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(_calls);
        Assert.Equal(
            ["create", "DhadgarAgent", "binPath=", exe, "start=", "demand", "obj=", "LocalSystem"],
            call.Args);
        Assert.Equal(
            [new ServiceEntry(AgentWindowsFacts.ServiceName)],
            CleanupManifest.Load(_paths.Manifest).Value.ToArray());
    }

    [Fact]
    public async Task Create_PathWithSpace_RefusedBeforeAnyCall()
    {
        var exe = Path.Combine(_paths.AgentPublish, "my agent", "Dhadgar.Agent.Windows.exe");

        var result = await CreateControl().Create(exe);

        Assert.True(result.IsFailure);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Create_PathOutsidePublishFolder_RefusedBeforeAnyCall()
    {
        var exe = Path.Combine(_paths.RunDir, "Dhadgar.Agent.Windows.exe");

        var result = await CreateControl().Create(exe);

        Assert.True(result.IsFailure);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task QueryConfig_CapturedSample_ParsesStartTypeAccountAndBinaryPath()
    {
        _output = QcSample;

        var config = await CreateControl().QueryConfig();

        Assert.True(config.IsSuccess);
        Assert.Equal("DEMAND_START", config.Value.StartType);
        Assert.Equal("LocalSystem", config.Value.Account);
        Assert.EndsWith("Dhadgar.Agent.Windows.exe", config.Value.BinaryPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_StoppedOutput_ReturnsStopped()
    {
        _output = "SERVICE_NAME: DhadgarAgent\r\n        STATE              : 1  STOPPED\r\n";

        var state = await CreateControl().Query();

        Assert.Equal("STOPPED", state.Value);
    }
}
