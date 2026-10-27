using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>
/// U1 and the enrollment smoke (P19): containers, Nodes and Gateway healthy, then S-mint, S-enroll, S-negotiate,
/// S-hub-ws and S-db. The sub-steps are recorded as evidence of U1 because <see cref="IUatScenario"/> returns one
/// evidence dictionary. Stops at the first failing sub-step.
/// </summary>
internal sealed class StackScenario : IUatScenario
{
    private static readonly TimeSpan HubStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MsiTokenLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MsiInstallWindow = TimeSpan.FromSeconds(60);
    private const int AgentHeartbeatSeconds = 10;
    private const int AgentConnectionCheckSeconds = 10;

    /// <summary>Service mode (P22): the service control and U10's outcome, kept for the agent legs.</summary>
    private sealed record ServiceRun(ServiceControl Control, ServiceSetupOutcome Setup);

    /// <summary>Creates the scenario for one mode name (<c>process</c>, <c>service</c>, <c>msi</c> or <c>stack-only</c>).</summary>
    public StackScenario(string mode)
    {
        Mode = mode;
    }

    /// <inheritdoc />
    public string Mode { get; }

    /// <inheritdoc />
    public async Task<Result<Dictionary<string, object?>>> RunAsync(RunContext context, CancellationToken ct)
    {
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        Directory.CreateDirectory(context.Paths.Logs);

        var jobResult = JobObject.Create();
        if (jobResult.IsFailure)
        {
            return Result<Dictionary<string, object?>>.Failure(jobResult.Error!);
        }

        using var job = jobResult.Value;
        using var host = new ServiceHost(context.Paths, job, context.Redactor, context.Manifest);
        try
        {
            var outcome = await RunStepsAsync(context, host, job, evidence, ct);
            return outcome.IsSuccess
                ? Result<Dictionary<string, object?>>.Success(evidence)
                : Result<Dictionary<string, object?>>.Failure(outcome.Error!);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<Result> RunStepsAsync(
        RunContext context, ServiceHost host, JobObject job, Dictionary<string, object?> evidence, CancellationToken ct)
    {
        var redactor = context.Redactor;
        var ports = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var port = FreePorts.AllocateLoopbackPort();
            if (port.IsFailure)
            {
                return Fail(evidence, "U1", port.Error!);
            }

            ports.Add(port.Value);
        }

        var (postgresPort, nodesPort, gatewayPort) = (ports[0], ports[1], ports[2]);
        var gatewayBase = ServiceHost.GatewayBase(gatewayPort);

        // U10 runs before U1 in service mode, because the Gateway needs the run-scoped TLS certificate at start (P22).
        ServiceRun? serviceRun = null;
        GatewayTls? tls = null;
        ServiceSetupOutcome? msiSetup = null;
        if (context.Mode == "service")
        {
            var setUp = await SetUpServiceAsync(context, gatewayBase, evidence);
            if (setUp.IsFailure)
            {
                return Result.Failure(setUp.Error!);
            }

            serviceRun = setUp.Value;
            tls = new GatewayTls(setUp.Value.Setup.Tls.PfxPath, setUp.Value.Setup.Tls.Password);
        }
        else if (context.Mode == "msi")
        {
            // U10 in MSI mode (P23): the TLS leaf and the decoy only; the MSI creates the service.
            var setup = await ServiceSetup.RunTlsOnlyAsync(
                context.Paths, context.RunId, context.Manifest, context.Redactor, new WindowsMachineActions(), RootSnapshot.Take, evidence);
            if (setup.IsFailure)
            {
                return Result.Failure(setup.Error!);
            }

            msiSetup = setup.Value;
            tls = new GatewayTls(setup.Value.Tls.PfxPath, setup.Value.Tls.Password);
        }

        var settings = new StackSettings(
            postgresPort, FreePorts.RabbitMqPort, nodesPort, gatewayPort,
            Secrets.GeneratePassword(redactor), Secrets.GeneratePassword(redactor), Secrets.GeneratePassword(redactor),
            tls);
        evidence["ports"] = $"postgres {postgresPort}, rabbitmq {FreePorts.RabbitMqPort}, nodes {nodesPort}, gateway {gatewayPort}";

        // U1: containers, then Nodes and the Gateway.
        var docker = new DockerStack(context.RunId, context.Manifest, redactor);
        var pg = await docker.StartPostgresAsync(postgresPort, settings.PostgresPassword, ct);
        if (pg.IsFailure)
        {
            return Fail(evidence, "U1", pg.Error!);
        }

        var mq = await docker.StartRabbitMqAsync(settings.RabbitMqPassword, ct);
        if (mq.IsFailure)
        {
            return Fail(evidence, "U1", mq.Error!);
        }

        evidence["postgresImage"] = await ImageIdAsync(docker.PostgresName, ct);
        evidence["rabbitmqImage"] = await ImageIdAsync(docker.RabbitMqName, ct);

        var nodesDb = new NodesDb(
            $"Host=127.0.0.1;Port={postgresPort};Database={DockerStack.DatabaseName};Username=postgres;Password={settings.PostgresPassword}");

        var started = await host.StartAsync(settings, ct);
        if (started.IsFailure)
        {
            return Fail(evidence, "U1", started.Error!);
        }

        evidence["nodesPid"] = host.NodesProcessId;
        evidence["gatewayPid"] = host.GatewayProcessId;
        evidence["U1"] = "pass";

        // S-mint
        var organizationId = Guid.NewGuid();
        evidence["organizationId"] = organizationId.ToString();
        var minter = new TokenMinter(() => nodesDb.Create(), context.RunId, redactor);
        var minted = await minter.MintAsync(organizationId, "uat-smoke", ct);
        if (minted.IsFailure)
        {
            return Fail(evidence, "S-mint", minted.Error!);
        }

        evidence["tokenId"] = minted.Value.ToString();
        evidence["S-mint"] = "pass";

        // S-enroll
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var hardware = new HardwareInventoryRequest(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            DiskBytes(),
            []);
        var request = new EnrollNodeRequest(
            minted.Value.PlainText, "windows", hardware,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "uat");

        EnrollNodeResponse? enrolled;
        using (var client = GatewayClients.Create(null, TimeSpan.FromSeconds(60)))
        {
            using var response = await client.PostAsJsonAsync(new Uri(gatewayBase, "api/v1/agents/enroll"), request, ct);
            evidence["enrollStatus"] = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.Created)
            {
                return Fail(evidence, "S-enroll", $"enroll returned {(int)response.StatusCode}, expected 201 (Created)");
            }

            enrolled = await response.Content.ReadFromJsonAsync<EnrollNodeResponse>(ct);
        }

        if (enrolled is null)
        {
            return Fail(evidence, "S-enroll", "enroll response body was empty");
        }

        var certificate = TestKeyCertificate.FromPem(key, enrolled.Certificate);
        if (certificate.IsFailure)
        {
            return Fail(evidence, "S-enroll", certificate.Error!);
        }

        using var clientCertificate = certificate.Value;
        var spiffe = AgentCertificateProfile.SpiffeId(enrolled.NodeId);
        var san = clientCertificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san is null || !san.Format(false).Contains(spiffe, StringComparison.Ordinal))
        {
            return Fail(evidence, "S-enroll", "certificate SAN does not equal the node's SPIFFE ID");
        }

