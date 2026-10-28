using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Faults;

/// <summary>
/// A mutating tool whose target the blast-radius rules cover. <see cref="SampleArgs"/> returns valid arguments aimed at
/// <paramref name="targetId"/>; <c>BlastRadiusTests</c> calls every such tool with it and expects a refusal.
/// </summary>
public interface IFaultTool
{
    /// <summary>
    /// Arguments that aim the tool at the target id.
    /// </summary>
    JsonElement SampleArgs(string targetId);
}

/// <summary>
/// Arguments of <c>fault_list_targets</c>. <c>kind</c> filters by target kind.
/// </summary>
public sealed record FaultListTargetsArgs(string? Kind = null);

/// <summary>
/// Arguments of <c>faults_active</c>. None.
/// </summary>
public sealed record FaultsActiveArgs;

/// <summary>
/// Arguments of <c>fault_toxic_add</c>. <c>toxicity</c> defaults to 1.0; <c>heal_after_s</c> defaults to 120.
/// </summary>
public sealed record FaultToxicAddArgs(
    string Proxy,
    string Toxic,
    string Stream,
    double? Toxicity = null,
    Dictionary<string, long>? Attributes = null,
    int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_proxy_disable</c>.
/// </summary>
public sealed record FaultProxyDisableArgs(string Proxy, int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_partition</c>: the two ends of an edge.
/// </summary>
public sealed record FaultPartitionArgs(string From, string To, int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_heal</c>.
/// </summary>
public sealed record FaultHealArgs(string FaultId);

/// <summary>
/// Arguments of <c>fault_reset</c>. None.
/// </summary>
public sealed record FaultResetArgs;

/// <summary>
/// One target in <c>fault_list_targets</c>. <c>faults_allowed</c> is the kinds the guard accepts on it.
/// </summary>
public sealed record FaultTargetView(string TargetId, string Kind, string Name, IReadOnlyList<string> FaultsAllowed);

/// <summary>
/// Result of <c>fault_list_targets</c>.
/// </summary>
public sealed record FaultTargetsResult(IReadOnlyList<FaultTargetView> Targets);

/// <summary>
/// Result of <c>faults_active</c>.
/// </summary>
public sealed record FaultsActiveResult(IReadOnlyList<ActiveFault> Faults);

/// <summary>
/// Result of <c>fault_heal</c>.
/// </summary>
public sealed record FaultHealResult(bool Healed, string Reason);

/// <summary>
/// Result of <c>fault_reset</c>: the fault ids that were in place and are now healed.
/// </summary>
public sealed record FaultResetResult(IReadOnlyList<string> Healed);

/// <summary>
/// Shared helpers for the fault tools: the heal window and the argument JSON the fault kinds read.
/// </summary>
internal static class FaultToolSupport
{
    /// <summary>
    /// The heal window when the caller gives none.
    /// </summary>
    public const int DefaultHealAfterS = 120;

    /// <summary>
    /// Checks <c>heal_after_s</c> against the 1..900 window.
    /// </summary>
    public static Result CheckHealAfter(int? healAfterS)
    {
        int value = healAfterS ?? DefaultHealAfterS;
        return value is >= FaultBook.MinHealAfterS and <= FaultBook.MaxHealAfterS
            ? Result.Success()
            : Result.Failure("[Lab.InvalidArgument] heal_after_s must be 1 to 900");
    }

    /// <summary>
    /// Serializes a kind's argument record with the lab JSON options.
    /// </summary>
    public static JsonElement ToArgs<T>(T value) => JsonSerializer.SerializeToElement(value, LabJson.Options);

    /// <summary>
    /// The target id of a proxy: <c>p:&lt;name&gt;</c>.
    /// </summary>
    public static string ProxyTargetId(string proxy) => "p:" + proxy;

    /// <summary>
    /// The proxy name inside a target id, or the id itself when it is not a proxy id.
    /// </summary>
    public static string ProxyNameOf(string targetId) =>
        targetId.StartsWith("p:", StringComparison.Ordinal) ? targetId["p:".Length..] : targetId;
}

/// <summary>
/// <c>fault_list_targets</c> (Open): the registry's targets and the fault kinds each accepts.
/// </summary>
public sealed class FaultListTargetsTool : LabTool<FaultListTargetsArgs>
{
    private readonly ILabTargetRegistry _registry;

    /// <summary>
    /// Creates the tool over the target registry.
    /// </summary>
    public FaultListTargetsTool(ILabTargetRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc />
    public override string Name => "fault_list_targets";

    /// <inheritdoc />
    public override string Description => "List the lab targets faults can aim at, with the fault kinds each accepts.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultListTargetsArgs args, LabCallContext context, CancellationToken ct)
    {
        LabTargetKind? filter = null;
        if (args.Kind is not null)
        {
            if (!Enum.TryParse(args.Kind, ignoreCase: true, out LabTargetKind parsed))
            {
                return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] unknown target kind"));
            }

            filter = parsed;
        }

        List<FaultTargetView> targets = _registry.All
            .Where(target => filter is null || target.Kind == filter)
            .Select(target => new FaultTargetView(
                target.TargetId,
                target.Kind.ToString().ToLowerInvariant(),
                target.Name,
                FaultTargetGuard.AllowedFor(target.Kind).OrderBy(kind => kind, StringComparer.Ordinal).ToList()))
            .ToList();

        return Task.FromResult(Result<object>.Success(new FaultTargetsResult(targets)));
    }
}

/// <summary>
/// <c>faults_active</c> (Open): the faults in place, with their deadlines and any heal error.
/// </summary>
public sealed class FaultsActiveTool(FaultBook book) : LabTool<FaultsActiveArgs>
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "faults_active";

    /// <inheritdoc />
    public override string Description => "List the faults in place: fault id, kind, target, start, deadline and any heal error.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultsActiveArgs args, LabCallContext context, CancellationToken ct) =>
        Task.FromResult(Result<object>.Success(new FaultsActiveResult(_book.ListActive())));
}

/// <summary>
/// <c>fault_toxic_add</c> (Leased): adds one Toxiproxy toxic to a C10 proxy until heal.
/// </summary>
public sealed class FaultToxicAddTool(FaultBook book) : LabTool<FaultToxicAddArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_toxic_add";

    /// <inheritdoc />
    public override string Description => "Add a Toxiproxy toxic to a proxy (latency, bandwidth, timeout, reset_peer, slow_close, slicer, limit_data); healed after heal_after_s.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) => FaultToolSupport.ToArgs(new FaultToxicAddArgs(
        FaultToolSupport.ProxyNameOf(targetId),
        "latency",
        "downstream",
        1.0,
        new Dictionary<string, long>(StringComparer.Ordinal) { ["latency"] = 1000 },
        FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FaultToxicAddArgs args, LabCallContext context, CancellationToken ct)
    {
        Result heal = FaultToolSupport.CheckHealAfter(args.HealAfterS);
        if (heal.IsFailure)
        {
            return Result<object>.Failure(heal.Error);
        }

        var kindArgs = new ToxicArgs(args.Toxic, args.Stream, args.Toxicity, args.Attributes);
        Result<ActiveFault> begun = await _book.BeginAsync(
            "toxic",
            FaultToolSupport.ProxyTargetId(args.Proxy),
            FaultToolSupport.ToArgs(kindArgs),
            args.HealAfterS ?? FaultToolSupport.DefaultHealAfterS,
            ct).ConfigureAwait(false);

        return begun.IsFailure ? Result<object>.Failure(begun.Error) : Result<object>.Success(begun.Value);
    }
}

