using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>history_run</c>. <c>scenario</c> is a C19 scenario id from <c>catalog/enroll-history.toml</c>;
/// <c>clients</c> (1..8) overrides the scenario default; <c>minimise</c> defaults to true; <c>replay</c> is the run id of an
/// earlier history bundle: its scenario, seed and client count are re-run (up to five attempts, schedule approximate) and
/// compared by grade. <c>lease_id</c> is used when it matches the current lease; without it the run takes its own lease.
/// </summary>
public sealed record HistoryRunArgs(
    string? Scenario = null,
    int? Clients = null,
    ulong? Seed = null,
    bool? Minimise = null,
    string? Replay = null,
    string? LeaseId = null);

/// <summary>
/// The production <see cref="IHistoryEnvironment"/>: every step goes through <see cref="LabToolDispatcher"/> (one path, one guard,
/// R-BLAST) with the run's lease. The lease id is only ever an argument of a dispatched call; it is never logged or stored.
/// </summary>
public sealed class DispatchedHistoryEnvironment : IHistoryEnvironment
{
    private const string FaultIdField = "fault_id";
    private const string TokenSql = "SELECT CAST(\"Id\" AS text) AS id, \"IsRevoked\" AS revoked, \"UsedAt\" AS used_at, CAST(\"UsedByNodeId\" AS text) AS used_by_node_id FROM enrollment_tokens WHERE \"Id\" = CAST(@id AS uuid)";
    private const string NodesSql = "SELECT CAST(n.\"Id\" AS text) AS id, n.\"Name\" AS name, (SELECT count(*) FROM agent_certificates c WHERE c.\"NodeId\" = n.\"Id\" AND NOT c.\"IsRevoked\") AS active_certs FROM nodes n WHERE n.\"Name\" LIKE @prefix ORDER BY n.\"Name\"";

    private readonly LabToolDispatcher _dispatcher;
    private readonly string? _leaseId;
    private readonly string _caller;
    private readonly List<string> _faultIds = [];

