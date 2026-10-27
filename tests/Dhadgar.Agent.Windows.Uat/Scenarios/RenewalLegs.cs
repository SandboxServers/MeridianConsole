using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>
/// The HTTP probes the renewal legs make. Every call is a new handler. A <see cref="Result"/> failure whose text starts
/// with <c>[Probe.Transport]</c> is a TLS or socket failure, which is inconclusive and never a pass (P21).
/// </summary>
internal interface IRenewalTransport
{
    /// <summary>POST <c>api/v1/agents/{nodeId}/certificates/renew</c> through the Gateway, presenting <paramref name="presented"/>.</summary>
    Task<Result<RenewCertificateResponse>> RenewAsync(Guid nodeId, X509Certificate2 presented, string publicKey, CancellationToken ct);

    /// <summary>POST negotiate through the Gateway with a client certificate; the HTTP status code.</summary>
    Task<Result<int>> GatewayNegotiateAsync(X509Certificate2 certificate, CancellationToken ct);

    /// <summary>POST negotiate straight to Nodes from loopback, the certificate in the forwarded header (a trusted proxy).</summary>
    Task<Result<int>> NodesNegotiateForwardedAsync(X509Certificate2 certificate, CancellationToken ct);
}

/// <summary>The production probes: the Gateway over mTLS and Nodes over loopback. Only loopback hosts are called.</summary>
internal sealed class GatewayRenewalTransport : IRenewalTransport
{
    private const string NegotiatePath = "negotiate?negotiateVersion=1";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _gatewayBase;
    private readonly Uri _nodesBase;

    /// <summary>Creates the transport over the stack's gateway and Nodes base addresses.</summary>
    public GatewayRenewalTransport(Uri gatewayBase, Uri nodesBase)
    {
        _gatewayBase = gatewayBase;
        _nodesBase = nodesBase;
    }

    /// <inheritdoc />
    public async Task<Result<RenewCertificateResponse>> RenewAsync(
        Guid nodeId, X509Certificate2 presented, string publicKey, CancellationToken ct)
    {
        var uri = new Uri(_gatewayBase, $"api/v1/agents/{nodeId:D}/certificates/renew");
        var loopback = RequireLoopback(uri);
        if (loopback.IsFailure)
        {
            return Result<RenewCertificateResponse>.Failure(loopback.Error!);
        }

        try
        {
            using var client = GatewayClients.Create(presented, RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new RenewCertificateRequest(publicKey)),
            };
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<RenewCertificateResponse>.Failure($"[Renewal.Rejected] {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<RenewCertificateResponse>(ct);
            return body is null
                ? Result<RenewCertificateResponse>.Failure("[Renewal.Empty] the renew response has no body")
                : Result<RenewCertificateResponse>.Success(body);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return Result<RenewCertificateResponse>.Failure($"[Probe.Transport] {Describe(ex)}");
        }
    }

    /// <inheritdoc />
    public Task<Result<int>> GatewayNegotiateAsync(X509Certificate2 certificate, CancellationToken ct) =>
        NegotiateAsync(new Uri(_gatewayBase, AgentHubProtocol.Path.TrimStart('/') + "/" + NegotiatePath), certificate, null, ct);

    /// <inheritdoc />
    public Task<Result<int>> NodesNegotiateForwardedAsync(X509Certificate2 certificate, CancellationToken ct) =>
        NegotiateAsync(
            new Uri(_nodesBase, AgentHubProtocol.Path.TrimStart('/') + "/" + NegotiatePath),
            null,
            Convert.ToBase64String(certificate.RawData),
            ct);

    private static async Task<Result<int>> NegotiateAsync(
        Uri uri, X509Certificate2? certificate, string? forwardedCertificate, CancellationToken ct)
    {
        var loopback = RequireLoopback(uri);
        if (loopback.IsFailure)
        {
            return Result<int>.Failure(loopback.Error!);
        }

        try
        {
            using var client = GatewayClients.Create(certificate, RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AgentHubProtocol.VersionHeader, AgentHubProtocol.CurrentVersion);
            if (forwardedCertificate is not null)
            {
                request.Headers.Add(AgentCertificateProfile.ForwardedCertificateHeader, forwardedCertificate);
            }

            using var response = await client.SendAsync(request, ct);
            return Result<int>.Success((int)response.StatusCode);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return Result<int>.Failure($"[Probe.Transport] {Describe(ex)}");
        }
    }

