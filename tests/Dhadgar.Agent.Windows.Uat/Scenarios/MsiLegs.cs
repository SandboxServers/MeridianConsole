using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>What U11a read from the machine after the MSI install. A missing value is null and fails the check.</summary>
/// <param name="ExitCode">The <c>msiexec /i</c> exit code.</param>
/// <param name="StartType">The <c>sc.exe qc</c> START_TYPE, for example <c>AUTO_START</c>.</param>
/// <param name="Account">The <c>sc.exe qc</c> SERVICE_START_NAME.</param>
/// <param name="ServiceState">The <c>sc.exe query</c> STATE, for example <c>RUNNING</c>.</param>
/// <param name="Registry">The HKLM agent values by name (<see cref="MsiLegs.RegistryValueNames"/>); null when absent.</param>
/// <param name="ExecutableExists">True when <c>&lt;InstallPath&gt;\Dhadgar.Agent.Windows.exe</c> exists.</param>
/// <param name="RelatedProducts">The product codes that carry the agent's UpgradeCode.</param>
/// <param name="RelatedProductsError">Set when the installer could not list the products; the check then fails.</param>
internal sealed record MsiInstallFacts(
    int ExitCode,
    string? StartType,
    string? Account,
    string? ServiceState,
    IReadOnlyDictionary<string, string?> Registry,
    bool ExecutableExists,
    IReadOnlyList<string> RelatedProducts,
    string? RelatedProductsError = null);

/// <summary>What U11b read from the machine after the MSI uninstall.</summary>
/// <param name="ExitCode">The <c>msiexec /x</c> exit code.</param>
/// <param name="ServiceExists">False only when <c>sc.exe query</c> reports the service does not exist (1060).</param>
/// <param name="RelatedProducts">The product codes that still carry the agent's UpgradeCode.</param>
/// <param name="InstallDirectoryExists">True when <c>%ProgramFiles%\Meridian Console\Agent</c> still exists.</param>
/// <param name="RemainingRegistryValues">The names of the MSI registry values that are still present.</param>
/// <param name="AgentCertificatesInMy">The agent certificates left in <c>LocalMachine\My</c> (residue, a warning).</param>
/// <param name="CaStoreHasCertificates">True when the <c>MeridianConsoleAgent</c> store still holds certificates (residue).</param>
/// <param name="StateDirectoryExists">True when <c>%ProgramData%\Meridian Console\Agent</c> still exists (residue).</param>
/// <param name="EventSourceExists">True when the event source is still registered (residue).</param>
/// <param name="RelatedProductsError">Set when the installer could not list the products; the check then fails, since absence is unproven.</param>
internal sealed record MsiUninstallFacts(
    int ExitCode,
    bool ServiceExists,
    IReadOnlyList<string> RelatedProducts,
    bool InstallDirectoryExists,
    IReadOnlyList<string> RemainingRegistryValues,
    IReadOnlyList<StoreCert> AgentCertificatesInMy,
    bool CaStoreHasCertificates,
    bool StateDirectoryExists,
    bool EventSourceExists,
    string? RelatedProductsError = null);

/// <summary>
/// The MSI leg (P23, D-S223). Its manifest entries are recorded before U11a's install, so the cleanup engine can
/// undo everything the installer leaves, even when the install fails part way. The checks are pure evaluators over
/// the facts, so the unit tests need no machine.
/// </summary>
internal static class MsiLegs
{
    /// <summary>U11a's step name in the evidence.</summary>
    public const string InstallStepName = "U11a";

    /// <summary>U11b's step name in the evidence.</summary>
    public const string UninstallStepName = "U11b";

    /// <summary>The HKLM values the MSI writes and U11b requires gone (EnrollmentToken included, D-S229).</summary>
    public static readonly IReadOnlyList<string> RegistryValueNames =
    [
        "InstallPath",
        "Version",
        "DataPath",
        "ServiceInstalled",
        AgentWindowsFacts.ControlPlaneEndpointValueName,
        AgentWindowsFacts.EnrollmentTokenValueName,
    ];

    /// <summary>Residue names as results.json writes them (the directory names use the placeholders the spec lists).</summary>
    public const string ProgramDataResidue = @"directory:%ProgramData%\Meridian Console\Agent";

