using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>A node row as the legs read it.</summary>
/// <param name="Id">The node ID.</param>
/// <param name="Status">The node status.</param>
/// <param name="LastHeartbeat">The last heartbeat, when one was recorded.</param>
internal sealed record NodeRow(Guid Id, NodeStatus Status, DateTime? LastHeartbeat);

/// <summary>An agent certificate row as the legs read it.</summary>
/// <param name="Id">The row ID.</param>
/// <param name="Thumbprint">The stored SHA-256 thumbprint.</param>
/// <param name="IsRevoked">True when the certificate is revoked.</param>
/// <param name="GraceUntil">The renewal grace end (D-S212), when the row has one.</param>
/// <param name="RevocationReason">Why the row was revoked, when it was.</param>
internal sealed record CertRow(Guid Id, string Thumbprint, bool IsRevoked, DateTime? GraceUntil = null, string? RevocationReason = null);

/// <summary>An enrollment token row as the legs read it.</summary>
/// <param name="Id">The token ID.</param>
/// <param name="UsedAt">When the token was used, if it was.</param>
/// <param name="UsedByNodeId">The node that used the token, if any.</param>
internal sealed record TokenRow(Guid Id, DateTime? UsedAt, Guid? UsedByNodeId);

/// <summary>The database reads the agent legs make. Abstracted so the legs run with a fake.</summary>
internal interface IAgentLegsDb
{
    /// <summary>Every node row of one organization.</summary>
    Task<Result<IReadOnlyList<NodeRow>>> NodesForOrganizationAsync(Guid organizationId, CancellationToken ct);

    /// <summary>Every agent certificate row of one node, revoked or not.</summary>
    Task<Result<IReadOnlyList<CertRow>>> CertificatesForNodeAsync(Guid nodeId, CancellationToken ct);

    /// <summary>One enrollment token row; a failure when it does not exist.</summary>
    Task<Result<TokenRow>> TokenAsync(Guid tokenId, CancellationToken ct);
}

/// <summary>Reads the agent legs' rows from the Nodes database.</summary>
internal sealed class NodesAgentLegsDb : IAgentLegsDb
{
    private readonly Func<NodesDbContext> _createContext;

