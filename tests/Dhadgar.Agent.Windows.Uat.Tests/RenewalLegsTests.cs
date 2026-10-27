using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The renewal legs U5-U8 against <see cref="RenewalWorld"/>: the machine store, the database, the transport and the agent
/// are fakes, so every step's pass and failure rule is exercised without a real store or Gateway.
/// </summary>
public sealed class RenewalLegsTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-renew-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly RenewalWorld _world;

    public RenewalLegsTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        _world = new RenewalWorld(_paths);
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

    private async Task<(Result Result, Dictionary<string, object?> Evidence)> RunLegsAsync()
    {
        var evidence = new Dictionary<string, object?>();
        var result = await _world.Legs().RunAsync(evidence, CancellationToken.None);
        return (result, evidence);
    }

    private static void AssertFailedAt(Result result, Dictionary<string, object?> evidence, string step, string contains)
    {
        Assert.True(result.IsFailure);
        Assert.StartsWith(step + ":", result.Error!, StringComparison.Ordinal);
        Assert.Contains(contains, result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence[step]?.ToString());
    }

    [Fact]
    public async Task RunAsync_WhenEveryStepHolds_PassesU5ToU8()
    {
        var (result, evidence) = await RunLegsAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pass", evidence["U5"]?.ToString());
        Assert.Equal("pass", evidence["U6"]?.ToString());
        Assert.Equal("pass", evidence["U7"]?.ToString());
        Assert.Equal("pass", evidence["U8"]?.ToString());
        Assert.Equal(2, _world.Starts);
        Assert.Equal(_world.C3Sha256, Assert.Single(_world.My).Sha256Thumbprint);
    }

    [Fact]
    public async Task RunAsync_StartsTheAgentOnlyAfterTheThresholdIsSet()
    {
        var (result, _) = await RunLegsAsync();

        Assert.True(result.IsSuccess, result.Error);
        // U6 starts with 90 days (renew at start); U8 starts after the key was removed again.
        Assert.Equal(new int?[] { 90, null }, _world.ThresholdsAtStart);
    }

    [Fact]
    public async Task RunAsync_U5_FailsWhenC1GraceUntilIsNull()
    {
        _world.C1GraceOffset = null;

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U5", "GraceUntil");
    }

    [Fact]
    public async Task RunAsync_U5_FailsWhenC1GraceIsOffByMoreThan60Seconds()
    {
        _world.C1GraceOffset = TimeSpan.FromMinutes(7);

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U5", "within 60 s");
    }

    [Fact]
    public async Task RunAsync_U6_FailsWhenTwoCertificatesRemainInMy()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.RestoreC1ToMy();
        };

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U6", "exactly one agent certificate");
    }

    [Fact]
    public async Task RunAsync_U6_FailsWhenC2IsNotHardRevoked()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.SetRow(w.C2Sha256, isRevoked: false, grace: null, reason: null);
        };

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U6", "C2's row is hard-revoked");
    }

    [Fact]
    public async Task RunAsync_U6_FailsWhenC1GraceMoved()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.SetRow(w.C1Sha256, isRevoked: true, grace: w.Now.AddMinutes(9), reason: null);
        };

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U6", "C1's GraceUntil is unchanged");
    }

    [Fact]
    public async Task RunAsync_U6_FailsWhenC1KeyStillExists()
    {
        _world.KeyStillExists = true;

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U6", "C1's CNG key no longer exists");
    }

    [Fact]
    public async Task RunAsync_U7_MapsAProbeTransportErrorToInconclusive()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.GatewayFailure = "[Probe.Transport] connection reset by peer";
        };

        var (result, evidence) = await RunLegsAsync();

        Assert.True(result.IsFailure);
        Assert.StartsWith("U7:", result.Error!, StringComparison.Ordinal);
        Assert.Equal("inconclusive", evidence["U7"]?.ToString());
    }

    [Fact]
    public async Task RunAsync_U7_FailsWhenC1Gets200AfterItsGrace()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.NodesStatus = 200;
        };

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U7", "C1 after its grace must return 401");
    }

    [Fact]
    public async Task RunAsync_U7_FailsWhenNodesRefusesC1InsideItsGrace()
    {
        _world.OnFirstStart = w =>
        {
            w.AgentRenewsFromGrace();
            w.NodesStatus = 401;
        };

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U7", "inside its grace");
    }

    [Fact]
    public async Task RunAsync_RecordsACertificateStoredAfterTheLastPoll_AndCleanupUndoesIt()
    {
        // The agent never renews during U6, so every poll times out; C3 appears only when the agent stops (after the last poll).
        _world.KeyStillExists = true;
        _world.OnFirstStart = _ => { };
        _world.OnStop = w => w.AgentRenewsFromGrace();

        var (result, _) = await RunLegsAsync();
        Assert.True(result.IsFailure);

        var c3Sha1 = Assert.Single(_world.My).Sha1Thumbprint;
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry certificate
            && string.Equals(certificate.ThumbprintSha1, c3Sha1, StringComparison.OrdinalIgnoreCase));

        var report = await new CleanupEngine(new RecordingActions(), _paths).UndoAsync(loaded.Value, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Equal(loaded.Value.Count, report.Undone);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringU8_StillRecordsACertificateStoredAfterTheStop()
    {
        // Security N1, U8 side: Ctrl+C while U8 polls. The poll is cancelled, and the stop in the finally must still complete
        // (CancellationToken.None), so the certificate the agent stores while it stops is recorded before the run ends.
        // OnStop also runs at U6's stop, so the certificate in My when U8 starts is captured: the stop must replace it.
        using var cts = new CancellationTokenSource();
        string? inMyAtU8Start = null;
        _world.OnRestart = w =>
        {
            inMyAtU8Start = w.My.Count == 1 ? w.My[0].Sha1Thumbprint : null;
            w.Rows.Add(new CertRow(Guid.NewGuid(), "0000000000000000", false));
            cts.Cancel();
        };
        _world.OnStop = w => w.AgentRenewsFromGrace();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _world.Legs().RunAsync(new Dictionary<string, object?>(), cts.Token));

        // The same certificate still alone in My means U8's stop was aborted before the agent stored its certificate.
        var stored = Assert.Single(_world.My).Sha1Thumbprint;
        Assert.NotNull(inMyAtU8Start);
        Assert.NotEqual(inMyAtU8Start, stored);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry certificate
            && string.Equals(certificate.ThumbprintSha1, stored, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringU6_StillRecordsACertificateStoredAfterTheStop()
    {
        // Security N1, U6 side: Ctrl+C while U6 polls. The agent does not renew on start, so the first poll is unmet and its
        // delay throws. The stop in U6's finally must still complete (CancellationToken.None), so the certificate the agent
        // stores while it stops replaces C1 in My and is recorded before the run ends.
        using var cts = new CancellationTokenSource();
        _world.OnFirstStart = _ => cts.Cancel();
        _world.OnStop = w => w.AgentRenewsFromGrace();
        var c1Sha1 = _world.C1Store!.Sha1Thumbprint;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _world.Legs().RunAsync(new Dictionary<string, object?>(), cts.Token));

        // C1 still alone in My means U6's stop was aborted before the agent stored its certificate.
        var stored = Assert.Single(_world.My).Sha1Thumbprint;
        Assert.NotEqual(c1Sha1, stored);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry certificate
            && string.Equals(certificate.ThumbprintSha1, stored, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RemovesTheThresholdKey_EvenWhenStoppingTheAgentThrows()
    {
        _world.StopThrows = true;

        var evidence = new Dictionary<string, object?>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _world.Legs().RunAsync(evidence, CancellationToken.None));

        var json = JsonNode.Parse(await File.ReadAllTextAsync(_world.AppSettingsPath))!;
        Assert.Null(json["Agent"]?["Security"]?["CertificateRenewalThresholdDays"]);
        Assert.Equal(1, json["Agent"]?["Security"]?["Other"]?.GetValue<int>());
    }

    [Fact]
    public async Task RunAsync_U8_FailsWhenANewRowAppears()
    {
        _world.OnRestart = w => w.Rows.Add(new CertRow(Guid.NewGuid(), "0000000000000000", false));

        var (result, evidence) = await RunLegsAsync();

        AssertFailedAt(result, evidence, "U8", "the certificate row count is unchanged");
    }

    [Fact]
    public async Task RunAsync_EveryEntryItAppends_IsUndoneByTheRealCleanupEngine()
    {
        var (result, _) = await RunLegsAsync();
        Assert.True(result.IsSuccess, result.Error);

        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value, entry => entry is CertificateEntry);

        var report = await new CleanupEngine(new RecordingActions(), _paths).UndoAsync(loaded.Value, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Equal(loaded.Value.Count, report.Undone);
    }
}

/// <summary>
/// One fake machine, database, transport and agent for the renewal legs. C1 is the agent's certificate before U5.
/// The Nodes side of U5 revokes C1 with a grace and creates C2 (active). The agent's first start is its renewal from
/// grace (C3 replaces C1 in My, C2 is hard-revoked); later starts only refresh the heartbeat.
/// </summary>
internal sealed class RenewalWorld : IStoreInspector, IAgentHost, IAgentLegsDb, IRenewalTransport, IDisposable
{
    private const string SupersededReason = "Superseded by a later renewal";
    private const string KeyProvider = "Microsoft Software Key Storage Provider";
    private const int AgentPid = 4242;
    private const int HttpOk = 200;
    private const int HttpUnauthorized = 401;

    private readonly ECDsa _caKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    private readonly X509Certificate2 _ca;
    private readonly string _caPem;
    private readonly Dictionary<string, (X509Certificate2 Certificate, ECDsa Key)> _machine = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _now;
    private bool _running;

    /// <summary>Creates the world for one run and installs C1 in <c>LocalMachine\My</c>.</summary>
    public RenewalWorld(RunPaths paths)
    {
        Paths = paths;
        Directory.CreateDirectory(paths.AgentPublish);
        AppSettingsPath = Path.Combine(paths.AgentPublish, "appsettings.json");
        File.WriteAllText(AppSettingsPath, """
            {
              "Agent": { "Security": { "CertificateRenewalThresholdDays": 30, "Other": 1 } }
            }
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Manifest)!);
        Manifest = new CleanupManifest(paths.Manifest);

        _now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        LastHeartbeat = _now.AddHours(-1);
        _ca = SelfSigned("CN=uat-renewal-ca", _caKey, certificateAuthority: true);
        _caPem = _ca.ExportCertificatePem();
        InstallInitialCertificate();
    }

    public RunPaths Paths { get; }

    public string AppSettingsPath { get; }

    public CleanupManifest Manifest { get; }

    public Guid OrganizationId { get; } = Guid.NewGuid();

    public Guid NodeId { get; } = Guid.NewGuid();

    /// <summary>The current fake time (UTC), without advancing the clock.</summary>
    public DateTime Now => _now;

    /// <summary>The agent certificates in <c>LocalMachine\My</c>.</summary>
    public List<StoreCert> My { get; } = [];

    /// <summary>The certificate rows of the node.</summary>
    public List<CertRow> Rows { get; } = [];

    public StoreCert? C1Store { get; private set; }

    public string C1Sha256 { get; private set; } = string.Empty;

    public string C2Sha256 { get; private set; } = string.Empty;

    public string C3Sha256 { get; private set; } = string.Empty;

    /// <summary>The Nodes-side grace of C1 after the renewal (5 minutes); null leaves C1's GraceUntil unset.</summary>
    public TimeSpan? C1GraceOffset { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>What the CNG key check reports for C1's key (false when the key is gone).</summary>
    public bool KeyStillExists { get; set; }

    /// <summary>When set, every Gateway probe fails with this error (a transport error).</summary>
    public string? GatewayFailure { get; set; }

    /// <summary>When set, the Nodes probe answers with this status instead of the row rule.</summary>
    public int? NodesStatus { get; set; }

    /// <summary>What the agent does on its first start (U6). Default: renew from grace.</summary>
    public Action<RenewalWorld> OnFirstStart { get; set; } = static w => w.AgentRenewsFromGrace();

    /// <summary>What the agent does on a later start (U8). Default: nothing beyond the heartbeat.</summary>
    public Action<RenewalWorld>? OnRestart { get; set; }

    /// <summary>What the agent does while it stops, after the process is down (a certificate stored after the last poll).</summary>
    public Action<RenewalWorld>? OnStop { get; set; }

    /// <summary>When set, stopping the agent throws, as a <c>Process.Kill</c> failure does.</summary>
    public bool StopThrows { get; set; }

    public int Starts { get; private set; }

    /// <summary>The threshold value in the appsettings file at each agent start.</summary>
    public List<int?> ThresholdsAtStart { get; } = [];

    public DateTime? LastHeartbeat { get; private set; }

    /// <summary>Builds the renewal legs over this world.</summary>
    public RenewalLegs Legs()
    {
        var deps = new RenewalLegsDeps(this, this, this, this, OpenMachine, _ => KeyStillExists, UtcNow);
        var options = new RenewalLegsOptions(
            OrganizationId, NodeId, AppSettingsPath, TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        return new RenewalLegs(Paths, Manifest, deps, options);
    }

    /// <summary>The clock: every read advances one second, so the U7 wait ends.</summary>
    public DateTime UtcNow()
    {
        var read = _now;
        _now = _now.AddSeconds(1);
        return read;
    }

    /// <summary>The agent's renewal from grace: C3 replaces C1 in My, C2 is hard-revoked, the heartbeat moves on.</summary>
    public void AgentRenewsFromGrace()
    {
        var (certificate, key) = NewAgentCertificate();
        _machine[certificate.Thumbprint] = (certificate, key);
        C3Sha256 = Sha256Hex(certificate);
        My.Clear();
        My.Add(ToStoreCert(certificate));
        SetRow(C2Sha256, isRevoked: true, grace: null, reason: SupersededReason);
        SetRow(C3Sha256, isRevoked: false, grace: null, reason: null);
    }

    /// <summary>Puts C1 back into <c>LocalMachine\My</c> (a leftover certificate).</summary>
    public void RestoreC1ToMy() => My.Add(C1Store!);

    /// <summary>Inserts or replaces the row for a certificate thumbprint (SHA-256).</summary>
    public void SetRow(string sha256, bool isRevoked, DateTime? grace, string? reason)
    {
        var index = Rows.FindIndex(row => string.Equals(row.Thumbprint, sha256, StringComparison.OrdinalIgnoreCase));
        var row = new CertRow(index >= 0 ? Rows[index].Id : Guid.NewGuid(), sha256, isRevoked, grace, reason);
        if (index >= 0)
        {
            Rows[index] = row;
        }
        else
        {
            Rows.Add(row);
        }
    }

    public IReadOnlyList<StoreCert> AgentCertificatesInMy() => My.ToList();

    public IReadOnlyList<StoreCert> CaInAgentStore() => [];

    public IReadOnlyList<StoreCert> AgentCasInRoot() => [];

    public bool AgentCertificateChainsToAgentCa() => true;

    public Result<StateFileAclInfo> StateFileAcl(string path) =>
        Result<StateFileAclInfo>.Success(new StateFileAclInfo(true, []));

    public int? ProcessId => _running ? AgentPid : null;

    public string Mode => "process";

    /// <inheritdoc />
    public string? ConsoleLogPath => null;

    /// <inheritdoc />
    public Task<Result> PrepareAsync(CancellationToken ct) => Task.FromResult(Result.Success());

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(Start());

    /// <summary>Starts the fake agent; <see cref="StartAsync"/> wraps it.</summary>
    public Result Start()
    {
        Starts++;
        ThresholdsAtStart.Add(ReadThreshold());
        LastHeartbeat = _now.AddMinutes(Starts);
        _running = true;
        if (Starts == 1)
        {
            OnFirstStart(this);
        }
        else
        {
            OnRestart?.Invoke(this);
        }

        return Result.Success();
    }

    public Task<Result> StopAsync(CancellationToken ct)
    {
        _running = false;
        if (StopThrows)
        {
            throw new InvalidOperationException("the agent process could not be stopped");
        }

        // As the service host does while STOP_PENDING: a cancelled token aborts the stop before the agent is down.
        ct.ThrowIfCancellationRequested();
        OnStop?.Invoke(this);
        return Task.FromResult(Result.Success());
    }

    public Result<bool> SawRecoveryWarning() => Result<bool>.Success(false);

    public Task<Result<IReadOnlyList<NodeRow>>> NodesForOrganizationAsync(Guid organizationId, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<NodeRow>>.Success([new NodeRow(NodeId, NodeStatus.Online, LastHeartbeat)]));

    public Task<Result<IReadOnlyList<CertRow>>> CertificatesForNodeAsync(Guid nodeId, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<CertRow>>.Success(Rows.ToList()));

    public Task<Result<TokenRow>> TokenAsync(Guid tokenId, CancellationToken ct) =>
        Task.FromResult(Result<TokenRow>.Failure("[Db.Token] not used by the renewal legs"));

    /// <summary>The Nodes side of a renewal: C1 is revoked with a grace, C2 is created active, PEM bound to the request key.</summary>
    public Task<Result<RenewCertificateResponse>> RenewAsync(
        Guid nodeId, X509Certificate2 presented, string publicKey, CancellationToken ct)
    {
        var grace = C1GraceOffset is { } offset ? _now + offset : (DateTime?)null;
        SetRow(Sha256Hex(presented), isRevoked: true, grace: grace, reason: null);

        using var subjectKey = ECDsa.Create();
        subjectKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        var request = new CertificateRequest("CN=renewed", subjectKey, HashAlgorithmName.SHA384);
        // Validity is real time: the CA was created on the real clock, and no step reads a certificate's validity.
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        // Capped at the CA's own NotAfter: X.509 times have whole-second precision, so a clock read taken later than the
        // CA's creation would otherwise run past the issuer and CreateCertificate would throw.
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        var issuerEnd = new DateTimeOffset(_ca.NotAfter.ToUniversalTime()).AddDays(-1);
        if (notAfter > issuerEnd)
        {
            notAfter = issuerEnd;
        }
        using var issued = request.Create(_ca, notBefore, notAfter, [0x01, 0x02, 0x03, 0x04]);
        C2Sha256 = Sha256Hex(issued);
        SetRow(C2Sha256, isRevoked: false, grace: null, reason: null);

        var response = new RenewCertificateResponse(C2Sha256, issued.ExportCertificatePem(), _caPem, notBefore, notAfter);
        return Task.FromResult(Result<RenewCertificateResponse>.Success(response));
    }

    public Task<Result<int>> GatewayNegotiateAsync(X509Certificate2 certificate, CancellationToken ct) =>
        Task.FromResult(GatewayFailure is { } failure
            ? Result<int>.Failure(failure)
            : Result<int>.Success(StatusFor(Sha256Hex(certificate))));

    public Task<Result<int>> NodesNegotiateForwardedAsync(X509Certificate2 certificate, CancellationToken ct) =>
        Task.FromResult(Result<int>.Success(NodesStatus ?? StatusFor(Sha256Hex(certificate))));

    /// <summary>Opens a machine certificate with its key (a copy, so the caller owns and disposes it).</summary>
    public Result<X509Certificate2> OpenMachine(string sha1)
    {
        if (!_machine.TryGetValue(sha1, out var entry))
        {
            return Result<X509Certificate2>.Failure($"no machine certificate {sha1}");
        }

        using var copy = X509CertificateLoader.LoadCertificate(entry.Certificate.RawData);
        return Result<X509Certificate2>.Success(copy.CopyWithPrivateKey(entry.Key));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var entry in _machine.Values)
        {
            entry.Certificate.Dispose();
            entry.Key.Dispose();
        }

        _ca.Dispose();
        _caKey.Dispose();
    }

    /// <summary>A hard-revoked or grace-expired row is refused (401); any other row answers 200.</summary>
    private int StatusFor(string sha256)
    {
        var row = Rows.FirstOrDefault(r => string.Equals(r.Thumbprint, sha256, StringComparison.OrdinalIgnoreCase));
        var refused = row is null || (row.IsRevoked && (row.GraceUntil is null || row.GraceUntil <= _now));
        return refused ? HttpUnauthorized : HttpOk;
    }

    private void InstallInitialCertificate()
    {
        var (certificate, key) = NewAgentCertificate();
        _machine[certificate.Thumbprint] = (certificate, key);
        C1Sha256 = Sha256Hex(certificate);
        C1Store = ToStoreCert(certificate);
        My.Add(C1Store);
        Rows.Add(new CertRow(Guid.NewGuid(), C1Sha256, false));
    }

    private int? ReadThreshold()
    {
        var root = JsonNode.Parse(File.ReadAllText(AppSettingsPath));
        return root?["Agent"]?["Security"]?["CertificateRenewalThresholdDays"]?.GetValue<int>();
    }

    private static (X509Certificate2 Certificate, ECDsa Key) NewAgentCertificate()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var issued = SelfSigned("CN=uat-agent", key);
        return (X509CertificateLoader.LoadCertificate(issued.RawData), key);
    }

    private static X509Certificate2 SelfSigned(string subject, ECDsa key, bool certificateAuthority = false)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA384);
        if (certificateAuthority)
        {
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, critical: true));
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static StoreCert ToStoreCert(X509Certificate2 certificate) => new(
        certificate.Subject,
        certificate.Thumbprint,
        Sha256Hex(certificate),
        [],
        certificate.NotBefore.ToUniversalTime(),
        certificate.NotAfter.ToUniversalTime(),
        true,
        new CngKeyInfo($"{Guid.NewGuid():N}_{Guid.NewGuid():D}", KeyProvider, true, true));

    private static string Sha256Hex(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