    private static Result RequireLoopback(Uri uri) =>
        uri.Host is "localhost" or "127.0.0.1"
            ? Result.Success()
            : Result.Failure("[Probe.Host] the UAT probes call loopback addresses only");

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or IOException or TaskCanceledException
            or System.Net.Sockets.SocketException;

    private static string Describe(Exception ex) =>
        ex.InnerException is null ? ex.GetType().Name : $"{ex.GetType().Name} (inner {ex.InnerException.GetType().Name})";
}

/// <summary>Everything the renewal legs touch besides the run paths and the manifest.</summary>
/// <param name="Store">The certificate stores (read only here).</param>
/// <param name="Agent">The agent process.</param>
/// <param name="Db">The database reads.</param>
/// <param name="Transport">The HTTP probes.</param>
/// <param name="OpenMachineCertificate">Opens a <c>LocalMachine\My</c> certificate with its key, by SHA-1 thumbprint.</param>
/// <param name="CngKeyExists">True when the named CNG key still exists in the machine store.</param>
/// <param name="UtcNow">The clock (UTC).</param>
internal sealed record RenewalLegsDeps(
    IStoreInspector Store,
    IAgentProcess Agent,
    IAgentLegsDb Db,
    IRenewalTransport Transport,
    Func<string, Result<X509Certificate2>> OpenMachineCertificate,
    Func<CngKeyInfo, bool> CngKeyExists,
    Func<DateTime> UtcNow);

/// <summary>The renewal leg settings.</summary>
/// <param name="OrganizationId">The run's organization.</param>
/// <param name="NodeId">The node the agent enrolled (from U3).</param>
/// <param name="AppSettingsPath">The published agent <c>appsettings.json</c>.</param>
/// <param name="PollInterval">Delay between condition polls (2 s in a real run).</param>
/// <param name="ConvergeTimeout">How long U6 polls for the renewal (90 s in a real run).</param>
/// <param name="RestartTimeout">How long U8 polls for the restart (60 s in a real run).</param>
internal sealed record RenewalLegsOptions(
    Guid OrganizationId,
    Guid NodeId,
    string AppSettingsPath,
    TimeSpan PollInterval,
    TimeSpan ConvergeTimeout,
    TimeSpan RestartTimeout);

/// <summary>
/// The renewal legs after the agent legs (P21 U5-U8): a renewal whose response is lost, the agent's own renewal from
/// grace, the grace window over the wire, and a restart after renewal. C1 is the agent's certificate after U4, C2 the
/// orphan this driver creates, C3 the agent's renewal. Every machine change is preceded by its manifest entry.
/// </summary>
internal sealed class RenewalLegs
{
    /// <summary>The renewal threshold U6 sets, in days (<c>CertificateRenewalThresholdDays</c>), so the agent renews at start.</summary>
    internal const int RenewalThresholdDays = 90;

    private const string SupersededReason = "Superseded by a later renewal";
    private const string ProbeTransportPrefix = "[Probe.Transport]";
    private const int HttpOk = 200;
    private const int HttpUnauthorized = 401;
    private static readonly TimeSpan ExpectedGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GraceTolerance = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GraceExpiryMargin = TimeSpan.FromSeconds(10);

    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly RenewalLegsDeps _deps;
    private readonly RenewalLegsOptions _options;
    private readonly HashSet<string> _recordedThumbprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _owned = [];

    /// <summary>Creates the renewal legs for one run.</summary>
    public RenewalLegs(RunPaths paths, CleanupManifest manifest, RenewalLegsDeps deps, RenewalLegsOptions options)
    {
        _paths = paths;
        _manifest = manifest;
        _deps = deps;
        _options = options;
    }

