using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Barrier;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.History.Rules;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Random;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.History;

/// <summary>
/// The lab steps a history run needs around the barrier proxy: the data reset, the enrollment token mint, the faults, and the
/// db_query snapshot of the final state. Production wires these through the lab's tools; tests pass fakes.
/// </summary>
public interface IHistoryEnvironment
{
    /// <summary>Resets the nodes database (<c>stack_reset_data {databases = ["nodes"]}</c>).</summary>
    Task<Result> ResetDataAsync(CancellationToken ct);

    /// <summary>Mints the enrollment token the clients present (<c>nodes_token_mint</c>).</summary>
    Task<Result<HistoryToken>> MintTokenAsync(CancellationToken ct);

    /// <summary>Injects the scenario's faults through the lab tool dispatcher.</summary>
    Task<Result> InjectFaultsAsync(IReadOnlyList<UatFault> faults, CancellationToken ct);

    /// <summary>Heals every fault the run injected.</summary>
    Task<Result> HealFaultsAsync(CancellationToken ct);

    /// <summary>The final state of the token and the run's nodes, read by db_query (R-OBSERVE).</summary>
    Task<Result<JsonElement>> SnapshotAsync(string tokenId, string run6, CancellationToken ct);
}

/// <summary>
/// The token the run's clients present: its id (the history key) and its vault reference (never the token).
/// </summary>
/// <param name="TokenId">The token row id.</param>
/// <param name="TokenRef">The vault reference of the token plaintext.</param>
public sealed record HistoryToken(string TokenId, string TokenRef);

/// <summary>
/// One history run request.
/// </summary>
/// <param name="RunId">The run id; its first six hex characters form the hostname prefix.</param>
/// <param name="RunFolder">The folder the observation lines are written to.</param>
/// <param name="Scenario">The scenario from <c>enroll-history.toml</c>.</param>
/// <param name="Clients">The number of clients (2..8).</param>
/// <param name="Seed">The run seed; client order and key labels come from <c>LabRandom(seed, "history:&lt;scenario&gt;")</c>.</param>
/// <param name="Minimise">Whether a violation is minimised and re-run with fewer clients.</param>
public sealed record HistoryRunRequest(
    string RunId,
    string RunFolder,
    HistoryScenario Scenario,
    int Clients,
    ulong Seed,
    bool Minimise);

/// <summary>
/// The result of a history run: the run that was kept (the smallest violating run when minimised), the number of re-runs made.
/// </summary>
/// <param name="RunId">The kept run's id.</param>
/// <param name="Clients">The kept run's client count.</param>
/// <param name="Reruns">The minimisation re-runs made.</param>
/// <param name="History">The recorded history of the kept run.</param>
/// <param name="Check">The check of the kept run.</param>
public sealed record HistoryRunResult(string RunId, int Clients, int Reruns, History History, CheckResult Check);

/// <summary>
/// What the history bundle writer needs for one history run (C19): the kept run, its check and the row grade.
/// </summary>
/// <param name="RunId">The kept run's id.</param>
/// <param name="Started">When the run started (the folder name).</param>
/// <param name="Scenario">The scenario id.</param>
/// <param name="Seed">The run seed.</param>
/// <param name="Clients">The kept run's client count.</param>
/// <param name="Reruns">The minimisation re-runs made.</param>
/// <param name="History">The recorded history.</param>
/// <param name="Check">The check.</param>
/// <param name="Grade">The synthetic row's grade (see <see cref="HistoryRunJob.Grade"/>).</param>
public sealed record HistoryBundle(
    string RunId,
    DateTimeOffset Started,
    string Scenario,
    ulong Seed,
    int Clients,
    int Reruns,
    History History,
    CheckResult Check,
    RowGrade Grade);

