using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Tools.Cli;
using Xunit;

namespace Dhadgar.Lab.Tests.Cli;

/// <summary>
/// The non-Docker half of the CLI-to-Gateway seam (D-ML163 d): the member config URLs plus the CLI's Refit path literals,
/// read from the CLI sources (no ProjectReference to the CLI), run through <see cref="GatewayRouteTable"/>. The live half is
/// <c>cli.toml</c>.
/// </summary>
public sealed class CliGatewaySeamTests
{
    private static string CliFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Dhadgar.Cli", .. parts]));

    [Fact]
    public void CliRefitPaths_ArePinned()
    {
        Assert.Contains("[Post(\"/connect/token\")]", CliFile("Infrastructure", "Clients", "IIdentityApi.cs"), StringComparison.Ordinal);
        Assert.Contains("[Get(\"/organizations/{orgId}/nodes\")]", CliFile("Infrastructure", "Clients", "INodesApi.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void MemberConfig_LoginAndNodesList_RouteThroughTheGateway()
    {
        Assert.Equal("identity", GatewayRouteTable.ClusterFor(new Uri(CliMemberInstall.MemberIdentityUrl).AbsolutePath + "/connect/token"));
        Assert.Equal("nodes", GatewayRouteTable.ClusterFor(new Uri(CliMemberInstall.MemberNodesUrl).AbsolutePath + "/organizations/o1/nodes"));

        // The CLI's own defaults match no Gateway route (fact 5). When D-ML166 fixes the CLI this goes red: drop the lab override then.
        Assert.Null(GatewayRouteTable.ClusterFor("/identity/connect/token"));
        Assert.Null(GatewayRouteTable.ClusterFor("/nodes/organizations/o1/nodes"));
    }

    [Fact]
    public void MemberGatewayHost_IsAllowedByTheLabGateway()
    {
        var ctx = new LabServiceContext(
            "a9c623a1",
            new Dictionary<string, string>(),
            "/lab-state/nodes-ca",
            "lab_user",
            "lab-password-value");
        var env = new GatewayAdapter().Environment(ctx);

        Assert.True(env.IsSuccess, env.Error);
        string[] hosts = env.Value!["AllowedHosts"].Split(';');
        Assert.Contains(new Uri(CliMemberInstall.MemberGatewayUrl).Host, hosts);
    }
}