    /// <summary>Creates the reader over a context factory.</summary>
    public NodesAgentLegsDb(Func<NodesDbContext> createContext)
    {
        _createContext = createContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<NodeRow>>> NodesForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        try
        {
            await using var db = _createContext();
            var rows = await db.Nodes.AsNoTracking()
                .Where(n => n.OrganizationId == organizationId)
                .Select(n => new NodeRow(n.Id, n.Status, n.LastHeartbeat))
                .ToListAsync(ct);
            return Result<IReadOnlyList<NodeRow>>.Success(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<NodeRow>>.Failure($"[Db.Nodes] read failed: {ex.GetType().Name}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CertRow>>> CertificatesForNodeAsync(Guid nodeId, CancellationToken ct)
    {
        try
        {
            await using var db = _createContext();
            var rows = await db.AgentCertificates.AsNoTracking()
                .Where(c => c.NodeId == nodeId)
                .Select(c => new CertRow(c.Id, c.Thumbprint, c.IsRevoked, c.GraceUntil, c.RevocationReason))
                .ToListAsync(ct);
            return Result<IReadOnlyList<CertRow>>.Success(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<CertRow>>.Failure($"[Db.AgentCertificates] read failed: {ex.GetType().Name}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<TokenRow>> TokenAsync(Guid tokenId, CancellationToken ct)
    {
        try
        {
            await using var db = _createContext();
            var row = await db.EnrollmentTokens.AsNoTracking()
                .Where(t => t.Id == tokenId)
                .Select(t => new TokenRow(t.Id, t.UsedAt, t.UsedByNodeId))
                .FirstOrDefaultAsync(ct);
            return row is null
                ? Result<TokenRow>.Failure("[Db.EnrollmentTokens] token not found")
                : Result<TokenRow>.Success(row);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<TokenRow>.Failure($"[Db.EnrollmentTokens] read failed: {ex.GetType().Name}");
        }
    }
}

/// <summary>Everything the legs touch besides the run paths and the manifest.</summary>
/// <param name="Registry">The enrollment token registry value.</param>
/// <param name="Store">The certificate stores and the state file ACL.</param>
/// <param name="Agent">The agent process.</param>
/// <param name="Db">The database reads.</param>
/// <param name="MintToken">Mints an enrollment token for the run's organization: label and cancellation in.</param>
internal sealed record AgentLegsDeps(
    IRegistryToken Registry,
    IStoreInspector Store,
    IAgentHost Agent,
    IAgentLegsDb Db,
    Func<string, CancellationToken, Task<Result<MintedToken>>> MintToken);

/// <summary>The leg settings.</summary>
/// <param name="OrganizationId">The run's organization.</param>
/// <param name="AgentAccountSid">The SID of the account the agent runs as (the elevated user in process mode).</param>
/// <param name="Config">The agent configuration to apply to the published appsettings.json.</param>
/// <param name="PollInterval">Delay between condition polls (2 s in a real run).</param>
/// <param name="FirstStartTimeout">How long U3 polls for its conditions (90 s in a real run).</param>
/// <param name="RestartTimeout">How long U4 polls for its conditions (60 s in a real run).</param>
/// <param name="DecoyThumbprint">Service mode only (P22): the decoy CA thumbprint from U10, which U3 requires to be absent from <c>Root</c>.</param>
/// <param name="StateDirectory">The folder that holds <c>agent-state.json</c>; null means the run's agent state folder (P23 uses the MSI default).</param>
internal sealed record AgentLegsOptions(
    Guid OrganizationId,
    string AgentAccountSid,
    AgentConfig Config,
    TimeSpan PollInterval,
    TimeSpan FirstStartTimeout,
    TimeSpan RestartTimeout,
    string? DecoyThumbprint = null,
    string? StateDirectory = null);

/// <summary>
/// The agent legs after the stack (P20 U2-U4): a token written to the registry, a first start that enrolls the node
/// and leaves its machine certificate behind, and a restart that must not consume a second token (#101). Every
/// machine change is preceded by its manifest entries; the agent process is stopped at the end.
/// </summary>
internal sealed class AgentLegs
{
    private const string StateFileName = "agent-state.json";
    private const string AppSettingsFileName = "appsettings.json";
    private const string MachineStoreLocation = "LocalMachine";
    private const string MachineStoreName = "My";
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";

    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly AgentLegsDeps _deps;
    private readonly AgentLegsOptions _options;
    private readonly HashSet<string> _recordedThumbprints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the legs for one run.</summary>
    public AgentLegs(RunPaths paths, CleanupManifest manifest, AgentLegsDeps deps, AgentLegsOptions options)
    {
        _paths = paths;
        _manifest = manifest;
        _deps = deps;
        _options = options;
    }

    /// <summary>Runs U2, U3 and U4 in order and stops at the first failing step. Writes <c>U2</c>-<c>U4</c> and evidence into <paramref name="evidence"/>.</summary>
    public Task<Result> RunAsync(Dictionary<string, object?> evidence, CancellationToken ct) =>
        RunStepsAsync(evidence, null, async (state, token) =>
        {
            var u2 = await U2TokenAsync(state, token);
            if (u2.IsFailure)
            {
                return u2;
            }

            var u3 = await U3FirstStartAsync(state, token);
            if (u3.IsFailure)
            {
                return u3;
            }

            return await U4RestartAsync(state, token);
        }, ct);

    /// <summary>
    /// The MSI leg's U3 and U4 (P23). The MSI installed the agent and enrolled it with <paramref name="installToken"/>
    /// (T1, minted for U11a), so U2 does not run and U3 does not edit the installed configuration.
    /// </summary>
    public Task<Result> RunMsiAsync(Dictionary<string, object?> evidence, MintedToken installToken, CancellationToken ct) =>
        RunStepsAsync(evidence, installToken, async (state, token) =>
        {
            var u3 = await U3FirstStartAsync(state, token, applyConfig: false);
            if (u3.IsFailure)
            {
                return u3;
            }

            return await U4RestartAsync(state, token);
        }, ct);

    private async Task<Result> RunStepsAsync(
        Dictionary<string, object?> evidence,
        MintedToken? installToken,
        Func<LegState, CancellationToken, Task<Result>> steps,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        evidence["warnings"] = warnings;
        var state = new LegState(evidence) { Token1 = installToken };
        try
        {
            return await steps(state, ct);
        }
        finally
        {
            var stopped = await _deps.Agent.StopAsync(CancellationToken.None);
            if (stopped.IsFailure)
            {
                warnings.Add($"agent stop: {stopped.Error}");
            }

            // The recovery warning is read from agent.log only where the host has a console log (process mode).
            if (_deps.Agent.ConsoleLogPath is not null)
            {
                var warning = _deps.Agent.SawRecoveryWarning();
                if (warning.IsFailure)
                {
                    warnings.Add($"agent.log: {warning.Error}");
                }
                else if (warning.Value)
                {
                    warnings.Add("agent.log: service recovery could not be configured (expected in process mode)");
                }
            }

            // A certificate the agent created after the last poll is recorded now, so cleanup removes it and its key.
            try
            {
                var recorded = RecordMachineCertificates(_deps.Store.AgentCertificatesInMy());
                if (recorded.IsFailure)
                {
                    warnings.Add($"certificate record: {recorded.Error}");
                }
            }
            catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or IOException)
            {
                warnings.Add($"certificate record: {ex.GetType().Name}");
            }
        }
    }

    private async Task<Result> U2TokenAsync(LegState state, CancellationToken ct)
    {
        var minted = await _deps.MintToken("uat-agent-first", ct);
        if (minted.IsFailure)
        {
            return Fail(state.Evidence, "U2", minted.Error!);
        }

        state.Token1 = minted.Value;
        state.Evidence["organizationId"] = _options.OrganizationId.ToString();
        state.Evidence["U2.tokenId"] = minted.Value.Id.ToString();

        var written = _deps.Registry.Write(minted.Value.PlainText);
        if (written.IsFailure)
        {
            return Fail(state.Evidence, "U2", written.Error!);
        }

        if (!_deps.Registry.ValueExists())
        {
            return Fail(state.Evidence, "U2", "the registry token value does not exist after the write");
        }

        state.Evidence["U2"] = "pass";
        return Result.Success();
    }

    private async Task<Result> U3FirstStartAsync(LegState state, CancellationToken ct, bool applyConfig = true)
    {
        if (applyConfig)
        {
            var applied = new AgentPublishConfig(_paths).Apply(
                Path.Combine(_paths.AgentPublish, AppSettingsFileName), _options.Config);
            if (applied.IsFailure)
            {
                return Fail(state.Evidence, "U3", applied.Error!);
            }
        }

        var prepared = await _deps.Agent.PrepareAsync(ct);
        if (prepared.IsFailure)
        {
            return Fail(state.Evidence, "U3", prepared.Error!);
        }

        var started = await _deps.Agent.StartAsync(ct);
        if (started.IsFailure)
        {
            return Fail(state.Evidence, "U3", started.Error!);
        }

        var polled = await PollAsync(
            "U3", _options.FirstStartTimeout, token => EvaluateFirstStartAsync(state, token), state.Evidence, ct);
        if (polled.IsFailure)
        {
            return polled;
        }

        state.Evidence["U3"] = "pass";
        return Result.Success();
    }

    /// <summary>One poll result: the condition that is still unmet, or null when every condition holds.</summary>
    private sealed record Check(string? Unmet)
    {
        public static Check Holds { get; } = new((string?)null);
    }

    private static Result<Check> Unmet(string condition) => Result<Check>.Success(new Check(condition));

    private async Task<Result<Check>> EvaluateFirstStartAsync(LegState state, CancellationToken ct)
    {
        var mine = _deps.Store.AgentCertificatesInMy();
        var recorded = RecordMachineCertificates(mine);
        if (recorded.IsFailure)
        {
            return Result<Check>.Failure(recorded.Error!);
        }

        var statePath = StatePath();
        if (statePath.IsFailure)
        {
            return Result<Check>.Failure(statePath.Error!);
        }

        if (state.NodeId is null)
        {
            state.NodeId = ReadStateNodeId(statePath.Value);
            if (state.NodeId is null)
            {
                return Unmet("agent-state.json exists and its NodeId parses");
            }
        }

        var nodeId = state.NodeId.Value;
        var nodes = await _deps.Db.NodesForOrganizationAsync(_options.OrganizationId, ct);
        if (nodes.IsFailure)
        {
            return Result<Check>.Failure(nodes.Error!);
        }

        if (nodes.Value.Count != 1 || nodes.Value[0].Id != nodeId)
        {
            return Unmet("exactly one node exists for the organization, with the state-file node ID");
        }

        var node = nodes.Value[0];
        if (node.Status is not (NodeStatus.Online or NodeStatus.Degraded) || node.LastHeartbeat is null)
        {
            return Unmet("the node is Online or Degraded with a LastHeartbeat");
        }

        state.LastHeartbeat = node.LastHeartbeat;
        var token = await _deps.Db.TokenAsync(state.Token1!.Id, ct);
        if (token.IsFailure)
        {
            return Result<Check>.Failure(token.Error!);
        }

        if (token.Value.UsedAt is null || token.Value.UsedByNodeId != nodeId)
        {
            return Unmet("token T1 has UsedAt set and UsedByNodeId equal to the node");
        }

        if (_deps.Registry.ValueExists())
        {
            return Unmet("the registry token value is removed after the agent reads it");
        }

        if (mine.Count != 1)
        {
            return Unmet("exactly one agent certificate is in LocalMachine\\My");
        }

        var certificate = mine[0];
        if (!certificate.HasPrivateKey || certificate.Key is not { IsMachineKey: true, CanSign: true })
        {
            return Unmet("the agent certificate has a machine CNG key that can sign");
        }

        if (!certificate.SanUris.Contains(AgentCertificateProfile.SpiffeId(nodeId), StringComparer.Ordinal))
        {
            return Unmet("the agent certificate SAN URI equals the node's SPIFFE ID");
        }

        var rows = await _deps.Db.CertificatesForNodeAsync(nodeId, ct);
        if (rows.IsFailure)
        {
            return Result<Check>.Failure(rows.Error!);
        }

        var active = rows.Value.Where(row => !row.IsRevoked).ToList();
        if (active.Count != 1 || !string.Equals(active[0].Thumbprint, certificate.Sha256Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return Unmet("exactly one unrevoked AgentCertificate row for the node has the store SHA-256 thumbprint");
        }

        if (_deps.Store.CaInAgentStore().Count == 0)
        {
            return Unmet("the agent CA is in the MeridianConsoleAgent store");
        }

        if (!_deps.Store.AgentCertificateChainsToAgentCa())
        {
            return Unmet("the agent certificate chains to the CA in the MeridianConsoleAgent store");
        }

        // Service mode (P22): the decoy CA planted by U10 must be gone, removed by the agent (P14, D-S219). A pass
        // condition, not a warning. It is checked before the generic Root check, which the decoy would also trip.
        if (_options.DecoyThumbprint is not null
            && _deps.Store.AgentCasInRoot().Any(c => string.Equals(c.Sha1Thumbprint, _options.DecoyThumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            return Unmet("the decoy CA from U10 is no longer in LocalMachine\\Root");
        }

        if (_deps.Store.AgentCasInRoot().Count != 0)
        {
            return Unmet("no agent CA is in LocalMachine\\Root");
        }

        var acl = _deps.Store.StateFileAcl(statePath.Value);
        if (acl.IsFailure)
        {
            return Result<Check>.Failure(acl.Error!);
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal) { SystemSid, AdministratorsSid, _options.AgentAccountSid };
        if (!acl.Value.IsProtected || acl.Value.AllowSids.Any(sid => !allowed.Contains(sid)))
        {
            return Unmet("the state file DACL is protected and allows only SYSTEM, Administrators and the agent account");
        }

        state.Certificate = certificate;
        state.Evidence["nodeId"] = nodeId.ToString();
        state.Evidence["thumbprint"] = certificate.Sha256Thumbprint;
        state.Evidence["keyName"] = certificate.Key!.KeyName;
        state.Evidence["lastHeartbeat"] = node.LastHeartbeat.Value.ToString("O");
        state.Evidence["notAfter"] = certificate.NotAfter.ToString("O");
        state.Evidence["stateFileAclSids"] = string.Join(",", acl.Value.AllowSids);
        return Result<Check>.Success(Check.Holds);
    }

    private async Task<Result> U4RestartAsync(LegState state, CancellationToken ct)
    {
        var minted = await _deps.MintToken("uat-agent-restart", ct);
        if (minted.IsFailure)
        {
            return Fail(state.Evidence, "U4", minted.Error!);
        }

        state.Token2 = minted.Value;
        state.Evidence["U4.token2Id"] = minted.Value.Id.ToString();

        var written = _deps.Registry.Write(minted.Value.PlainText);
        if (written.IsFailure)
        {
            return Fail(state.Evidence, "U4", written.Error!);
        }

        var nodeId = state.NodeId!.Value;
        var nodes = await _deps.Db.NodesForOrganizationAsync(_options.OrganizationId, ct);
        if (nodes.IsFailure)
        {
            return Fail(state.Evidence, "U4", nodes.Error!);
        }

        var before = nodes.Value.FirstOrDefault(n => n.Id == nodeId)?.LastHeartbeat;
        if (before is null)
        {
            return Fail(state.Evidence, "U4", "no LastHeartbeat baseline for the node");
        }

        var rows = await _deps.Db.CertificatesForNodeAsync(nodeId, ct);
        if (rows.IsFailure)
        {
            return Fail(state.Evidence, "U4", rows.Error!);
        }

        state.Evidence["U4.heartbeatBefore"] = before.Value.ToString("O");
        state.Evidence["U4.certificateRowsBefore"] = rows.Value.Count;
        state.CertificateRowsBefore = rows.Value.Count;

        var stopped = await _deps.Agent.StopAsync(ct);
        if (stopped.IsFailure)
        {
            return Fail(state.Evidence, "U4", stopped.Error!);
        }

        var started = await _deps.Agent.StartAsync(ct);
        if (started.IsFailure)
        {
            return Fail(state.Evidence, "U4", started.Error!);
        }

        var baseline = before.Value;
        var polled = await PollAsync(
            "U4", _options.RestartTimeout, token => EvaluateRestartAsync(state, baseline, token), state.Evidence, ct);
        if (polled.IsFailure)
        {
            return polled;
        }

        state.Evidence["U4.token2StillInRegistry"] = _deps.Registry.ValueExists();
        state.Evidence["U4"] = "pass";
        return Result.Success();
    }

    private async Task<Result<Check>> EvaluateRestartAsync(LegState state, DateTime baseline, CancellationToken ct)
    {
        var mine = _deps.Store.AgentCertificatesInMy();
        var recorded = RecordMachineCertificates(mine);
        if (recorded.IsFailure)
        {
            return Result<Check>.Failure(recorded.Error!);
        }

        var nodeId = state.NodeId!.Value;
        var nodes = await _deps.Db.NodesForOrganizationAsync(_options.OrganizationId, ct);
        if (nodes.IsFailure)
        {
            return Result<Check>.Failure(nodes.Error!);
        }

        var node = nodes.Value.FirstOrDefault(n => n.Id == nodeId);
        if (node?.LastHeartbeat is not { } heartbeat || heartbeat <= baseline)
        {
            return Unmet("LastHeartbeat is later than the baseline");
        }

        state.Evidence["U4.heartbeatAfter"] = heartbeat.ToString("O");
        if (nodes.Value.Count != 1)
        {
            return Unmet("still one node for the organization");
        }

        var token = await _deps.Db.TokenAsync(state.Token2!.Id, ct);
        if (token.IsFailure)
        {
            return Result<Check>.Failure(token.Error!);
        }

        if (token.Value.UsedAt is not null)
        {
            return Unmet("token T2 is not used (UsedAt is null)");
        }

        var rows = await _deps.Db.CertificatesForNodeAsync(nodeId, ct);
        if (rows.IsFailure)
        {
            return Result<Check>.Failure(rows.Error!);
        }

        if (rows.Value.Count != state.CertificateRowsBefore)
        {
            return Unmet("the certificate row count is unchanged");
        }

        var statePath = StatePath();
        if (statePath.IsFailure)
        {
            return Result<Check>.Failure(statePath.Error!);
        }

        if (ReadStateNodeId(statePath.Value) != nodeId)
        {
            return Unmet("agent-state.json holds the same node ID");
        }

        if (!mine.Any(c => string.Equals(c.Sha256Thumbprint, state.Certificate!.Sha256Thumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            return Unmet("the same thumbprint is in LocalMachine\\My");
        }

        return Result<Check>.Success(Check.Holds);
    }

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

    /// <summary>The entries recorded for one agent certificate. Its key is deleted with it, through the certificate (D-S238).</summary>
    internal static IReadOnlyList<CleanupEntry> CertificateEntries(StoreCert certificate) =>
        [new CertificateEntry(MachineStoreLocation, MachineStoreName, certificate.Sha1Thumbprint)];

    /// <summary>Appends the <c>certificate</c> entry for every agent certificate seen, before any check uses it; the key goes with the certificate (D-S238).</summary>
    private Result RecordMachineCertificates(IReadOnlyList<StoreCert> certificates)
    {
        foreach (var certificate in certificates)
        {
            if (!_recordedThumbprints.Add(certificate.Sha1Thumbprint))
            {
                continue;
            }

            foreach (var entry in CertificateEntries(certificate))
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

    /// <summary>Reads <c>NodeId</c> (case-insensitive) from the state file; null when the file is missing or the value does not parse.</summary>
    private static Guid? ReadStateNodeId(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "NodeId", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(property.Value.GetString(), out var nodeId))
                {
                    return nodeId;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The state file path. The run's agent state folder is validated against the run folder; the MSI leg's state
    /// folder is the machine folder <c>%ProgramData%\Meridian Console\Agent</c>, which only that folder check accepts.
    /// </summary>
    private Result<string> StatePath()
    {
        if (_options.StateDirectory is null)
        {
            return _paths.Validate(Path.Combine(_paths.AgentState, StateFileName));
        }

        return AgentDirectories.IsMachineAgentDirectory(_options.StateDirectory)
            ? Result<string>.Success(Path.Combine(_options.StateDirectory, StateFileName))
            : Result<string>.Failure("the MSI state folder is not the machine agent folder");
    }

    private static Result Fail(Dictionary<string, object?> evidence, string step, string message)
    {
        evidence[step] = "fail";
        return Result.Failure($"{step}: {message}");
    }

    /// <summary>Mutable state shared by the steps of one run.</summary>
    private sealed class LegState(Dictionary<string, object?> evidence)
    {
        public Dictionary<string, object?> Evidence { get; } = evidence;

        public MintedToken? Token1 { get; set; }

        public MintedToken? Token2 { get; set; }

        public Guid? NodeId { get; set; }

        public DateTime? LastHeartbeat { get; set; }

        public int CertificateRowsBefore { get; set; }

        public StoreCert? Certificate { get; set; }
    }
}