    /// <summary>Runs U5-U8 in order and stops at the first failing step. Writes <c>U5</c>-<c>U8</c> and evidence into <paramref name="evidence"/>.</summary>
    public async Task<Result> RunAsync(Dictionary<string, object?> evidence, CancellationToken ct)
    {
        var state = new RenewalState(evidence);
        try
        {
            var u5 = await U5LostResponseAsync(state, ct);
            if (u5.IsFailure)
            {
                return u5;
            }

            var u6 = await U6RenewFromGraceAsync(state, ct);
            if (u6.IsFailure)
            {
                return u6;
            }

            var u7 = await U7GraceOverWireAsync(state, ct);
            if (u7.IsFailure)
            {
                return u7;
            }

            return await U8RestartAsync(state, ct);
        }
        finally
        {
            foreach (var disposable in _owned)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<Result> U5LostResponseAsync(RenewalState state, CancellationToken ct)
    {
        var mine = _deps.Store.AgentCertificatesInMy();
        if (mine.Count != 1 || mine[0].Key is not { IsMachineKey: true, CanSign: true })
        {
            return Fail(state.Evidence, "U5", "exactly one agent certificate with a machine CNG key is in LocalMachine\\My (C1)");
        }

        var c1Store = mine[0];
        var opened = _deps.OpenMachineCertificate(c1Store.Sha1Thumbprint);
        if (opened.IsFailure)
        {
            return Fail(state.Evidence, "U5", opened.Error!);
        }

        var c1 = Track(opened.Value);
        if (!c1.HasPrivateKey)
        {
            return Fail(state.Evidence, "U5", "C1 was opened without its private key");
        }

        state.C1 = c1;
        state.C1Sha256 = c1Store.Sha256Thumbprint;
        state.C1Key = c1Store.Key;

        var k2 = Track(ECDsa.Create(ECCurve.NamedCurves.nistP384));
        var publicKey = Convert.ToBase64String(k2.ExportSubjectPublicKeyInfo());
        var renewed = await _deps.Transport.RenewAsync(_options.NodeId, c1, publicKey, ct);
        if (renewed.IsFailure)
        {
            return FailProbe(state.Evidence, "U5", renewed.Error!);
        }

        var c2 = TestKeyCertificate.FromPem(k2, renewed.Value.Certificate);
        if (c2.IsFailure)
        {
            return Fail(state.Evidence, "U5", c2.Error!);
        }

        var c2Certificate = Track(c2.Value);
        var c2Sha256 = Convert.ToHexString(SHA256.HashData(c2Certificate.RawData));
        if (!string.Equals(c2Sha256, renewed.Value.CertificateThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(state.Evidence, "U5", "the renew response thumbprint does not match the certificate it carries");
        }

        state.C2 = c2Certificate;
        state.C2Sha256 = c2Sha256;

        var nodeId = _options.NodeId;
        var rows = await _deps.Db.CertificatesForNodeAsync(nodeId, ct);
        if (rows.IsFailure)
        {
            return Fail(state.Evidence, "U5", rows.Error!);
        }

        var c1Row = FindRow(rows.Value, state.C1Sha256);
        if (c1Row is not { IsRevoked: true, GraceUntil: { } grace })
        {
            return Fail(state.Evidence, "U5", "C1's row is revoked with a GraceUntil");
        }

        if ((grace - (_deps.UtcNow() + ExpectedGrace)).Duration() > GraceTolerance)
        {
            return Fail(state.Evidence, "U5", "C1's GraceUntil is about 5 minutes after the renewal (within 60 s)");
        }

        var c2Row = FindRow(rows.Value, c2Sha256);
        if (c2Row is not { IsRevoked: false })
        {
            return Fail(state.Evidence, "U5", "C2's row is active");
        }

        state.C1GraceUntil = grace;
        state.Evidence["U5.graceUntil"] = grace.ToString("O");

        var probeC1 = await _deps.Transport.GatewayNegotiateAsync(c1, ct);
        if (probeC1.IsFailure)
        {
            return FailProbe(state.Evidence, "U5", probeC1.Error!);
        }

        var probeC2 = await _deps.Transport.GatewayNegotiateAsync(c2Certificate, ct);
        if (probeC2.IsFailure)
        {
            return FailProbe(state.Evidence, "U5", probeC2.Error!);
        }

        state.Evidence["U5.negotiateC1"] = probeC1.Value;
        state.Evidence["U5.negotiateC2"] = probeC2.Value;
        if (probeC1.Value != HttpOk || probeC2.Value != HttpOk)
        {
            return Fail(state.Evidence, "U5", $"negotiate with C1 and C2 through the Gateway must return 200 (got {probeC1.Value} and {probeC2.Value})");
        }

        state.Evidence["U5"] = "pass";
        return Result.Success();
    }

    private async Task<Result> U6RenewFromGraceAsync(RenewalState state, CancellationToken ct)
    {
        var appSettings = new AgentPublishConfig(_paths);
        Result body = Result.Success();
        try
        {
            var set = appSettings.SetRenewalThreshold(_options.AppSettingsPath, RenewalThresholdDays);
            if (set.IsFailure)
            {
                body = Fail(state.Evidence, "U6", set.Error!);
            }
            else
            {
                body = await U6BodyAsync(state, ct);
            }
        }
        finally
        {
            var stopped = await _deps.Agent.StopAsync();
            if (stopped.IsFailure)
            {
                state.Evidence["U6.agentStop"] = stopped.Error!;
            }

            var reset = appSettings.SetRenewalThreshold(_options.AppSettingsPath, null);
            if (reset.IsFailure && body.IsSuccess)
            {
                body = Fail(state.Evidence, "U6", $"the renewal threshold key could not be removed: {reset.Error}");
            }
        }

        return body;
    }

    private async Task<Result> U6BodyAsync(RenewalState state, CancellationToken ct)
    {
        var node = await NodeAsync(ct);
        if (node.IsFailure)
        {
            return Fail(state.Evidence, "U6", node.Error!);
        }

        if (node.Value.LastHeartbeat is not { } baseline)
        {
            return Fail(state.Evidence, "U6", "no LastHeartbeat baseline for the node");
        }

        var started = _deps.Agent.Start();
        if (started.IsFailure)
        {
            return Fail(state.Evidence, "U6", started.Error!);
        }

        var polled = await PollAsync(
            "U6", _options.ConvergeTimeout, token => EvaluateRenewalAsync(state, baseline, token), state.Evidence, ct);
        if (polled.IsFailure)
        {
            return polled;
        }

        state.Evidence["U6"] = "pass";
        return Result.Success();
    }

    private async Task<Result<Check>> EvaluateRenewalAsync(RenewalState state, DateTime baseline, CancellationToken ct)
    {
        var mine = _deps.Store.AgentCertificatesInMy();
        var recorded = RecordMachineCertificates(mine);
        if (recorded.IsFailure)
        {
            return Result<Check>.Failure(recorded.Error!);
        }

        if (mine.Count != 1)
        {
            return Unmet("exactly one agent certificate is in LocalMachine\\My");
        }

        var c3 = mine[0];
        if (string.Equals(c3.Sha256Thumbprint, state.C1Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return Unmet("the certificate in LocalMachine\\My is not C1");
        }

        if (!c3.HasPrivateKey || c3.Key is not { IsMachineKey: true, CanSign: true })
        {
            return Unmet("C3 has a machine CNG key that can sign");
        }

        if (state.C1Key is null || _deps.CngKeyExists(state.C1Key))
        {
            return Unmet("C1's CNG key no longer exists (P14 A2)");
        }

        var node = await NodeAsync(ct);
        if (node.IsFailure)
        {
            return Result<Check>.Failure(node.Error!);
        }

        if (node.Value.LastHeartbeat is not { } heartbeat || heartbeat <= baseline)
        {
            return Unmet("LastHeartbeat is later than the baseline");
        }

        var rows = await _deps.Db.CertificatesForNodeAsync(_options.NodeId, ct);
        if (rows.IsFailure)
        {
            return Result<Check>.Failure(rows.Error!);
        }

        var c3Row = FindRow(rows.Value, c3.Sha256Thumbprint);
        if (c3Row is not { IsRevoked: false })
        {
            return Unmet("C3's row is active");
        }

        var c2Row = FindRow(rows.Value, state.C2Sha256);
        if (c2Row is not { IsRevoked: true, GraceUntil: null, RevocationReason: SupersededReason })
        {
            return Unmet("C2's row is hard-revoked with no grace and the reason \"Superseded by a later renewal\"");
        }

        var c1Row = FindRow(rows.Value, state.C1Sha256);
        if (c1Row is null || c1Row.GraceUntil != state.C1GraceUntil)
        {
            return Unmet("C1's GraceUntil is unchanged from U5");
        }

        state.C3 = c3;
        state.Evidence["U6.thumbprint"] = c3.Sha256Thumbprint;
        state.Evidence["U6.lastHeartbeat"] = heartbeat.ToString("O");
        return Result<Check>.Success(Check.Holds);
    }

    private async Task<Result> U7GraceOverWireAsync(RenewalState state, CancellationToken ct)
    {
        var c2 = await _deps.Transport.GatewayNegotiateAsync(state.C2!, ct);
        if (c2.IsFailure)
        {
            return FailProbe(state.Evidence, "U7", c2.Error!);
        }

        state.Evidence["U7.c2Gateway"] = c2.Value;
        if (c2.Value != HttpUnauthorized)
        {
            return Fail(state.Evidence, "U7", $"C2 through the Gateway must return 401 (hard revoked), got {c2.Value}");
        }

        if (state.C3 is null)
        {
            return Fail(state.Evidence, "U7", "C3 is not known");
        }

        var opened = _deps.OpenMachineCertificate(state.C3.Sha1Thumbprint);
        if (opened.IsFailure)
        {
            return Fail(state.Evidence, "U7", opened.Error!);
        }

        var c3 = Track(opened.Value);
        var c3Probe = await _deps.Transport.GatewayNegotiateAsync(c3, ct);
        if (c3Probe.IsFailure)
        {
            return FailProbe(state.Evidence, "U7", c3Probe.Error!);
        }

        state.Evidence["U7.c3Gateway"] = c3Probe.Value;
        if (c3Probe.Value != HttpOk)
        {
            return Fail(state.Evidence, "U7", $"C3 through the Gateway must return 200, got {c3Probe.Value}");
        }

        var deadline = state.C1GraceUntil!.Value + GraceExpiryMargin;
        state.Evidence["U7.waitUntil"] = deadline.ToString("O");
        while (_deps.UtcNow() < deadline)
        {
            await Task.Delay(_options.PollInterval, ct);
        }

        var c1 = await _deps.Transport.NodesNegotiateForwardedAsync(state.C1!, ct);
        if (c1.IsFailure)
        {
            return FailProbe(state.Evidence, "U7", c1.Error!);
        }

        state.Evidence["U7.c1NodesDirect"] = c1.Value;
        state.Evidence["U7.c1Path"] = "Nodes direct, loopback, X-Client-Cert (C1's key is gone; the Gateway path proved C1 in U5)";
        if (c1.Value != HttpUnauthorized)
        {
            return Fail(state.Evidence, "U7", $"C1 after its grace must return 401 from Nodes, got {c1.Value}");
        }

        state.Evidence["U7"] = "pass";
        return Result.Success();
    }

    private async Task<Result> U8RestartAsync(RenewalState state, CancellationToken ct)
    {
        var node = await NodeAsync(ct);
        if (node.IsFailure)
        {
            return Fail(state.Evidence, "U8", node.Error!);
        }

        if (node.Value.LastHeartbeat is not { } baseline)
        {
            return Fail(state.Evidence, "U8", "no LastHeartbeat baseline for the node");
        }

        var rows = await _deps.Db.CertificatesForNodeAsync(_options.NodeId, ct);
        if (rows.IsFailure)
        {
            return Fail(state.Evidence, "U8", rows.Error!);
        }

        var rowsBefore = rows.Value.Count;
        state.Evidence["U8.certificateRowsBefore"] = rowsBefore;

        var started = _deps.Agent.Start();
        if (started.IsFailure)
        {
            return Fail(state.Evidence, "U8", started.Error!);
        }

        Result polled;
        try
        {
            polled = await PollAsync(
                "U8", _options.RestartTimeout, token => EvaluateRestartAsync(state, baseline, rowsBefore, token), state.Evidence, ct);
        }
        finally
        {
            var stopped = await _deps.Agent.StopAsync();
            if (stopped.IsFailure)
            {
                state.Evidence["U8.agentStop"] = stopped.Error!;
            }
        }

        if (polled.IsFailure)
        {
            return polled;
        }

        state.Evidence["U8"] = "pass";
        return Result.Success();
    }

    private async Task<Result<Check>> EvaluateRestartAsync(RenewalState state, DateTime baseline, int rowsBefore, CancellationToken ct)
    {
        var mine = _deps.Store.AgentCertificatesInMy();
        var recorded = RecordMachineCertificates(mine);
        if (recorded.IsFailure)
        {
            return Result<Check>.Failure(recorded.Error!);
        }

        var node = await NodeAsync(ct);
        if (node.IsFailure)
        {
            return Result<Check>.Failure(node.Error!);
        }

        if (node.Value.LastHeartbeat is not { } heartbeat || heartbeat <= baseline)
        {
            return Unmet("LastHeartbeat is later than the baseline");
        }

        state.Evidence["U8.heartbeatAfter"] = heartbeat.ToString("O");
        var rows = await _deps.Db.CertificatesForNodeAsync(_options.NodeId, ct);
        if (rows.IsFailure)
        {
            return Result<Check>.Failure(rows.Error!);
        }

        if (rows.Value.Count != rowsBefore)
        {
            return Unmet("the certificate row count is unchanged");
        }

        if (state.C3 is null || mine.Count != 1 || !string.Equals(mine[0].Sha256Thumbprint, state.C3.Sha256Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return Unmet("only C3 is in LocalMachine\\My");
        }

        return Result<Check>.Success(Check.Holds);
    }

    private async Task<Result<NodeRow>> NodeAsync(CancellationToken ct)
    {
        var nodes = await _deps.Db.NodesForOrganizationAsync(_options.OrganizationId, ct);
        if (nodes.IsFailure)
        {
            return Result<NodeRow>.Failure(nodes.Error!);
        }

        var node = nodes.Value.FirstOrDefault(n => n.Id == _options.NodeId);
        return node is null
            ? Result<NodeRow>.Failure("[Db.Nodes] the node is not in the organization")
            : Result<NodeRow>.Success(node);
    }

    /// <summary>One poll result: the condition that is still unmet, or null when every condition holds.</summary>
    private sealed record Check(string? Unmet)
    {
        public static Check Holds { get; } = new((string?)null);
    }

    private static Result<Check> Unmet(string condition) => Result<Check>.Success(new Check(condition));

    /// <summary>
    /// Polls <paramref name="evaluate"/> until it reports no unmet condition, a failure, or the timeout. The timeout
    /// fails naming the condition that was unmet last.
    /// </summary>
    private async Task<Result> PollAsync(
        string step,
        TimeSpan timeout,
        Func<CancellationToken, Task<Result<Check>>> evaluate,
        Dictionary<string, object?> evidence,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var check = await evaluate(ct);
            if (check.IsFailure)
            {
                return Fail(evidence, step, check.Error!);
            }

            if (check.Value.Unmet is null)
            {
                return Result.Success();
            }

            if (clock.Elapsed >= timeout)
            {
                return Fail(evidence, step, $"{check.Value.Unmet} did not hold within {timeout.TotalSeconds:0} s");
            }

            await Task.Delay(_options.PollInterval, ct);
        }
    }

    /// <summary>Appends the <c>certificate</c> entry for every agent certificate not yet recorded (D-S238).</summary>
    private Result RecordMachineCertificates(IReadOnlyList<StoreCert> certificates)
    {
        foreach (var certificate in certificates)
        {
            if (!_recordedThumbprints.Add(certificate.Sha1Thumbprint))
            {
                continue;
            }

            foreach (var entry in AgentLegs.CertificateEntries(certificate))
            {
                var appended = _manifest.Append(entry);
                if (appended.IsFailure)
                {
                    return appended;
                }
            }
        }

        return Result.Success();
    }

    private static CertRow? FindRow(IReadOnlyList<CertRow> rows, string sha256) =>
        rows.FirstOrDefault(row => string.Equals(row.Thumbprint, sha256, StringComparison.OrdinalIgnoreCase));

    private T Track<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    private static Result Fail(Dictionary<string, object?> evidence, string step, string message)
    {
        evidence[step] = "fail";
        return Result.Failure($"{step}: {message}");
    }

    /// <summary>A probe that never got an HTTP answer is inconclusive, not a failure of the rule under test (P21).</summary>
    private static Result FailProbe(Dictionary<string, object?> evidence, string step, string error)
    {
        evidence[step] = error.StartsWith(ProbeTransportPrefix, StringComparison.Ordinal) ? "inconclusive" : "fail";
        return Result.Failure($"{step}: {error}");
    }

    /// <summary>Mutable state shared by the steps of one run.</summary>
    private sealed class RenewalState(Dictionary<string, object?> evidence)
    {
        public Dictionary<string, object?> Evidence { get; } = evidence;

        public X509Certificate2? C1 { get; set; }

        public string C1Sha256 { get; set; } = string.Empty;

        public CngKeyInfo? C1Key { get; set; }

        public DateTime? C1GraceUntil { get; set; }

        public X509Certificate2? C2 { get; set; }

        public string C2Sha256 { get; set; } = string.Empty;

        public StoreCert? C3 { get; set; }
    }
}