    private const string HklmHive = "HKLM";
    private const string ParentKeyPath = AgentWindowsFacts.RegistryParentKeyPath;
    private const string AgentKeyPath = AgentWindowsFacts.RegistryKeyPath;
    private const string AutoStart = "AUTO_START";
    private const string LocalSystem = "LocalSystem";
    private const string Running = "RUNNING";
    private const string ServiceInstalledValueName = "ServiceInstalled";
    private const string ServiceInstalledOn = "1";
    private const string ServiceMissingText = "does not exist";
    private const int MsiSuccess = 0;
    private const int MsiSuccessRebootRequested = 3010;

    /// <summary>
    /// The entries the MSI leg may cause, in the spec's order: the product, the service, the two registry keys, the
    /// event source, the CA store, and the two directories. Each records whether the item existed before.
    /// </summary>
    internal static IReadOnlyList<CleanupEntry> PreInstallEntries(
        string msiPath,
        bool registryParentExisted,
        bool registryKeyExisted,
        bool eventSourceExisted,
        bool programDataExisted,
        bool programFilesExisted) =>
    [
        new MsiProductEntry(AgentWindowsFacts.MsiUpgradeCode, msiPath),
        new ServiceEntry(AgentWindowsFacts.ServiceName),
        new RegistryKeyEntry(HklmHive, ParentKeyPath, registryParentExisted),
        new RegistryKeyEntry(HklmHive, AgentKeyPath, registryKeyExisted),
        new EventSourceEntry(AgentWindowsFacts.EventLogSourceName, eventSourceExisted),
        new CertStoreEntry(AgentWindowsFacts.CaStoreName, ExistedBefore: false),
        new DirectoryEntry(AgentDirectories.MachineAgentDirectory, programDataExisted),
        new DirectoryEntry(AgentDirectories.MachineProgramFilesDirectory, programFilesExisted),
    ];