    /// <summary>
    /// Creates the environment.
    /// </summary>
    /// <param name="dispatcher">The tool dispatcher.</param>
    /// <param name="leaseId">The run's lease id, added to calls of Leased tools.</param>
    /// <param name="caller">The caller label for the audit log.</param>
    public DispatchedHistoryEnvironment(LabToolDispatcher dispatcher, string? leaseId, string caller)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leaseId = leaseId;
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    }

    /// <inheritdoc />
    public async Task<Result> ResetDataAsync(CancellationToken ct)
    {
        var args = new JsonObject { ["databases"] = new JsonArray("nodes") };
        Result<JsonElement> call = await CallAsync("stack_reset_data", args, ct).ConfigureAwait(false);
        return call.IsFailure ? Result.Failure(call.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<HistoryToken>> MintTokenAsync(CancellationToken ct)
    {
        var args = new JsonObject { ["label"] = "lab-history" };
        Result<JsonElement> call = await CallAsync("nodes_token_mint", args, ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return Result<HistoryToken>.Failure(call.Error);
        }

        string? tokenRef = Text(call.Value, "token_ref");
        string? tokenId = Text(call.Value, "token_id");
        return tokenRef is null || tokenId is null
            ? Result<HistoryToken>.Failure("[Lab.Unavailable] nodes_token_mint returned no token")
            : Result<HistoryToken>.Success(new HistoryToken(tokenId, tokenRef));
    }

    /// <inheritdoc />
    public async Task<Result> InjectFaultsAsync(IReadOnlyList<UatFault> faults, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(faults);
        foreach (UatFault fault in faults)
        {
            JsonObject args = JsonNode.Parse(fault.Args.GetRawText()) as JsonObject ?? new JsonObject();
            Result<JsonElement> call = await CallAsync(fault.Tool, args, ct).ConfigureAwait(false);
            if (call.IsFailure)
            {
                return Result.Failure(call.Error);
            }

            if (Text(call.Value, FaultIdField) is { } faultId)
            {
                _faultIds.Add(faultId);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealFaultsAsync(CancellationToken ct)
    {
        string? firstError = null;
        foreach (string faultId in _faultIds.ToList())
        {
            var args = new JsonObject { [FaultIdField] = faultId };
            Result<JsonElement> call = await CallAsync("fault_heal", args, ct).ConfigureAwait(false);
            if (call.IsFailure)
            {
                firstError ??= call.Error;
            }
        }

        _faultIds.Clear();
        return firstError is null ? Result.Success() : Result.Failure(firstError);
    }

    /// <inheritdoc />
    public async Task<Result<JsonElement>> SnapshotAsync(string tokenId, string run6, CancellationToken ct)
    {
        var tokenArgs = new JsonObject
        {
            ["database"] = "nodes",
            ["sql"] = TokenSql,
            ["params"] = new JsonObject { ["id"] = tokenId },
            ["max_rows"] = 1,
        };
        Result<JsonElement> token = await CallAsync("db_query", tokenArgs, ct).ConfigureAwait(false);
        if (token.IsFailure)
        {
            return Result<JsonElement>.Failure(token.Error);
        }

        var nodeArgs = new JsonObject
        {
            ["database"] = "nodes",
            ["sql"] = NodesSql,
            ["params"] = new JsonObject { ["prefix"] = "lab-" + run6 + "-%" },
            ["max_rows"] = 500,
        };
        Result<JsonElement> nodes = await CallAsync("db_query", nodeArgs, ct).ConfigureAwait(false);
        if (nodes.IsFailure)
        {
            return Result<JsonElement>.Failure(nodes.Error);
        }

        return Result<JsonElement>.Success(BuildSnapshot(tokenId, token.Value, nodes.Value));
    }

    /// <summary>
    /// Maps the two <c>db_query</c> payloads to the snapshot shape the enrollment rules read: <c>token</c> with
    /// <c>id</c>, <c>revoked</c>, <c>used_at</c> and <c>used_by_node_id</c>; <c>nodes</c> with <c>id</c>, <c>name</c> and
    /// <c>active_certs</c>. Each payload is <c>{"rows":[...]}</c>; a missing row gives the unused, unrevoked default.
    /// </summary>
    /// <param name="tokenId">The token row id (the history key).</param>
    /// <param name="tokenPayload">The token <c>db_query</c> payload.</param>
    /// <param name="nodesPayload">The nodes <c>db_query</c> payload.</param>
    /// <returns>The snapshot.</returns>
    internal static JsonElement BuildSnapshot(string tokenId, JsonElement tokenPayload, JsonElement nodesPayload)
    {
        JsonNode tokenState = new JsonObject { ["id"] = tokenId, ["revoked"] = false, ["used_at"] = null, ["used_by_node_id"] = null };
        if (Rows(tokenPayload).FirstOrDefault() is { } tokenRow)
        {
            tokenState = new JsonObject
            {
                ["id"] = tokenId,
                ["revoked"] = tokenRow.TryGetProperty("revoked", out JsonElement revoked) && revoked.ValueKind == JsonValueKind.True,
                ["used_at"] = tokenRow.TryGetProperty("used_at", out JsonElement usedAt) && usedAt.ValueKind == JsonValueKind.String
                    ? usedAt.GetString()
                    : null,
                ["used_by_node_id"] = tokenRow.TryGetProperty("used_by_node_id", out JsonElement usedBy) && usedBy.ValueKind == JsonValueKind.String
                    ? usedBy.GetString()
                    : null,
            };
        }

        var nodeList = new JsonArray();
        foreach (JsonElement row in Rows(nodesPayload))
        {
            long certs = row.TryGetProperty("active_certs", out JsonElement c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out long n) ? n : 0;
            nodeList.Add(new JsonObject
            {
                ["id"] = row.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() : string.Empty,
                ["name"] = row.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() : string.Empty,
                ["active_certs"] = certs,
            });
        }

        var snapshot = new JsonObject { ["token"] = tokenState, ["nodes"] = nodeList };
        return JsonSerializer.SerializeToElement(snapshot);
    }

    private static List<JsonElement> Rows(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("rows", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToList()
            : [];

    private static string? Text(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<Result<JsonElement>> CallAsync(string tool, JsonObject args, CancellationToken ct)
    {
        if (ToolPolicy.GateFor(tool) is LabGate.Leased or LabGate.OwnLease && _leaseId is not null)
        {
            args[LabToolDispatcher.LeaseIdArgument] = _leaseId;
        }

        var context = new LabCallContext(Guid.NewGuid().ToString("N"), _leaseId, _caller);
        LabToolResult result = await _dispatcher.InvokeAsync(tool, JsonSerializer.SerializeToElement(args), context, ct).ConfigureAwait(false);
        if (!result.IsError)
        {
            return Result<JsonElement>.Success(result.Payload);
        }

        string code = result.Payload.ValueKind == JsonValueKind.Object
            && result.Payload.TryGetProperty("error", out JsonElement error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out JsonElement c)
            && c.ValueKind == JsonValueKind.String
            && c.GetString() is { Length: > 0 } text
                ? text
                : "Lab.Unavailable";
        return Result<JsonElement>.Failure("[" + code + "] " + tool + " failed");
    }
}

/// <summary>
/// <c>history_run</c> (OwnLease): runs one enrollment history scenario through the barrier proxy, checks it, writes the history
/// bundle and returns the verdict. Synchronous, at most 300 s. <c>replay</c> re-runs an earlier bundle's seed and compares grades
/// through <see cref="ReplayJudge"/>.
/// </summary>
public sealed class HistoryRunTool : LabTool<HistoryRunArgs>
{
    private const string ScenarioFile = "catalog/enroll-history.toml";
    private const string OwnLeasePrefix = "history-run/";
    private const int OwnLeaseTtlSeconds = 600;

    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. Collaborators that depend on the tool registry are resolved at call time.
    /// </summary>
    public HistoryRunTool(LabPaths paths, IPathValidator validator, IServiceProvider services)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "history_run";

    /// <inheritdoc />
    public override string Description => "Runs one enrollment history scenario (C19) through the barrier proxy and checks it for linearizability. Returns run_id, verdict, violations and the minimal repro. Takes its own lease unless lease_id is given.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(HistoryRunArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool hasReplay = !string.IsNullOrEmpty(args.Replay);
        if (string.IsNullOrEmpty(args.Scenario) == !hasReplay)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] give scenario or replay, not both and not neither");
        }

        ReplaySource? replay = null;
        if (hasReplay)
        {
            Result<ReplaySource> loaded = await LoadReplayAsync(args.Replay!, ct).ConfigureAwait(false);
            if (loaded.IsFailure)
            {
                return Result<object>.Failure(loaded.Error);
            }

            replay = loaded.Value!;
        }

        string scenarioId = replay?.Scenario ?? args.Scenario!;
        Result<HistoryScenario> scenario = await LoadScenarioAsync(scenarioId, ct).ConfigureAwait(false);
        if (scenario.IsFailure)
        {
            return Result<object>.Failure(scenario.Error);
        }

        int clients = replay?.Clients ?? args.Clients ?? scenario.Value!.Clients;
        if (clients < HistoryRunJob.MinRunClients || clients > HistoryRunJob.MaxClients)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] clients must be between 1 and 8");
        }

        ulong seed = replay?.Seed ?? args.Seed ?? NewSeed();
        bool minimise = args.Minimise ?? true;

        LeaseBook leases = _services.GetRequiredService<LeaseBook>();
        TimeProvider time = _services.GetRequiredService<TimeProvider>();
        string? leaseId = args.LeaseId ?? context.LeaseId;
        bool ownLease = leaseId is null;
        if (leaseId is null)
        {
            Result<LeaseGrant> grant = leases.Acquire(OwnLeasePrefix + Guid.NewGuid().ToString("N"), "history_run " + scenarioId, OwnLeaseTtlSeconds, false, null);
            if (grant.IsFailure)
            {
                return Result<object>.Failure(grant.Error);
            }

            leaseId = grant.Value!.LeaseId;
        }

        try
        {
            int attempts = replay is null ? 1 : ReplayJudge.MaxApproximateAttempts;
            var attemptGrades = new List<IReadOnlyList<string>>();
            AttemptOutcome? kept = null;
            for (int i = 0; i < attempts; i++)
            {
                if (ownLease)
                {
                    _ = leases.Renew(leaseId, OwnLeaseTtlSeconds);
                }

                Result<AttemptOutcome> attempt = await RunAttemptAsync(scenario.Value!, clients, seed, minimise, leaseId, time, ct).ConfigureAwait(false);
                if (attempt.IsFailure)
                {
                    return Result<object>.Failure(attempt.Error);
                }

                kept = attempt.Value!;
                attemptGrades.Add([kept.Grade.ToString()]);
                if (replay is null || ReplayJudge.Reproduced(ReplayFidelity.ScheduleApproximate, [replay.Grade], attemptGrades) == true)
                {
                    break;
                }
            }

            AttemptOutcome final = kept!;
            var payload = new JsonObject
            {
                ["run_id"] = final.Result.RunId,
                ["verdict"] = final.Result.Check.Verdict,
                ["violations"] = JsonSerializer.SerializeToNode(
                    final.Result.Check.Violations.Select(v => new { rule = v.Rule, ops = v.OpIds, required = v.Required }).ToList(), LabJson.Options),
                ["minimal_repro"] = final.Result.Check.Minimal is { } minimal ? JsonSerializer.SerializeToNode(minimal, LabJson.Options) : null,
                ["bundle"] = Path.GetFileName(final.Folder),
            };
            if (replay is not null)
            {
                payload["replay"] = new JsonObject
                {
                    ["of"] = replay.RunId,
                    ["attempts"] = attemptGrades.Count,
                    ["reproduced"] = ReplayJudge.Reproduced(ReplayFidelity.ScheduleApproximate, [replay.Grade], attemptGrades),
                };
            }

            return Result<object>.Success(payload);
        }
        finally
        {
            if (ownLease)
            {
                _ = leases.Release(leaseId);
            }
        }
    }

    private async Task<Result<AttemptOutcome>> RunAttemptAsync(
        HistoryScenario scenario, int clients, ulong seed, bool minimise, string leaseId, TimeProvider time, CancellationToken ct)
    {
        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);
        string runId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        DateTimeOffset started = time.GetUtcNow();
        string folder = Path.Combine(_paths.UatRuns, BundleFolders.Name(started, stamp.Id, runId));

        var environment = new DispatchedHistoryEnvironment(
            _services.GetRequiredService<LabToolDispatcher>(), leaseId, "history:" + runId);
        using HttpMessageHandler handler = LabHttp.CreateHandler();
        var job = new HistoryRunJob(
            environment,
            _services.GetRequiredService<ILabObservationSink>(),
            _services.GetRequiredService<Redactor>(),
            _services.GetRequiredService<SecretVault>(),
            handler,
            time,
            _services.GetService<LabListenerPorts>());

        Result<HistoryRunResult> run = await job.RunAsync(new HistoryRunRequest(runId, folder, scenario, clients, seed, minimise), ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<AttemptOutcome>.Failure(run.Error);
        }

        HistoryRunResult result = run.Value!;
        RowGrade grade = HistoryRunJob.Grade(result.Check);
        var writer = new BundleWriter(
            _paths.UatRuns,
            _validator,
            _services.GetRequiredService<Redactor>(),
            new SecretScanner(_services.GetRequiredService<SecretVault>()),
            time,
            stamp.Id,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            stamp.LedgerLine,
            label: null);
        writer.UseBuild(stamp);
        Result<string> written = writer.WriteHistoryRun(new HistoryBundle(
            result.RunId, started, scenario.Id, seed, result.Clients, result.Reruns, result.History, result.Check, grade));
        return written.IsFailure
            ? Result<AttemptOutcome>.Failure(written.Error)
            : Result<AttemptOutcome>.Success(new AttemptOutcome(result, grade, written.Value!));
    }

    private async Task<Result<HistoryScenario>> LoadScenarioAsync(string id, CancellationToken ct)
    {
        Result<string> dir = UatSpecPaths.ResolveDir(null, _paths, _validator);
        if (dir.IsFailure)
        {
            return Result<HistoryScenario>.Failure(dir.Error);
        }

        Result<string> path = UatSpecPaths.ResolveSpec(dir.Value!, ScenarioFile, _validator);
        if (path.IsFailure)
        {
            return Result<HistoryScenario>.Failure(path.Error);
        }

        if (!File.Exists(path.Value!) || new FileInfo(path.Value!).Length > UatSpecPaths.MaxSpecBytes)
        {
            return Result<HistoryScenario>.Failure("[Lab.NotFound] the scenario file is missing or larger than 1 MiB");
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path.Value!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<HistoryScenario>.Failure("[Lab.Unavailable] the scenario file could not be read");
        }

        HistoryScenarioLoadOutcome loaded = HistoryScenarioLoader.LoadText(ScenarioFile, text);
        if (loaded.Problems.Count > 0)
        {
            return Result<HistoryScenario>.Failure("[Lab.InvalidArgument] the scenario file does not load: " + loaded.Problems.Count.ToString(CultureInfo.InvariantCulture) + " problem(s)");
        }

        HistoryScenario? found = loaded.Scenarios.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
        return found is null
            ? Result<HistoryScenario>.Failure("[Lab.NotFound] no such scenario")
            : Result<HistoryScenario>.Success(found);
    }

    private async Task<Result<ReplaySource>> LoadReplayAsync(string runId, CancellationToken ct)
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, runId, _validator);
        if (folder.IsFailure)
        {
            return Result<ReplaySource>.Failure(folder.Error);
        }

        Result<string> file = BundleFolders.ResolveFile(folder.Value!, "run.json", _validator);
        if (file.IsFailure)
        {
            return Result<ReplaySource>.Failure(file.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(file.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<ReplaySource>.Failure(text.Error);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text.Value!);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "history"
                || !root.TryGetProperty("scenario", out JsonElement scenario) || scenario.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("seed", out JsonElement seed) || !seed.TryGetUInt64(out ulong seedValue)
                || !root.TryGetProperty("clients", out JsonElement clients) || !clients.TryGetInt32(out int clientCount)
                || !root.TryGetProperty("grade", out JsonElement grade) || grade.ValueKind != JsonValueKind.String)
            {
                return Result<ReplaySource>.Failure("[Lab.InvalidArgument] the bundle is not a history run");
            }

            return Result<ReplaySource>.Success(new ReplaySource(runId, scenario.GetString()!, seedValue, clientCount, grade.GetString()!));
        }
        catch (JsonException)
        {
            return Result<ReplaySource>.Failure("[Lab.InvalidArgument] the bundle's run.json does not parse");
        }
    }

    private static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private sealed record ReplaySource(string RunId, string Scenario, ulong Seed, int Clients, string Grade);

    private sealed record AttemptOutcome(HistoryRunResult Result, RowGrade Grade, string Folder);
}
