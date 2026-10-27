using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class RootSnapshotTests
{
    [Fact]
    public void Diff_RemovedThumbprint_ReportsRootRemoved()
    {
        var before = new RootSnapshot(["AAA", "BBB"]);
        var after = new RootSnapshot(["AAA"]);

        var diff = RootSnapshot.Diff(before, after, _ => null);

        Assert.Equal(["BBB"], diff.RootRemoved);
        Assert.Empty(diff.RootAdded);
    }

    [Fact]
    public void Diff_AddedUatCertificate_ReportsRootAdded()
    {
        var before = new RootSnapshot(["AAA"]);
        var after = new RootSnapshot(["AAA", "CCC"]);

        var diff = RootSnapshot.Diff(before, after, _ => SelfSigned("CN=decoy, O=Meridian Console UAT"));

        Assert.Equal(["CCC"], diff.RootAdded);
        Assert.Empty(diff.Warnings);
    }

    [Fact]
    public void Diff_AddedAgentCa_ReportsRootAdded()
    {
        var before = new RootSnapshot(["AAA"]);
        var after = new RootSnapshot(["AAA", "EEE"]);

        var diff = RootSnapshot.Diff(before, after, _ => SelfSigned($"CN={AgentCertificateProfile.CaCommonName}"));

        Assert.Equal(["EEE"], diff.RootAdded);
        Assert.Empty(diff.Warnings);
    }

    [Fact]
    public void Diff_OtherAddition_IsWarningOnly()
    {
        var before = new RootSnapshot(["AAA"]);
        var after = new RootSnapshot(["AAA", "DDD"]);

        var diff = RootSnapshot.Diff(before, after, _ => SelfSigned("CN=Windows Update Root"));

        Assert.Empty(diff.RootAdded);
        Assert.Empty(diff.RootRemoved);
        Assert.Single(diff.Warnings);
    }

    private static X509Certificate2 SelfSigned(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