/// <summary>
/// <c>fault_proxy_disable</c> (Leased): disables one C10 proxy until heal.
/// </summary>
public sealed class FaultProxyDisableTool(FaultBook book) : LabTool<FaultProxyDisableArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_proxy_disable";

    /// <inheritdoc />
    public override string Description => "Disable a C10 proxy so its listener refuses traffic; re-enabled after heal_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) => FaultToolSupport.ToArgs(new FaultProxyDisableArgs(
        FaultToolSupport.ProxyNameOf(targetId),
        FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FaultProxyDisableArgs args, LabCallContext context, CancellationToken ct)
    {
        Result heal = FaultToolSupport.CheckHealAfter(args.HealAfterS);
        if (heal.IsFailure)
        {
            return Result<object>.Failure(heal.Error);
        }

        Result<ActiveFault> begun = await _book.BeginAsync(
            "proxy_disable",
            FaultToolSupport.ProxyTargetId(args.Proxy),
            FaultToolSupport.ToArgs(new ProxyHealData(args.Proxy)),
            args.HealAfterS ?? FaultToolSupport.DefaultHealAfterS,
            ct).ConfigureAwait(false);

        return begun.IsFailure ? Result<object>.Failure(begun.Error) : Result<object>.Success(begun.Value);
    }
}

