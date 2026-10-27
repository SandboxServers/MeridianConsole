using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>What U10 leaves in place for the later steps.</summary>
/// <param name="Tls">The run-scoped TLS leaf: the PFX for the Gateway and the public thumbprint in <c>Root</c>.</param>
/// <param name="DecoyThumbprint">The SHA-1 thumbprint of the decoy CA that U3 looks for in <c>Root</c>.</param>
/// <param name="BinaryPath">The service binary path given to <c>sc.exe create</c>.</param>
internal sealed record ServiceSetupOutcome(UatTlsMaterial Tls, string DecoyThumbprint, string BinaryPath);

/// <summary>The machine seams U10 uses, so the step runs with fakes in the tests.</summary>
/// <param name="Machine">The machine writes (certificate adds), recorded in the manifest by the callee.</param>
/// <param name="Service">The <c>DhadgarAgent</c> service control.</param>
/// <param name="ReadRoot">Reads the current <c>LocalMachine\Root</c> thumbprints.</param>
internal sealed record ServiceSetupDeps(
    IMachineActions Machine,
    IServiceControl Service,
    Func<Result<RootSnapshot>> ReadRoot);

/// <summary>
/// U10 "Service setup" (D-S222 (A)). The agent's <c>appsettings.json</c> is edited as in process mode, the run-scoped
/// TLS leaf and the decoy CA are added to <c>LocalMachine\Root</c>, and the <c>DhadgarAgent</c> service is created as a
/// demand-start LocalSystem service. The Gateway needs the TLS certificate at start, so U10 runs before U1 in service mode.
/// </summary>
internal static class ServiceSetup
{
    /// <summary>The step's name in the evidence and in failure messages.</summary>
    public const string StepName = "U10";

    private const string AppSettingsFileName = "appsettings.json";
    private const string DemandStart = "DEMAND_START";
    private const string LocalSystem = "LocalSystem";
    private const string Stopped = "STOPPED";

    /// <summary>
    /// Runs U10. Pass when both thumbprints are in <c>LocalMachine\Root</c>, the service is demand start, runs as
    /// LocalSystem and is stopped. Records the outcome under <c>U10</c> in <paramref name="evidence"/>.
    /// </summary>
    public static async Task<Result<ServiceSetupOutcome>> RunAsync(
        RunPaths paths,
        string runId,
        CleanupManifest manifest,
        Redactor redactor,
        AgentConfig config,
        ServiceSetupDeps deps,
        Dictionary<string, object?> evidence)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(deps);
        ArgumentNullException.ThrowIfNull(evidence);

        var outcome = await SetUpAsync(paths, runId, manifest, redactor, config, deps);
        if (outcome.IsFailure)
        {
            evidence[StepName] = "fail";
            return Result<ServiceSetupOutcome>.Failure($"{StepName}: {outcome.Error}");
        }

        evidence["tlsThumbprint"] = outcome.Value.Tls.ThumbprintSha1;
        evidence["decoyThumbprint"] = outcome.Value.DecoyThumbprint;
        evidence["serviceBinaryPath"] = outcome.Value.BinaryPath;
        evidence[StepName] = "pass";
        return outcome;
    }

    private static async Task<Result<ServiceSetupOutcome>> SetUpAsync(
        RunPaths paths,
        string runId,
        CleanupManifest manifest,
        Redactor redactor,
        AgentConfig config,
        ServiceSetupDeps deps)
    {
        var applied = new AgentPublishConfig(paths).Apply(Path.Combine(paths.AgentPublish, AppSettingsFileName), config);
        if (applied.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(applied.Error!);
        }

        var tls = UatTlsCertificate.Create(paths, runId, manifest, redactor, deps.Machine);
        if (tls.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(tls.Error!);
        }

        var decoy = DecoyCa.Plant(runId, manifest, deps.Machine);
        if (decoy.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(decoy.Error!);
        }

        var binaryPath = Path.Combine(paths.AgentPublish, AgentProcess.ExecutableName);
        var created = await deps.Service.Create(binaryPath);
        if (created.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(created.Error!);
        }

        var serviceConfig = await deps.Service.QueryConfig();
        if (serviceConfig.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(serviceConfig.Error!);
        }

        if (!string.Equals(serviceConfig.Value.StartType, DemandStart, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ServiceSetupOutcome>.Failure($"service start type is {serviceConfig.Value.StartType}, expected {DemandStart}");
        }

        if (!string.Equals(serviceConfig.Value.Account, LocalSystem, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ServiceSetupOutcome>.Failure($"service account is {serviceConfig.Value.Account}, expected {LocalSystem}");
        }

        var state = await deps.Service.Query();
        if (state.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(state.Error!);
        }

        if (!string.Equals(state.Value, Stopped, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ServiceSetupOutcome>.Failure($"service state is {state.Value}, expected {Stopped}");
        }

        var root = deps.ReadRoot();
        if (root.IsFailure)
        {
            return Result<ServiceSetupOutcome>.Failure(root.Error!);
        }

        var inRoot = root.Value.Thumbprints;
        if (!inRoot.Contains(tls.Value.ThumbprintSha1, StringComparer.OrdinalIgnoreCase))
        {
            return Result<ServiceSetupOutcome>.Failure("the TLS leaf is not in LocalMachine\\Root");
        }

        if (!inRoot.Contains(decoy.Value, StringComparer.OrdinalIgnoreCase))
        {
            return Result<ServiceSetupOutcome>.Failure("the decoy CA is not in LocalMachine\\Root");
        }

        return Result<ServiceSetupOutcome>.Success(new ServiceSetupOutcome(tls.Value, decoy.Value, binaryPath));
    }
}