/// <summary>
/// Runs one enrollment history (C19): reset, mint, start the barrier proxy, arm the rules, inject faults, run the clients,
/// heal, read the final state, record and check. A violation is minimised by re-running with fewer clients.
/// </summary>
public sealed class HistoryRunJob
{
    /// <summary>The barrier that holds every client's first enroll until all have arrived (HoldRequest scenarios).</summary>
    public const string EnrollBarrier = "enroll-start";

    /// <summary>The fewest clients a run uses (also the floor of the minimisation).</summary>
    public const int MinClients = 2;

    /// <summary>The most clients a run uses.</summary>
    public const int MaxClients = 8;

    /// <summary>The most minimisation re-runs one history run makes.</summary>
    public const int MaxReruns = 20;

    /// <summary>
    /// E5 is reported non-required until D-ML49 makes it required; the model's idempotent retry is off.
    /// </summary>
    public const bool IdempotentRetry = false;

    private const string EnrollPath = "/api/v1/agents/enroll";
    private const string Platform = "linux";
    private const int RunPrefixLength = 6;
    private const int ReadMax = 5000;
    private const string ActorPrefix = "client:";
    private static readonly TimeSpan CheckBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ClientsCap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BarrierWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RunCap = TimeSpan.FromSeconds(300);

    private readonly IHistoryEnvironment _environment;
    private readonly ILabObservationSink _sink;
    private readonly Redactor _redactor;
    private readonly SecretVault _vault;
    private readonly HttpMessageHandler _handler;
    private readonly TimeProvider _time;
    private readonly LabListenerPorts? _listeners;
    private readonly int _maxReruns;