        using var certKey = clientCertificate.GetECDsaPublicKey();
        if (certKey is null || !certKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
        {
            return Fail(evidence, "S-enroll", "certificate public key does not equal the enrolled key");
        }

        evidence["nodeId"] = enrolled.NodeId.ToString();
        evidence["thumbprint"] = enrolled.CertificateThumbprint;
        evidence["S-enroll"] = "pass";

        // S-negotiate
        var negotiate = new Uri(gatewayBase, AgentHubProtocol.Path.TrimStart('/') + "/negotiate?negotiateVersion=1");
        var withCert = await PostNegotiateAsync(negotiate, clientCertificate, ct);
        var withoutCert = await PostNegotiateAsync(negotiate, null, ct);
        evidence["negotiateWithCertificate"] = withCert;
        evidence["negotiateWithoutCertificate"] = withoutCert;
        if (withCert != 200 || withoutCert != 401)
        {
            return Fail(evidence, "S-negotiate", $"expected 200 with and 401 without a certificate, got {withCert} and {withoutCert}");
        }

        evidence["S-negotiate"] = "pass";

        // S-hub-ws
        var hub = await HubHeartbeatAsync(gatewayPort, clientCertificate, evidence, ct);
        if (hub.IsFailure)
        {
            return Fail(evidence, "S-hub-ws", hub.Error!);
        }

        evidence["S-hub-ws"] = "pass";

        // S-db
        await using (var db = nodesDb.Create())
        {
            var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == enrolled.NodeId, ct);
            var token = await db.EnrollmentTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == minted.Value.Id, ct);
            if (node is null || node.OrganizationId != organizationId)
            {
                return Fail(evidence, "S-db", "node row missing or in another organization");
            }

