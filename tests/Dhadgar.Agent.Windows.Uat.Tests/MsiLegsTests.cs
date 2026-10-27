using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>The U11a and U11b checks over fake machine facts (P23). No test reads the real machine.</summary>
public class MsiLegsTests
{
    private const string Endpoint = "https://localhost:8443/";

    private static Dictionary<string, string?> GoodRegistry() => new(StringComparer.Ordinal)
    {
        ["InstallPath"] = @"C:\Program Files\Meridian Console\Agent",
        ["Version"] = "1.0.0",
        ["DataPath"] = @"C:\ProgramData\Meridian Console\Agent",
        ["ServiceInstalled"] = "1",
        ["ControlPlaneEndpoint"] = Endpoint,
        ["EnrollmentToken"] = null,
    };

    private static MsiInstallFacts GoodInstall(
        string startType = "AUTO_START",
        string account = "LocalSystem",
        string state = "RUNNING",
        int products = 1,
        int exitCode = 0) =>
        new(exitCode, startType, account, state, GoodRegistry(), true, Enumerable.Repeat("{11111111-2222-3333-4444-555555555555}", products).ToList());

    private static MsiUninstallFacts CleanUninstall(
        bool serviceExists = false,
        IReadOnlyList<string>? remaining = null,
        IReadOnlyList<StoreCert>? certificates = null,
        bool stateDirectory = false,
        bool eventSource = false) =>
        new(0, serviceExists, [], false, remaining ?? [], certificates ?? [], false, stateDirectory, eventSource);

    [Fact]
    public void EvaluateInstall_GoodFacts_Passes()
    {
        Assert.True(MsiLegs.EvaluateInstall(GoodInstall(), Endpoint).IsSuccess);
    }

    [Fact]
    public void EvaluateInstall_RebootRequested_Passes()
    {
        Assert.True(MsiLegs.EvaluateInstall(GoodInstall(exitCode: 3010), Endpoint).IsSuccess);
    }

    [Fact]
    public void EvaluateInstall_DemandStart_Fails()
    {
        var result = MsiLegs.EvaluateInstall(GoodInstall(startType: "DEMAND_START"), Endpoint);

        Assert.True(result.IsFailure);
        Assert.Contains("DEMAND_START", result.Error);
    }

    [Fact]
    public void EvaluateInstall_NotLocalSystem_Fails()
    {
        Assert.True(MsiLegs.EvaluateInstall(GoodInstall(account: "NT AUTHORITY\\NetworkService"), Endpoint).IsFailure);
    }

    [Fact]
    public void EvaluateInstall_ServiceNotRunning_Fails()
    {
        Assert.True(MsiLegs.EvaluateInstall(GoodInstall(state: "STOPPED"), Endpoint).IsFailure);
    }

    [Fact]
    public void EvaluateInstall_MissingControlPlaneEndpoint_Fails()
    {
        var registry = GoodRegistry();
        registry["ControlPlaneEndpoint"] = null;
        var missing = GoodInstall() with { Registry = registry };

        var result = MsiLegs.EvaluateInstall(missing, Endpoint);

        Assert.True(result.IsFailure);
        Assert.Contains("ControlPlaneEndpoint", result.Error);
    }

    [Fact]
    public void EvaluateInstall_TwoRelatedProducts_Fails()
    {
        var result = MsiLegs.EvaluateInstall(GoodInstall(products: 2), Endpoint);

        Assert.True(result.IsFailure);
        Assert.Contains("found 2", result.Error);
    }

    [Fact]
    public void EvaluateUninstall_CleanMachine_PassesWithoutWarnings()
    {
        var result = MsiLegs.EvaluateUninstall(CleanUninstall());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void EvaluateUninstall_ServiceStillExists_Fails()
    {
        Assert.True(MsiLegs.EvaluateUninstall(CleanUninstall(serviceExists: true)).IsFailure);
    }

    [Fact]
    public void EvaluateUninstall_ControlPlaneEndpointRemains_Fails()
    {
        var result = MsiLegs.EvaluateUninstall(CleanUninstall(remaining: ["ControlPlaneEndpoint"]));

        Assert.True(result.IsFailure);
        Assert.Contains("ControlPlaneEndpoint", result.Error);
    }

    [Fact]
    public void EvaluateUninstall_AgentCertificateInMy_WarnsAndPasses()
    {
        var certificate = new StoreCert("CN=agent", "AAAA", "BBBB", [], DateTime.UtcNow, DateTime.UtcNow.AddDays(1), true, null);

        var result = MsiLegs.EvaluateUninstall(CleanUninstall(certificates: [certificate]));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, w => w.StartsWith("certificate:AAAA", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateUninstall_StateFolderAndEventSource_AreResidueWarnings()
    {
        var result = MsiLegs.EvaluateUninstall(CleanUninstall(stateDirectory: true, eventSource: true));

        Assert.True(result.IsSuccess);
        Assert.Contains(MsiLegs.ProgramDataResidue, result.Value);
        Assert.Contains(result.Value, w => w.StartsWith("eventSource:", StringComparison.Ordinal));
    }
}
