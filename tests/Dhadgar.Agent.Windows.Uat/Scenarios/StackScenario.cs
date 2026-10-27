using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
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
    private const int AgentHeartbeatSeconds = 10;
    private const int AgentConnectionCheckSeconds = 10;

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
        var settings = new StackSettings(
            postgresPort, FreePorts.RabbitMqPort, nodesPort, gatewayPort,
            Secrets.GeneratePassword(redactor), Secrets.GeneratePassword(redactor), Secrets.GeneratePassword(redactor));
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
        var gatewayBase = ServiceHost.GatewayBase(gatewayPort);
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
        if (context.Mode != "process")
        {
            return Result.Success();
        }

        return await RunAgentLegsAsync(context, job, nodesDb, gatewayBase, evidence, ct);
    }

    /// <summary>
    /// The process-mode agent legs (P20 U2-U4) after the stack smoke. The legs use a new organization, so the smoke's
    /// node is not counted as a node of the run's organization in U3.
    /// </summary>
    private static async Task<Result> RunAgentLegsAsync(
        RunContext context, JobObject job, NodesDb nodesDb, Uri gatewayBase, Dictionary<string, object?> evidence, CancellationToken ct)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var accountSid = identity.User?.Value;
        if (accountSid is null)
        {
            return Fail(evidence, "U2", "could not read the SID of the account the agent runs as");
        }

        var organizationId = Guid.NewGuid();
        var minter = new TokenMinter(() => nodesDb.Create(), context.RunId, context.Redactor);
        var config = new AgentConfig(
            gatewayBase,
            AgentHeartbeatSeconds,
            AgentConnectionCheckSeconds,
            context.Paths.AgentState,
            Path.Combine(context.Paths.AgentData, "Servers"),
            Path.Combine(context.Paths.AgentData, "Temp"));
        var deps = new AgentLegsDeps(
            new RegistryToken(context.Manifest),
            new StoreInspector(),
            new AgentProcess(context.Paths, job, context.Redactor, context.Manifest),
            new NodesAgentLegsDb(() => nodesDb.Create()),
            (label, c) => minter.MintAsync(organizationId, label, c));
        var options = new AgentLegsOptions(
            organizationId, accountSid, config, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(60));
        evidence["legsOrganizationId"] = organizationId.ToString();

        return await new AgentLegs(context.Paths, context.Manifest, deps, options).RunAsync(evidence, ct);
    }

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
