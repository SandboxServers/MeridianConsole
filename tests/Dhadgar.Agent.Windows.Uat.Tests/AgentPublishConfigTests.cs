using System.Text.Json.Nodes;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public sealed class AgentPublishConfigTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-repo-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly string _appsettings;

    public AgentPublishConfigTests()
    {
        _paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(_paths.AgentPublish);
        _appsettings = Path.Combine(_paths.AgentPublish, "appsettings.json");
        File.WriteAllText(_appsettings, """
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "Agent": {
                "ControlPlane": { "Endpoint": "https://old", "KeepMe": "yes" },
                "Security": { "CertificateRenewalThresholdDays": 30, "Other": 1 }
              }
            }
            """);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private AgentConfig Config() => new(
        new Uri("https://localhost:5443/some/path"),
        10,
        10,
        _paths.AgentState,
        Path.Combine(_paths.AgentData, "Servers"),
        Path.Combine(_paths.AgentData, "Temp"));

    private JsonNode? Root() => JsonNode.Parse(File.ReadAllText(_appsettings));

    [Fact]
    public void Apply_SetsEveryContractKey()
    {
        var result = new AgentPublishConfig(_paths).Apply(_appsettings, Config());

        Assert.True(result.IsSuccess, result.Error);
        var root = Root()!;
        Assert.Equal("https://localhost:5443", root["Agent"]!["ControlPlane"]!["Endpoint"]!.GetValue<string>());
        Assert.Equal(10, root["Agent"]!["ControlPlane"]!["HeartbeatIntervalSeconds"]!.GetValue<int>());
        Assert.Equal(10, root["Agent"]!["ControlPlane"]!["ConnectionCheckIntervalSeconds"]!.GetValue<int>());
        Assert.Equal(_paths.AgentState, root["Agent"]!["StateDirectory"]!.GetValue<string>());
        Assert.Equal(Path.Combine(_paths.AgentData, "Servers"), root["Agent"]!["Process"]!["ServerBasePath"]!.GetValue<string>());
        Assert.Equal(Path.Combine(_paths.AgentData, "Temp"), root["Agent"]!["Files"]!["TempDirectory"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_KeepsUnrelatedKeys()
    {
        var result = new AgentPublishConfig(_paths).Apply(_appsettings, Config());

        Assert.True(result.IsSuccess, result.Error);
        var root = Root()!;
        Assert.Equal("Information", root["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>());
        Assert.Equal("yes", root["Agent"]!["ControlPlane"]!["KeepMe"]!.GetValue<string>());
        Assert.Equal(30, root["Agent"]!["Security"]!["CertificateRenewalThresholdDays"]!.GetValue<int>());
        Assert.Equal(1, root["Agent"]!["Security"]!["Other"]!.GetValue<int>());
    }

    [Fact]
    public void SetRenewalThreshold_Null_RemovesKeyAndKeepsSiblings()
    {
        var result = new AgentPublishConfig(_paths).SetRenewalThreshold(_appsettings, null);

        Assert.True(result.IsSuccess, result.Error);
        var security = Root()!["Agent"]!["Security"]!.AsObject();
        Assert.False(security.ContainsKey("CertificateRenewalThresholdDays"));
        Assert.Equal(1, security["Other"]!.GetValue<int>());
    }

    [Fact]
    public void SetRenewalThreshold_Value_SetsKey()
    {
        var result = new AgentPublishConfig(_paths).SetRenewalThreshold(_appsettings, 90);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(90, Root()!["Agent"]!["Security"]!["CertificateRenewalThresholdDays"]!.GetValue<int>());
    }

    [Fact]
    public void Apply_PathOutsideRunFolder_Fails()
    {
        var config = Config() with { StateDirectory = Path.Combine(_repo, "elsewhere") };

        var result = new AgentPublishConfig(_paths).Apply(_appsettings, config);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("https://example.test:5443/")]
    [InlineData("http://localhost:5443/")]
    [InlineData("https://localhost/")]
    [InlineData("https://user@localhost:5443/")]
    [InlineData("https://127.0.0.1:5443/")]
    public void Apply_EndpointNotLocalhostHttps_Fails(string endpoint)
    {
        var result = new AgentPublishConfig(_paths).Apply(_appsettings, Config() with { Endpoint = new Uri(endpoint) });

        Assert.True(result.IsFailure);
        Assert.StartsWith("[AgentConfig.Endpoint]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_HeartbeatBelowMinimum_Fails()
    {
        var result = new AgentPublishConfig(_paths).Apply(_appsettings, Config() with { HeartbeatIntervalSeconds = 5 });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Apply_MissingFile_FailsWithoutCreatingIt()
    {
        var missing = Path.Combine(_paths.AgentPublish, "missing.json");

        var result = new AgentPublishConfig(_paths).Apply(missing, Config());

        Assert.True(result.IsFailure);
        Assert.False(File.Exists(missing));
    }
}