    /// <summary>
    /// Creates the job.
    /// </summary>
    /// <param name="environment">The reset, mint, fault and snapshot steps.</param>
    /// <param name="sink">The observation sink.</param>
    /// <param name="redactor">The redactor for every argument and result.</param>
    /// <param name="vault">The vault that holds the token.</param>
    /// <param name="handler">The client handler, over <see cref="LabHttp.CreateHandler"/>.</param>
    /// <param name="time">The clock.</param>
    /// <param name="listeners">The lab's listener registry (D-ML69); the proxy's port is recorded there.</param>
    /// <param name="maxReruns">The minimisation re-run cap (tests lower it); defaults to <see cref="MaxReruns"/>.</param>
    public HistoryRunJob(
        IHistoryEnvironment environment,
        ILabObservationSink sink,
        Redactor redactor,
        SecretVault vault,
        HttpMessageHandler handler,
        TimeProvider time,
        LabListenerPorts? listeners = null,
        int maxReruns = MaxReruns)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _listeners = listeners;
        _maxReruns = Math.Max(0, maxReruns);
    }

    /// <summary>
    /// Runs the history, and when <see cref="HistoryRunRequest.Minimise"/> is set and the run violates, re-runs with the client
    /// count halved (down to two) while the violation recurs, at most <see cref="MaxReruns"/> times and inside 300 s.
    /// </summary>
    public async Task<Result<HistoryRunResult>> RunAsync(HistoryRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Clients < MinClients || request.Clients > MaxClients)
        {
            return Result<HistoryRunResult>.Failure("[Lab.InvalidArgument] clients must be between 2 and 8");
        }

        DateTimeOffset started = _time.GetUtcNow();
        Result<Attempt> first = await RunOnceAsync(request, ct).ConfigureAwait(false);
        if (first.IsFailure)
        {
            return Result<HistoryRunResult>.Failure(first.Error);
        }

        Attempt best = first.Value!;
        int reruns = 0;
        if (request.Minimise && best.Check.Verdict == LinearizabilityChecker.Violation)
        {
            int clients = best.Clients;
            while (clients > MinClients && reruns < _maxReruns && _time.GetUtcNow() - started < RunCap)
            {
                clients = Math.Max(MinClients, clients / 2);
                reruns++;
                string rerunId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
                HistoryRunRequest rerun = request with
                {
                    RunId = rerunId,
                    RunFolder = request.RunFolder + "-rerun-" + reruns.ToString(CultureInfo.InvariantCulture),
                    Clients = clients,
                };
                Result<Attempt> again = await RunOnceAsync(rerun, ct).ConfigureAwait(false);
                if (again.IsFailure || again.Value!.Check.Verdict != LinearizabilityChecker.Violation)
                {
                    break;
                }

                best = again.Value;
            }
        }

        return Result<HistoryRunResult>.Success(new HistoryRunResult(best.RunId, best.Clients, reruns, best.History, best.Check));
    }

    /// <summary>
    /// Checks a recorded history: the linearizability check of the enrollment model, merged with the E1-E6 rules. The verdict is
    /// <c>violation</c> when the check or a required rule fails, otherwise the check's verdict. A minimal failing history is kept
    /// when <paramref name="minimise"/> is set and the verdict is a violation.
    /// </summary>
    public static CheckResult Evaluate(History history, bool minimise)
    {
        ArgumentNullException.ThrowIfNull(history);
        var model = new EnrollmentHistoryModel(IdempotentRetry);
        CheckResult linearizable = LinearizabilityChecker.Check(history, model, CheckBudget);
        IReadOnlyList<HistoryViolation> rules = EnrollmentRules.Evaluate(history, IdempotentRetry);
        var violations = linearizable.Violations.Concat(rules).ToList();
        bool requiredRule = rules.Any(v => v.Required);
        string verdict = linearizable.Verdict == LinearizabilityChecker.Violation || requiredRule
            ? LinearizabilityChecker.Violation
            : linearizable.Verdict;
        History? minimal = minimise && verdict == LinearizabilityChecker.Violation
            ? LinearizabilityChecker.Minimise(history, model, CheckBudget)
            : null;
        return new CheckResult(verdict, violations, minimal, verdict == LinearizabilityChecker.Unknown ? linearizable.Reason : null);
    }

    /// <summary>
    /// The row grade of a check (C19): FAIL for a required violation, UNVERIFIED for <c>unknown</c>, PASS for
    /// <c>linearizable</c> with no required violation.
    /// </summary>
    public static RowGrade Grade(CheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Verdict == LinearizabilityChecker.Violation || check.Violations.Any(v => v.Required))
        {
            return RowGrade.Fail;
        }

        return check.Verdict == LinearizabilityChecker.Unknown ? RowGrade.Unverified : RowGrade.Pass;
    }

    private async Task<Result<Attempt>> RunOnceAsync(HistoryRunRequest request, CancellationToken ct)
    {
        var upstream = new Uri("http://127.0.0.1:" + LabPorts.AgentIngress.ToString(CultureInfo.InvariantCulture));
        if (!LabHosts.IsLabEndpoint(upstream))
        {
            return Result<Attempt>.Failure("[Lab.Untrusted] the barrier upstream is not a lab endpoint");
        }

        Result reset = await _environment.ResetDataAsync(ct).ConfigureAwait(false);
        if (reset.IsFailure)
        {
            return Result<Attempt>.Failure(reset.Error);
        }

        Result<HistoryToken> token = await _environment.MintTokenAsync(ct).ConfigureAwait(false);
        if (token.IsFailure)
        {
            return Result<Attempt>.Failure(token.Error);
        }

        Result began = _sink.BeginRun(request.RunId, request.RunFolder);
        if (began.IsFailure)
        {
            return Result<Attempt>.Failure(began.Error);
        }

        // Client order comes from the seeded stream; the first op ids are reserved so the barrier rules can name them.
        var rng = new LabRandom(request.Seed, "history:" + request.Scenario.Id);
        int[] order = Enumerable.Range(1, request.Clients).ToArray();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var firstOps = new Dictionary<int, string>();
        foreach (int index in order)
        {
            firstOps[index] = _sink.NextOp(request.RunId, ActorPrefix + index.ToString(CultureInfo.InvariantCulture)).ToString();
        }

        Result<BarrierProxy> started = await BarrierProxy.StartAsync(upstream, LabPorts.Barrier, _sink, _time, ct, _listeners)
            .ConfigureAwait(false);
        if (started.IsFailure)
        {
            return Result<Attempt>.Failure(started.Error);
        }

        await using BarrierProxy proxy = started.Value!;
        bool hold = request.Scenario.FirstOpAction == HistoryFirstOpAction.HoldRequest;
        IReadOnlyList<BarrierRule> rules = order
            .Select(index => hold
                ? new BarrierRule(firstOps[index], BarrierAction.HoldRequest, EnrollBarrier, 0)
                : new BarrierRule(firstOps[index], BarrierAction.DropResponse, null, 0))
            .ToList();
        Result armed = proxy.Arm(request.RunId, rules);
        if (armed.IsFailure)
        {
            return Result<Attempt>.Failure(armed.Error);
        }

        Result injected = await _environment.InjectFaultsAsync(request.Scenario.Faults, ct).ConfigureAwait(false);
        if (injected.IsFailure)
        {
            return Result<Attempt>.Failure(injected.Error);
        }

        var endpoint = new Uri("http://127.0.0.1:" + LabPorts.Barrier.ToString(CultureInfo.InvariantCulture) + EnrollPath);
        string run6 = new(request.RunId.Where(char.IsAsciiHexDigitLower).Take(RunPrefixLength).ToArray());
        string tokenId = token.Value!.TokenId;
        using var http = new HttpClient(_handler, disposeHandler: false);
        var clients = new List<HistoryClient>();
        var tasks = new List<Task<Result<HistoryClientOutcome>>>();
        try
        {
            foreach (int index in order)
            {
                var client = new HistoryClient(
                    request.RunId,
                    index,
                    "key-" + index.ToString(CultureInfo.InvariantCulture),
                    Platform,
                    token.Value.TokenRef,
                    endpoint,
                    http,
                    _sink,
                    _vault,
                    _redactor,
                    _time);
                clients.Add(client);
                tasks.Add(client.RunAsync(request.Scenario.Retry, firstOps[index], ct));
            }

            if (hold)
            {
                _ = await proxy.WaitArrivedAsync(EnrollBarrier, request.Clients, BarrierWait, ct).ConfigureAwait(false);
                _ = proxy.Release(EnrollBarrier);
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(ClientsCap, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Stragglers are closed Unknown by EndRunAsync below.
            }
        }
        finally
        {
            foreach (HistoryClient client in clients)
            {
                client.Dispose();
            }
        }

        _ = await _environment.HealFaultsAsync(ct).ConfigureAwait(false);
        Result<long> ended = await _sink.EndRunAsync(request.RunId, ct).ConfigureAwait(false);
        if (ended.IsFailure)
        {
            return Result<Attempt>.Failure(ended.Error);
        }

        Result<JsonElement> final = await _environment.SnapshotAsync(tokenId, run6, ct).ConfigureAwait(false);
        if (final.IsFailure)
        {
            return Result<Attempt>.Failure(final.Error);
        }

        IReadOnlyList<LabObservation> observations = _sink.Read(request.RunId, 0, ReadMax);
        Result<History> recorded = HistoryRecorder.FromObservations(
            request.RunId,
            request.Scenario.Id,
            observations,
            final.Value!,
            o => SelectEnroll(tokenId, o));
        if (recorded.IsFailure)
        {
            return Result<Attempt>.Failure(recorded.Error);
        }

        CheckResult check = Evaluate(recorded.Value!, request.Minimise);
        return Result<Attempt>.Success(new Attempt(request.RunId, request.Clients, recorded.Value!, check));
    }

    private static (string Key, string F, JsonElement Input)? SelectEnroll(string tokenId, LabObservation o)
    {
        if (o.Kind != ObservationKind.Invoke || o.Op != HistoryClient.Operation)
        {
            return null;
        }

        return (tokenId, o.Op, o.Args);
    }

    private sealed record Attempt(string RunId, int Clients, History History, CheckResult Check);
}