    /// <summary>Reads the machine and appends <see cref="PreInstallEntries"/> to <paramref name="manifest"/> in order.</summary>
    internal static Result AppendPreInstallEntries(CleanupManifest manifest, string msiPath)
    {
        var entries = PreInstallEntries(
            msiPath,
            registryParentExisted: RegistryKeyExists(ParentKeyPath),
            registryKeyExisted: RegistryKeyExists(AgentKeyPath),
            eventSourceExisted: EventSourceExists(AgentWindowsFacts.EventLogSourceName),
            programDataExisted: Directory.Exists(AgentDirectories.MachineAgentDirectory),
            programFilesExisted: Directory.Exists(AgentDirectories.MachineProgramFilesDirectory));

        foreach (var entry in entries)
        {
            var appended = manifest.Append(entry);
            if (appended.IsFailure)
            {
                return appended;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// U11a's checks (spec step 7), in order: the exit code, AUTO_START, LocalSystem, RUNNING, the HKLM values with
    /// <c>ControlPlaneEndpoint</c> equal to <paramref name="expectedEndpoint"/>, the executable, and exactly one related product.
    /// </summary>
    internal static Result EvaluateInstall(MsiInstallFacts facts, string expectedEndpoint)
    {
        if (facts.ExitCode is not (MsiSuccess or MsiSuccessRebootRequested))
        {
            return Result.Failure($"msiexec /i exited {facts.ExitCode}");
        }

        if (!string.Equals(facts.StartType, AutoStart, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure($"service start type is {facts.StartType ?? "unknown"}, expected {AutoStart}");
        }

        if (!string.Equals(facts.Account, LocalSystem, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure($"service account is {facts.Account ?? "unknown"}, expected {LocalSystem}");
        }

        if (!string.Equals(facts.ServiceState, Running, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure($"service state is {facts.ServiceState ?? "unknown"}, expected {Running}");
        }

        foreach (var name in RegistryValueNames.Where(n => n != AgentWindowsFacts.EnrollmentTokenValueName))
        {
            if (string.IsNullOrEmpty(facts.Registry.GetValueOrDefault(name)))
            {
                return Result.Failure($"HKLM agent value {name} is missing");
            }
        }

        if (!string.Equals(facts.Registry.GetValueOrDefault(ServiceInstalledValueName), ServiceInstalledOn, StringComparison.Ordinal))
        {
            return Result.Failure("HKLM agent value ServiceInstalled is not 1");
        }

        if (!string.Equals(facts.Registry.GetValueOrDefault(AgentWindowsFacts.ControlPlaneEndpointValueName), expectedEndpoint, StringComparison.Ordinal))
        {
            return Result.Failure("HKLM agent value ControlPlaneEndpoint is missing or is not the Gateway URL");
        }

        if (!facts.ExecutableExists)
        {
            return Result.Failure($"{AgentProcess.ExecutableName} is not in the install folder");
        }

        if (facts.RelatedProductsError is not null)
        {
            return Result.Failure($"related products unreadable: {facts.RelatedProductsError}");
        }

        if (facts.RelatedProducts.Count != 1)
        {
            return Result.Failure($"expected one related product for the agent UpgradeCode, found {facts.RelatedProducts.Count}");
        }

        return Result.Success();
    }

    /// <summary>
    /// U11b's checks, in order: the exit code, the service gone (1060), no related product, no install folder, and no
    /// MSI registry value left. Residue (agent certificates in <c>My</c>, the CA store, the state folder, the event
    /// source) is not a failure: it returns as warnings, one per item, for the results and the cleanup pass.
    /// </summary>
    internal static Result<IReadOnlyList<string>> EvaluateUninstall(MsiUninstallFacts facts)
    {
        if (facts.ExitCode is not (MsiSuccess or MsiSuccessRebootRequested))
        {
            return Result<IReadOnlyList<string>>.Failure($"msiexec /x exited {facts.ExitCode}");
        }

        if (facts.ServiceExists)
        {
            return Result<IReadOnlyList<string>>.Failure($"service {AgentWindowsFacts.ServiceName} still exists after uninstall");
        }

        if (facts.RelatedProductsError is not null)
        {
            return Result<IReadOnlyList<string>>.Failure($"related products unreadable, absence unproven: {facts.RelatedProductsError}");
        }

        if (facts.RelatedProducts.Count > 0)
        {
            return Result<IReadOnlyList<string>>.Failure($"{facts.RelatedProducts.Count} related product(s) still installed");
        }

        if (facts.InstallDirectoryExists)
        {
            return Result<IReadOnlyList<string>>.Failure(@"%ProgramFiles%\Meridian Console\Agent still exists after uninstall");
        }

        if (facts.RemainingRegistryValues.Count > 0)
        {
            return Result<IReadOnlyList<string>>.Failure(
                $"HKLM agent values remain after uninstall: {string.Join(", ", facts.RemainingRegistryValues)}");
        }

        var warnings = new List<string>();
        warnings.AddRange(facts.AgentCertificatesInMy.Select(c => $"certificate:{c.Sha1Thumbprint} left in LocalMachine\\My"));
        if (facts.CaStoreHasCertificates)
        {
            warnings.Add($"certStore:{AgentWindowsFacts.CaStoreName}");
        }

        if (facts.StateDirectoryExists)
        {
            warnings.Add(ProgramDataResidue);
        }

        if (facts.EventSourceExists)
        {
            warnings.Add($"eventSource:{AgentWindowsFacts.EventLogSourceName}");
        }

        return Result<IReadOnlyList<string>>.Success(warnings);
    }

    /// <summary>
    /// Appends the pre-install manifest entries with <paramref name="appendEntries"/>, and only when that succeeds runs
    /// <see cref="InstallAsync"/>. The entries therefore always precede the <c>msiexec</c> call. A failed append fails U11a.
    /// </summary>
    public static async Task<Result> PrepareAndInstallAsync(
        Func<Result> appendEntries,
        IMsiInstaller installer,
        IServiceControl service,
        IMsiProducts products,
        string msiPath,
        MintedToken token,
        Uri endpoint,
        TimeSpan window,
        TimeSpan pollInterval,
        Dictionary<string, object?> evidence,
        CancellationToken ct)
    {
        var appended = appendEntries();
        if (appended.IsFailure)
        {
            evidence[InstallStepName] = "fail";
            return Result.Failure($"{InstallStepName}: manifest entries could not be recorded: {appended.Error}");
        }

        return await InstallAsync(installer, service, products, msiPath, token, endpoint, window, pollInterval, evidence, ct);
    }

    /// <summary>
    /// Runs U11a: <c>msiexec /i</c> through <paramref name="installer"/>, then polls the machine until the checks pass or
    /// <paramref name="window"/> ends (60 s in a real run). Records the install under <c>msi</c> in <paramref name="evidence"/>.
    /// </summary>
    public static async Task<Result> InstallAsync(
        IMsiInstaller installer,
        IServiceControl service,
        IMsiProducts products,
        string msiPath,
        MintedToken token,
        Uri endpoint,
        TimeSpan window,
        TimeSpan pollInterval,
        Dictionary<string, object?> evidence,
        CancellationToken ct)
    {
        var msi = new Dictionary<string, object?>(StringComparer.Ordinal);
        evidence["msi"] = msi;
        var installed = installer.Install(msiPath, token, endpoint);
        if (installed.IsFailure)
        {
            evidence[InstallStepName] = "fail";
            return Result.Failure($"{InstallStepName}: {installed.Error}");
        }

        msi["installExitCode"] = installed.Value.ExitCode;
        msi["installCommandLine"] = installed.Value.LoggedCommandLine;
        msi["installWarnings"] = installed.Value.Warnings;
        EvidenceWarnings(evidence).AddRange(installed.Value.Warnings);

        var deadline = DateTime.UtcNow + window;
        Result check;
        while (true)
        {
            var facts = await ReadInstallFactsAsync(installed.Value.ExitCode, service, products);
            check = EvaluateInstall(facts, endpoint.AbsoluteUri);
            if (check.IsSuccess || DateTime.UtcNow >= deadline)
            {
                msi["productCode"] = facts.RelatedProducts.Count > 0 ? facts.RelatedProducts[0] : null;
                msi["productVersion"] = facts.RelatedProducts.Count == 1
                    && products.ProductVersion(facts.RelatedProducts[0]) is { IsSuccess: true } version
                    ? version.Value
                    : null;
                break;
            }

            await Task.Delay(pollInterval, ct);
        }

        msi["endpoint"] = endpoint.AbsoluteUri;
        if (check.IsFailure)
        {
            evidence[InstallStepName] = "fail";
            return Result.Failure($"{InstallStepName}: {check.Error}");
        }

        evidence[InstallStepName] = "pass";
        return Result.Success();
    }

    /// <summary>
    /// Runs U11b: <c>msiexec /x</c> through <paramref name="installer"/> (no service is stopped by hand), then reads the
    /// machine once. Residue becomes <c>msi.residue</c> and the step's warnings; a failed check fails the step.
    /// </summary>
    public static async Task<Result> UninstallAsync(
        IMsiInstaller installer,
        IServiceControl service,
        IMsiProducts products,
        IStoreInspector store,
        string msiPath,
        Dictionary<string, object?> evidence)
    {
        var msi = evidence.TryGetValue("msi", out var existing) && existing is Dictionary<string, object?> dict
            ? dict
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        evidence["msi"] = msi;

        var uninstalled = installer.Uninstall(msiPath);
        if (uninstalled.IsFailure)
        {
            evidence[UninstallStepName] = "fail";
            return Result.Failure($"{UninstallStepName}: {uninstalled.Error}");
        }

        msi["uninstallExitCode"] = uninstalled.Value.ExitCode;
        msi["uninstallCommandLine"] = uninstalled.Value.LoggedCommandLine;
        msi["uninstallWarnings"] = uninstalled.Value.Warnings;
        EvidenceWarnings(evidence).AddRange(uninstalled.Value.Warnings);

        var facts = await ReadUninstallFactsAsync(uninstalled.Value.ExitCode, service, products, store);
        var checkResult = EvaluateUninstall(facts);
        if (checkResult.IsFailure)
        {
            evidence[UninstallStepName] = "fail";
            return Result.Failure($"{UninstallStepName}: {checkResult.Error}");
        }

        msi["residue"] = checkResult.Value.ToList();
        EvidenceWarnings(evidence).AddRange(checkResult.Value);
        evidence[UninstallStepName] = "pass";
        return Result.Success();
    }

    /// <summary>
    /// Custody after a failed U11a (P23 security review, N2). The MSI starts the LocalSystem service and the agent may
    /// have enrolled, so a client certificate may now sit in <c>LocalMachine\My</c>. <see cref="AgentLegs"/> never runs
    /// on this path, so nothing else records it: this stops the service (ignoring a failure, which becomes a warning)
    /// and appends a <c>certificate</c> entry for every agent certificate in My, so cleanup removes it and its key.
    /// Duplicate entries are harmless because the certificate removal is idempotent.
    /// </summary>
    internal static async Task RecordCustodyAfterFailedInstallAsync(
        IServiceControl service,
        IStoreInspector store,
        CleanupManifest manifest,
        Dictionary<string, object?> evidence)
    {
        var warnings = EvidenceWarnings(evidence);
        var stopped = await service.StopAsync(CancellationToken.None);
        if (stopped.IsFailure)
        {
            warnings.Add($"agent stop after failed install: {stopped.Error}");
        }

        try
        {
            foreach (var certificate in store.AgentCertificatesInMy())
            {
                foreach (var entry in AgentLegs.CertificateEntries(certificate))
                {
                    var appended = manifest.Append(entry);
                    if (appended.IsFailure)
                    {
                        warnings.Add($"certificate custody: {appended.Error}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or IOException)
        {
            warnings.Add($"certificate custody: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The evidence <c>warnings</c> list, created when the first step needs it. <c>AgentLegs.RunStepsAsync</c> reuses
    /// the same list, so an install warning and the residue survive into the results.
    /// </summary>
    internal static List<string> EvidenceWarnings(Dictionary<string, object?> evidence)
    {
        var list = evidence.TryGetValue("warnings", out var existing) && existing is List<string> warnings
            ? warnings
            : new List<string>();
        evidence["warnings"] = list;
        return list;
    }

    private static async Task<MsiInstallFacts> ReadInstallFactsAsync(int exitCode, IServiceControl service, IMsiProducts products)
    {
        var config = await service.QueryConfig();
        var state = await service.Query();
        var registry = ReadAgentRegistry();
        var installPath = registry.GetValueOrDefault("InstallPath");
        var exe = string.IsNullOrEmpty(installPath) ? null : Path.Combine(installPath, AgentProcess.ExecutableName);
        var related = products.RelatedProducts(AgentWindowsFacts.MsiUpgradeCode);
        return new MsiInstallFacts(
            exitCode,
            config.IsSuccess ? config.Value.StartType : null,
            config.IsSuccess ? config.Value.Account : null,
            state.IsSuccess ? state.Value : null,
            registry,
            exe is not null && File.Exists(exe),
            related.IsSuccess ? related.Value : [],
            related.IsFailure ? related.Error : null);
    }

    private static async Task<MsiUninstallFacts> ReadUninstallFactsAsync(
        int exitCode, IServiceControl service, IMsiProducts products, IStoreInspector store)
    {
        var state = await service.Query();
        var serviceExists = state.IsSuccess || !state.Error!.Contains(ServiceMissingText, StringComparison.OrdinalIgnoreCase);
        var registry = ReadAgentRegistry();
        var remaining = RegistryValueNames.Where(name => !string.IsNullOrEmpty(registry.GetValueOrDefault(name))).ToList();
        var related = products.RelatedProducts(AgentWindowsFacts.MsiUpgradeCode);
        return new MsiUninstallFacts(
            exitCode,
            serviceExists,
            related.IsSuccess ? related.Value : [],
            Directory.Exists(AgentDirectories.MachineProgramFilesAgentDirectory),
            remaining,
            store.AgentCertificatesInMy(),
            CaStoreHasCertificates(),
            Directory.Exists(AgentDirectories.MachineAgentDirectory),
            EventSourceExists(AgentWindowsFacts.EventLogSourceName),
            related.IsFailure ? related.Error : null);
    }

    private static Dictionary<string, string?> ReadAgentRegistry()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var key = Registry.LocalMachine.OpenSubKey(AgentKeyPath);
        foreach (var name in RegistryValueNames)
        {
            values[name] = key?.GetValue(name)?.ToString();
        }

        return values;
    }

    /// <summary>True when the agent's CA store holds a certificate. A store that cannot be read counts as residue.</summary>
    private static bool CaStoreHasCertificates()
    {
#pragma warning disable CA1031 // A failed read is residue, never a crash of the cleanup pass.
        try
        {
            using var store = new X509Store(AgentWindowsFacts.CaStoreName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Count > 0;
        }
        catch (Exception)
        {
            return true;
        }
#pragma warning restore CA1031
    }

    private static bool RegistryKeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    /// <summary>
    /// True when the event source is already registered. When the check itself fails the source counts as existing,
    /// so the undo never deletes a source the run cannot prove it created.
    /// </summary>
    private static bool EventSourceExists(string source)
    {
#pragma warning disable CA1031 // A failed check must not stop the leg; see the comment above.
        try
        {
            return EventLog.SourceExists(source);
        }
        catch (Exception)
        {
            return true;
        }
#pragma warning restore CA1031
    }
}