            if (node.LastHeartbeat is null)
            {
                return Fail(evidence, "S-db", "node LastHeartbeat is not set after the hub heartbeat");
            }

            if (token?.UsedAt is null || token.UsedByNodeId != enrolled.NodeId)
            {
                return Fail(evidence, "S-db", "token is not marked used by the node");
            }

            evidence["lastHeartbeat"] = node.LastHeartbeat.Value.ToString("O");
        }

        evidence["S-db"] = "pass";
        if (context.Mode == "msi")
        {
            return await RunMsiLegsAsync(context, nodesDb, gatewayBase, msiSetup!, evidence, ct);
        }

        if (context.Mode is not ("process" or "service"))
        {
            return Result.Success();
        }

        return await RunAgentLegsAsync(context, job, nodesDb, gatewayBase, ServiceHost.NodesBase(nodesPort), serviceRun, evidence, ct);
    }

    /// <summary>
    /// U10 in service mode (P22): the run-scoped TLS leaf and the decoy CA go into <c>LocalMachine\Root</c>, and the
    /// <c>DhadgarAgent</c> service is created. The returned <see cref="ServiceRun"/> keeps the service control for the legs.
    /// </summary>
    private static async Task<Result<ServiceRun>> SetUpServiceAsync(
        RunContext context, Uri gatewayBase, Dictionary<string, object?> evidence)
    {
        var control = new ServiceControl(context.Paths, context.Manifest, context.Redactor);
        var deps = new ServiceSetupDeps(new WindowsMachineActions(), control, RootSnapshot.Take);
        var setup = await ServiceSetup.RunAsync(
            context.Paths, context.RunId, context.Manifest, context.Redactor, BuildAgentConfig(context, gatewayBase), deps, evidence);
        return setup.IsSuccess
            ? Result<ServiceRun>.Success(new ServiceRun(control, setup.Value))
            : Result<ServiceRun>.Failure(setup.Error!);
    }

    /// <summary>
    /// The MSI legs (P23) after the stack smoke: U11a (the manifest entries, then <c>msiexec /i</c> with a 10-minute
    /// token), U3 and U4 against the installed service, U5-U8 skipped, then U11b (<c>msiexec /x</c>). The driver never
    /// runs <c>msiexec</c> in a coder session; this runs only inside the user's <c>-Mode Msi</c> run.
    /// </summary>
    private static async Task<Result> RunMsiLegsAsync(
        RunContext context, NodesDb nodesDb, Uri gatewayBase, ServiceSetupOutcome setup,
        Dictionary<string, object?> evidence, CancellationToken ct)
    {
        var msiPath = FindMsi(context.Paths);
        if (msiPath.IsFailure)
        {
            return Fail(evidence, MsiLegs.InstallStepName, msiPath.Error!);
        }

        var organizationId = Guid.NewGuid();
        evidence["legsOrganizationId"] = organizationId.ToString();
        var minter = new TokenMinter(() => nodesDb.Create(), context.RunId, context.Redactor);
        var minted = await minter.MintAsync(organizationId, "uat-msi", ct, MsiTokenLifetime);
        if (minted.IsFailure)
        {
            return Fail(evidence, MsiLegs.InstallStepName, minted.Error!);
        }

        var control = new ServiceControl(context.Paths, context.Manifest, context.Redactor);
        var products = new MsiProducts();
        var installer = new MsiInstaller(context.Paths, context.Redactor);
        // A failed install (or a cancelled one) may already have enrolled the agent; its certificate is recorded here,
        // since AgentLegs does not run on this path. A successful install keeps the service running for the legs.
        var installSucceeded = false;
        Result installed;
        try
        {
            installed = await MsiLegs.PrepareAndInstallAsync(
                () => MsiLegs.AppendPreInstallEntries(context.Manifest, msiPath.Value),
                installer, control, products, msiPath.Value, minted.Value, gatewayBase,
                MsiInstallWindow, TimeSpan.FromSeconds(2), evidence, ct);
            installSucceeded = installed.IsSuccess;
        }
        finally
        {
            if (!installSucceeded)
            {
                await MsiLegs.RecordCustodyAfterFailedInstallAsync(control, new StoreInspector(), context.Manifest, evidence);
            }
        }

        if (installed.IsFailure)
        {
            return installed;
        }

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var deps = new AgentLegsDeps(
            new RegistryToken(context.Manifest),
            new StoreInspector(),
            new ServiceAgentHost(control, context.Manifest, writesEntries: false),
            new NodesAgentLegsDb(() => nodesDb.Create()),
            (label, c) => minter.MintAsync(organizationId, label, c));
        var options = new AgentLegsOptions(
            organizationId, systemSid, BuildAgentConfig(context, gatewayBase), TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(90), setup.DecoyThumbprint, AgentDirectories.MachineAgentDirectory);
        var legs = await new AgentLegs(context.Paths, context.Manifest, deps, options).RunMsiAsync(evidence, minted.Value, ct);
        if (legs.IsFailure)
        {
            return legs;
        }

        foreach (var step in new[] { "U5", "U6", "U7", "U8" })
        {
            evidence[step] = "skipped: covered by process and service modes";
        }

        return await MsiLegs.UninstallAsync(installer, control, products, new StoreInspector(), msiPath.Value, evidence);
    }

    /// <summary>The one MSI the run script copied to <c>&lt;runDir&gt;\msi\</c>.</summary>
    private static Result<string> FindMsi(RunPaths paths)
    {
        if (!Directory.Exists(paths.Msi))
        {
            return Result<string>.Failure("the msi folder is missing; the run script copies the built MSI to <runDir>\\msi");
        }

        var files = Directory.GetFiles(paths.Msi, "*.msi");
        if (files.Length != 1)
        {
            return Result<string>.Failure($"expected one MSI in the msi folder, found {files.Length}");
        }

        return paths.Validate(files[0]);
    }

    private static AgentConfig BuildAgentConfig(RunContext context, Uri gatewayBase) => new(
        gatewayBase,
        AgentHeartbeatSeconds,
        AgentConnectionCheckSeconds,
        context.Paths.AgentState,
        Path.Combine(context.Paths.AgentData, "Servers"),
        Path.Combine(context.Paths.AgentData, "Temp"));

    /// <summary>
    /// The agent legs (P20 U2-U4) after the stack smoke, then the renewal legs (P21 U5-U8). In process mode the agent is
    /// a child process; in service mode (P22) it is the <c>DhadgarAgent</c> LocalSystem service. The legs use a new
    /// organization, so the smoke's node is not counted as a node of the run's organization in U3.
    /// </summary>
    private static async Task<Result> RunAgentLegsAsync(
        RunContext context, JobObject job, NodesDb nodesDb, Uri gatewayBase, Uri nodesBase, ServiceRun? service,
        Dictionary<string, object?> evidence, CancellationToken ct)
    {
        // The service runs as LocalSystem (U10 checks it); the process agent runs as the elevated user.
        var accountSid = service is null ? CurrentUserSid() : new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        if (accountSid is null)
        {
            return Fail(evidence, "U2", "could not read the SID of the account the agent runs as");
        }

        var organizationId = Guid.NewGuid();
        var minter = new TokenMinter(() => nodesDb.Create(), context.RunId, context.Redactor);
        var config = BuildAgentConfig(context, gatewayBase);
        using var agentProcess = service is null ? new AgentProcess(context.Paths, job, context.Redactor, context.Manifest) : null;
        IAgentHost agentHost = service is null
            ? new ProcessAgentHost(context.Paths, agentProcess!)
            : new ServiceAgentHost(service.Control, context.Manifest);
        var deps = new AgentLegsDeps(
            new RegistryToken(context.Manifest),
            new StoreInspector(),
            agentHost,
            new NodesAgentLegsDb(() => nodesDb.Create()),
            (label, c) => minter.MintAsync(organizationId, label, c));
        var options = new AgentLegsOptions(
            organizationId, accountSid, config, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(60),
            service?.Setup.DecoyThumbprint);
        evidence["legsOrganizationId"] = organizationId.ToString();

        var legs = await new AgentLegs(context.Paths, context.Manifest, deps, options).RunAsync(evidence, ct);
        if (legs.IsFailure || !Guid.TryParse(evidence["nodeId"] as string, out var nodeId))
        {
            return legs.IsFailure ? legs : Fail(evidence, "U5", "the node ID from U3 is missing");
        }

        // The renewal legs run after the agent is stopped (U4 leaves it stopped), with the same host object.
        var renewalDeps = new RenewalLegsDeps(
            new StoreInspector(),
            agentHost,
            new NodesAgentLegsDb(() => nodesDb.Create()),
            new GatewayRenewalTransport(gatewayBase, nodesBase),
            OpenMachineCertificate,
            CngKeyExists,
            () => DateTime.UtcNow);
        var renewalOptions = new RenewalLegsOptions(
            organizationId,
            nodeId,
            Path.Combine(context.Paths.AgentPublish, "appsettings.json"),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(60));
        return await new RenewalLegs(context.Paths, context.Manifest, renewalDeps, renewalOptions).RunAsync(evidence, ct);
    }

    /// <summary>The SID of the account this run runs as, or null when it cannot be read.</summary>
    private static string? CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }

    /// <summary>Opens a <c>LocalMachine\My</c> certificate with its key, by its SHA-1 thumbprint (P21 U5-U7).</summary>
    private static Result<X509Certificate2> OpenMachineCertificate(string sha1Thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, sha1Thumbprint, validOnly: false);
            try
            {
                return found.Count == 1
                    ? Result<X509Certificate2>.Success(new X509Certificate2(found[0]))
                    : Result<X509Certificate2>.Failure($"[Store.My] {found.Count} certificates match the thumbprint");
            }
            finally
            {
                foreach (var match in found)
                {
                    match.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return Result<X509Certificate2>.Failure($"[Store.My] open failed: {ex.GetType().Name}");
        }
    }

    /// <summary>True when the named CNG key still exists in the machine key store (P14 A2).</summary>
    private static bool CngKeyExists(CngKeyInfo key) =>
        CngKey.Exists(key.KeyName, new CngProvider(key.Provider), CngKeyOpenOptions.MachineKey);

    private static async Task<Result> HubHeartbeatAsync(
        int gatewayPort, X509Certificate2 certificate, Dictionary<string, object?> evidence, CancellationToken ct)
    {
        var url = new Uri($"https://localhost:{gatewayPort}{AgentHubProtocol.Path}");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.ClientCertificates = [certificate];
                o.Headers[AgentHubProtocol.VersionHeader] = AgentHubProtocol.CurrentVersion;
            })
            .Build();

        using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startCts.CancelAfter(HubStartTimeout);
        try
        {
            await connection.StartAsync(startCts.Token);
            evidence["hubTransport"] = "WebSockets";
            using var invokeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            invokeCts.CancelAfter(HubStartTimeout);
            var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat), new AgentHeartbeat(1, 1, 1, 0, "uat-smoke", null), invokeCts.Token);
            evidence["hubAckServerTime"] = ack.ServerTime.ToString("O");
            return ack.Acknowledged
                ? Result.Success()
                : Result.Failure($"heartbeat was not acknowledged: {ack.ErrorCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure("hub did not start or answer the heartbeat within 30 s");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                       or System.Net.WebSockets.WebSocketException or HubException)
        {
            return Result.Failure($"hub WebSocket connection failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<int> PostNegotiateAsync(Uri uri, X509Certificate2? certificate, CancellationToken ct)
    {
        using var client = GatewayClients.Create(certificate, TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(AgentHubProtocol.VersionHeader, AgentHubProtocol.CurrentVersion);
        using var response = await client.SendAsync(request, ct);
        return (int)response.StatusCode;
    }

    private static async Task<string> ImageIdAsync(string container, CancellationToken ct)
    {
        var result = await CommandRunner.RunAsync(
            "docker", ["inspect", "--format", "{{.Image}}", container], TimeSpan.FromSeconds(30), null, null, ct);
        return result.IsSuccess && result.Value.ExitCode == 0 ? result.Value.StandardOutput.Trim() : "unknown";
    }

    private static long DiskBytes()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory)!).TotalSize;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static Result Fail(Dictionary<string, object?> evidence, string step, string message)
    {
        evidence[step] = "fail";
        return Result.Failure($"{step}: {message}");
    }
}
