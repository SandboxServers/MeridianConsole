using System.Security.Cryptography;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// One fake machine, database and agent for the legs. The fake agent enrolls on its first start (node row, used token,
/// certificate in My and CA in the agent store, state file) and refreshes the heartbeat on every start.
/// </summary>
internal sealed class LegWorld : IRegistryToken, IStoreInspector, IAgentProcess, IAgentLegsDb
{
    public const string TokenPrefix = "tok-";
    public const string SystemSid = "S-1-5-18";
    public const string AdministratorsSid = "S-1-5-32-544";
    public const string AgentSid = "S-1-5-21-1-2-3-1001";

    private readonly RunPaths _paths;
    private readonly CleanupManifest? _manifest;
    private readonly Dictionary<Guid, TokenRow> _tokens = new();

    public LegWorld(RunPaths paths, CleanupManifest? manifest = null)
    {
        _paths = paths;
        _manifest = manifest;
        Sha256 = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CaSha1 = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        CaSha256 = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public Guid NodeId { get; } = Guid.NewGuid();

    public Guid OrganizationId { get; } = Guid.NewGuid();

    public string Sha256 { get; }

    public string CaSha1 { get; }

    public string CaSha256 { get; }

    /// <summary>Every machine, store and database call in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The number of machine-entry lines (certificate lines; cngKey lines must stay zero) in the manifest at each call.</summary>
    public List<(string Call, int MachineEntries)> Snapshots { get; } = [];

    public List<NodeRow> Nodes { get; } = [];

    public List<CertRow> CertRows { get; } = [];

    public List<StoreCert> Mine { get; } = [];

    public List<StoreCert> CaStore { get; } = [];

    public List<StoreCert> Root { get; } = [];

    public bool ChainOk { get; set; } = true;

    public StateFileAclInfo Acl { get; set; } = new(true, [SystemSid, AdministratorsSid, AgentSid]);

    /// <summary>When false, the first start enrolls but leaves the registry value in place.</summary>
    public bool ClearRegistryOnStart { get; set; } = true;

    /// <summary>When true, a restart consumes whatever token is in the registry (a #101 regression).</summary>
    public bool ConsumeOnRestart { get; set; }

    /// <summary>Runs after the fake's own start work; the start count is already incremented.</summary>
    public Action<LegWorld>? OnStart { get; set; }

    public string? RegistryValue { get; private set; }

    public int StartCount { get; private set; }

    /// <inheritdoc />
    public int? ProcessId => StartCount > 0 ? 4242 : null;

    /// <inheritdoc />
    public Result Write(string token)
    {
        Log("registry.write");
        RegistryValue = token;
        return Result.Success();
    }

    /// <inheritdoc />
    public bool ValueExists() => RegistryValue is not null;

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> AgentCertificatesInMy()
    {
        Log("store.my");
        return Mine.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> CaInAgentStore()
    {
        Log("store.ca");
        return CaStore.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> AgentCasInRoot()
    {
        Log("store.root");
        return Root.ToList();
    }

    /// <inheritdoc />
    public bool AgentCertificateChainsToAgentCa()
    {
        Log("store.chain");
        return ChainOk;
    }

    /// <inheritdoc />
    public Result<StateFileAclInfo> StateFileAcl(string path)
    {
        Log("acl");
        return Result<StateFileAclInfo>.Success(Acl);
    }

    /// <inheritdoc />
    public Result Start()
    {
        Log("agent.start");
        StartCount++;
        Directory.CreateDirectory(_paths.AgentState);
        var consumes = StartCount == 1 || ConsumeOnRestart;
        if (RegistryValue is { } value && consumes)
        {
            var tokenId = Guid.ParseExact(value[TokenPrefix.Length..], "N");
            _tokens[tokenId] = new TokenRow(tokenId, DateTime.UtcNow, NodeId);
            if (CertRows.Count == 0)
            {
                Nodes.Add(new NodeRow(NodeId, NodeStatus.Online, null));
                CertRows.Add(new CertRow(Guid.NewGuid(), Sha256, false));
                Mine.Add(AgentCert(Sha256, "agent-key"));
                CaStore.Add(new StoreCert(
                    "CN=Meridian Agent CA", CaSha1, CaSha256, [], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(365),
                    false, null));
            }

            if (ClearRegistryOnStart)
            {
                RegistryValue = null;
            }
        }

        for (var i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].Id == NodeId)
            {
                Nodes[i] = Nodes[i] with { LastHeartbeat = DateTime.UtcNow.AddSeconds(StartCount) };
            }
        }

        File.WriteAllText(
            Path.Combine(_paths.AgentState, "agent-state.json"),
            $"{{\"NodeId\":\"{NodeId:D}\",\"Other\":1}}");
        OnStart?.Invoke(this);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> StopAsync()
    {
        Log("agent.stop");
        return Task.FromResult(Result.Success());
    }

    /// <summary>What <see cref="SawRecoveryWarning"/> returns.</summary>
    public Result<bool> RecoveryWarning { get; set; } = Result<bool>.Success(false);

    /// <summary>Forgets every token row, so the next <see cref="TokenAsync"/> misses.</summary>
    public void ClearTokens() => _tokens.Clear();

    /// <inheritdoc />
    public Result<bool> SawRecoveryWarning()
    {
        Log("agent.log.read");
        return RecoveryWarning;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<NodeRow>>> NodesForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        Log("db.nodes");
        return Task.FromResult(Result<IReadOnlyList<NodeRow>>.Success(Nodes.ToList()));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CertRow>>> CertificatesForNodeAsync(Guid nodeId, CancellationToken ct)
    {
        Log("db.certs");
        return Task.FromResult(Result<IReadOnlyList<CertRow>>.Success(CertRows.ToList()));
    }

    /// <inheritdoc />
    public Task<Result<TokenRow>> TokenAsync(Guid tokenId, CancellationToken ct)
    {
        Log("db.token");
        return Task.FromResult(_tokens.TryGetValue(tokenId, out var row)
            ? Result<TokenRow>.Success(row)
            : Result<TokenRow>.Failure("[Db.EnrollmentTokens] token not found"));
    }

    /// <summary>The mint function the legs receive.</summary>
    public Task<Result<MintedToken>> MintAsync(string label, CancellationToken ct)
    {
        Log("mint");
        var id = Guid.NewGuid();
        _tokens[id] = new TokenRow(id, null, null);
        return Task.FromResult(Result<MintedToken>.Success(new MintedToken(id, TokenPrefix + id.ToString("N"))));
    }

    /// <summary>An agent certificate in My with a machine CNG key that can sign, named for this node.</summary>
    public StoreCert AgentCert(string sha256, string keyName)
    {
        var sha1 = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        return new StoreCert(
            "CN=agent", sha1, sha256, [AgentCertificateProfile.SpiffeId(NodeId)],
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), true,
            new CngKeyInfo(keyName, "Microsoft Platform Crypto Provider", true, true));
    }

    private void Log(string call)
    {
        Calls.Add(call);
        Snapshots.Add((call, _manifest is null ? 0 : CountMachineEntries(_manifest.Path)));
    }

    private static int CountMachineEntries(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return 0;
        }

        return File.ReadAllLines(manifestPath)
            .Count(line => line.Contains("\"kind\":\"certificate\"", StringComparison.Ordinal)
                           || line.Contains("\"kind\":\"cngKey\"", StringComparison.Ordinal));
    }
}

public class AgentLegsTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-legs-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public AgentLegsTests()
    {
        _paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(_paths.AgentPublish);
        Directory.CreateDirectory(_paths.Logs);
        File.WriteAllText(Path.Combine(_paths.AgentPublish, "appsettings.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    internal static AgentLegsOptions Options(RunPaths paths, Guid organizationId) => new(
        organizationId,
        LegWorld.AgentSid,
        new AgentConfig(
            new Uri("https://localhost:5443/"), 10, 10,
            paths.AgentState,
            Path.Combine(paths.AgentData, "Servers"),
            Path.Combine(paths.AgentData, "Temp")),
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    internal AgentLegs Legs(LegWorld world, CleanupManifest manifest) => new(
        _paths,
        manifest,
        new AgentLegsDeps(world, world, world, world, world.MintAsync),
        Options(_paths, world.OrganizationId));

    internal CleanupManifest Manifest() => new(_paths.Manifest);

    [Fact]
    public async Task HappyPath_PassesU2ThroughU4()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pass", evidence["U2"]);
        Assert.Equal("pass", evidence["U3"]);
        Assert.Equal("pass", evidence["U4"]);
        Assert.Equal(true, evidence["U4.token2StillInRegistry"]);
        Assert.Equal(["agent.stop", "agent.log.read", "store.my"], world.Calls.TakeLast(3).ToArray());
    }

    [Fact]
    public async Task RunAsync_ReadsRecoveryWarningAfterStop()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest) { RecoveryWarning = Result<bool>.Success(true) };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(world.Calls.IndexOf("agent.log.read") > world.Calls.LastIndexOf("agent.stop"));
        var warnings = Assert.IsType<List<string>>(evidence["warnings"]);
        Assert.Contains(warnings, w => w.Contains("expected in process mode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RecoveryWarningReadFails_AddsWarning()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest)
        {
            RecoveryWarning = Result<bool>.Failure("[AgentProcess.Log] agent.log could not be read"),
        };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var warnings = Assert.IsType<List<string>>(evidence["warnings"]);
        Assert.Contains(warnings, w => w.StartsWith("agent.log: [AgentProcess.Log]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task U3_RecordsCertificateBeforeOtherConditionsHold()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest) { ClearRegistryOnStart = false };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("U3: the registry token value is removed", result.Error, StringComparison.Ordinal);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Single(loaded.Value.OfType<CertificateEntry>());
        Assert.Empty(loaded.Value.OfType<CngKeyEntry>());

        // The entry exists before the first database read of the poll, not only after the run (the finally also records).
        var firstDbRead = world.Snapshots.First(s => s.Call == "db.nodes");
        Assert.Equal(1, firstDbRead.MachineEntries);
    }

    [Fact]
    public async Task U3_FailsWhenTokenRowIsMissing()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest) { OnStart = w => w.ClearTokens() };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Db.EnrollmentTokens] token not found", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U3_FailsWhenRegistryValueRemains()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest) { ClearRegistryOnStart = false };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("fail", evidence["U3"]);
        Assert.Contains("U3: the registry token value is removed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U3_FailsWhenTwoCertificatesAreInMy()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest)
        {
            OnStart = w =>
            {
                if (w.StartCount == 1)
                {
                    w.Mine.Add(w.AgentCert(w.Sha256 + "00", "second-key"));
                }
            },
        };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("U3: exactly one agent certificate is in LocalMachine", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U3_FailsWhenAnAgentCaIsInRoot()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest)
        {
            OnStart = w =>
            {
                if (w.StartCount == 1)
                {
                    w.Root.Add(new StoreCert(
                        "CN=Meridian Agent CA", w.CaSha1, w.CaSha256, [], DateTime.UtcNow, DateTime.UtcNow.AddDays(1),
                        false, null));
                }
            },
        };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("U3: no agent CA is in LocalMachine\\Root", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U3_FailsWhenUsersCanWriteTheStateFile()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest)
        {
            Acl = new StateFileAclInfo(true, [LegWorld.SystemSid, LegWorld.AdministratorsSid, LegWorld.AgentSid, "S-1-5-32-545"]),
        };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("U3: the state file DACL is protected and allows only SYSTEM", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U4_FailsWhenTheNodeCountBecomesTwo()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest)
        {
            OnStart = w =>
            {
                if (w.StartCount == 2)
                {
                    w.Nodes.Add(new NodeRow(Guid.NewGuid(), NodeStatus.Online, DateTime.UtcNow));
                }
            },
        };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("fail", evidence["U4"]);
        Assert.Contains("U4: still one node for the organization", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U4_FailsWhenTokenTwoIsUsedOnRestart()
    {
        var manifest = Manifest();
        var world = new LegWorld(_paths, manifest) { ConsumeOnRestart = true };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await Legs(world, manifest).RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("fail", evidence["U4"]);
        Assert.Contains("U4: token T2 is not used (UsedAt is null)", result.Error, StringComparison.Ordinal);
    }
}
