using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Service;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>The keyless decoy agent CA (D-S219): it matches the agent's CA rule, has no key, and its manifest entry comes first.</summary>
public class DecoyCaTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-decoy-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly RecordingActions _actions = new();

    public DecoyCaTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.RunDir);
        _manifest = new CleanupManifest(_paths.Manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [SkippableFact]
    public void Plant_Decoy_MatchesAgentCaRuleAndHasNoPrivateKey()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var result = DecoyCa.Plant("run-1", _manifest, _actions);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_actions.Added);
        Assert.Equal("Root", added.StoreName);
        Assert.False(added.PrivateKeyAtAdd);
        Assert.False(added.Certificate.HasPrivateKey);
        Assert.Equal(result.Value, added.Certificate.Thumbprint);
        Assert.True(AgentCertificateMatcher.IsAgentCa(added.Certificate));
    }

    [SkippableFact]
    public void Plant_ManifestCertificateEntry_PrecedesTheAdd()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var result = DecoyCa.Plant("run-1", _manifest, _actions);

        Assert.True(result.IsSuccess);
        var entry = Assert.IsType<CertificateEntry>(Assert.Single(CleanupManifest.Load(_paths.Manifest).Value));
        Assert.Equal(("LocalMachine", "Root", result.Value), (entry.StoreLocation, entry.StoreName, entry.ThumbprintSha1));
        Assert.Contains($"AddCertificate:LocalMachine\\Root\\{result.Value}", _actions.Calls);
    }
}
