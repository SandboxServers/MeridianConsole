using System.Text.Json.Nodes;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// U6 lowers the renewal threshold to 90 days so the agent renews at start (<see cref="RenewalLegs.RenewalThresholdDays"/>).
/// The key must be removed again after U6, whether U6 passes or fails, so the published config is left as it was built.
/// </summary>
public sealed class RenewalThresholdOverrideTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-threshold-" + Guid.NewGuid().ToString("N"));
    private readonly RenewalWorld _world;

    public RenewalThresholdOverrideTests()
    {
        _world = new RenewalWorld(new RunPaths(_repo, "run-1"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _world.Dispose();
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenU6Passes_RemovesTheThresholdKeyAndKeepsSiblings()
    {
        var result = await _world.Legs().RunAsync(new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoThresholdKey();
    }

    [Fact]
    public async Task RunAsync_WhenU6Fails_StillRemovesTheThresholdKey()
    {
        _world.KeyStillExists = true;

        var result = await _world.Legs().RunAsync(new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNoThresholdKey();
    }

    private void AssertNoThresholdKey()
    {
        var root = JsonNode.Parse(File.ReadAllText(_world.AppSettingsPath))!;
        var security = root["Agent"]!["Security"]!.AsObject();
        Assert.False(security.ContainsKey("CertificateRenewalThresholdDays"));
        Assert.Equal(1, security["Other"]!.GetValue<int>());
    }
}