/// <summary>
/// <c>fault_partition</c> (Leased): disables every C10 proxy on one edge under one fault id.
/// </summary>
public sealed class FaultPartitionTool(FaultBook book) : LabTool<FaultPartitionArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_partition";

    /// <inheritdoc />
    public override string Description => "Partition two services: disable every proxy on the edge between from and to under one fault id.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) => FaultToolSupport.ToArgs(new FaultPartitionArgs(
        "gateway",
        "nodes",
        FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FaultPartitionArgs args, LabCallContext context, CancellationToken ct)
    {
        Result heal = FaultToolSupport.CheckHealAfter(args.HealAfterS);
        if (heal.IsFailure)
        {
            return Result<object>.Failure(heal.Error);
        }

        // The edge's first proxy is the target the guard resolves; the fault covers every proxy on the edge.
        IReadOnlyList<string> edge = PartitionFault.EdgeProxies(args.From, args.To);
        if (edge.Count == 0)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] no proxy on that edge");
        }

        Result<ActiveFault> begun = await _book.BeginAsync(
            "partition",
            FaultToolSupport.ProxyTargetId(edge[0]),
            FaultToolSupport.ToArgs(new PartitionArgs(args.From, args.To)),
            args.HealAfterS ?? FaultToolSupport.DefaultHealAfterS,
            ct).ConfigureAwait(false);

        return begun.IsFailure ? Result<object>.Failure(begun.Error) : Result<object>.Success(begun.Value);
    }
}

/// <summary>
/// <c>fault_heal</c> (Leased): heals one fault by id.
/// </summary>
public sealed class FaultHealTool(FaultBook book) : LabTool<FaultHealArgs>
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_heal";

    /// <inheritdoc />
    public override string Description => "Heal one fault by fault_id now.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FaultHealArgs args, LabCallContext context, CancellationToken ct)
    {
        Result healed = await _book.HealAsync(args.FaultId, "fault_heal", ct).ConfigureAwait(false);
        return healed.IsFailure
            ? Result<object>.Failure(healed.Error)
            : Result<object>.Success(new FaultHealResult(true, "fault_heal"));
    }
}

/// <summary>
/// <c>fault_reset</c> (Leased): heals every fault in place.
/// </summary>
public sealed class FaultResetTool(FaultBook book) : LabTool<FaultResetArgs>
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_reset";

    /// <inheritdoc />
    public override string Description => "Heal every fault in place and return the fault ids that were healed.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FaultResetArgs args, LabCallContext context, CancellationToken ct)
    {
        List<string> before = _book.ListActive().Select(fault => fault.FaultId).ToList();
        Result<int> healed = await _book.HealAllAsync("fault_reset", ct).ConfigureAwait(false);
        if (healed.IsFailure)
        {
            return Result<object>.Failure(healed.Error);
        }

        HashSet<string> stillActive = _book.ListActive().Select(fault => fault.FaultId).ToHashSet(StringComparer.Ordinal);
        List<string> done = before.Where(id => !stillActive.Contains(id)).ToList();
        return Result<object>.Success(new FaultResetResult(done));
    }
}
