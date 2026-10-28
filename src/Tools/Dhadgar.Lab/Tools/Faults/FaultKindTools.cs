using System.Text.Json;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Faults;

/// <summary>
/// Arguments of <c>fault_kill</c>. <c>restart_after_s</c> is the heal window: the container is started again then.
/// </summary>
public sealed record FaultKillArgs(string Target, string? Signal = null, int? RestartAfterS = null);

/// <summary>
/// Arguments of <c>fault_pause</c>.
/// </summary>
public sealed record FaultPauseArgs(string Target, int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_disk_fill</c>. <c>mount</c> must be a declared mount of the target (C10).
/// </summary>
public sealed record FaultDiskFillArgs(string Target, string Mount, int? LeaveKib = null, int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_corrupt_file</c>. <c>file</c> is a logical name from the C10 table.
/// </summary>
public sealed record FaultCorruptFileArgs(string File, string Mode, string? Member = null, int? HealAfterS = null);

/// <summary>
/// Arguments of <c>fault_db</c>. <c>database</c> is a database target id (<c>d:nodes</c>) or a service name.
/// </summary>
public sealed record FaultDbArgs(string Database, string Action, string? Table = null, int? Limit = null, int? HealAfterS = null);

/// <summary>
/// Shared start path of the L20 fault tools: validate the heal window, then hand the kind's arguments to the fault book.
/// </summary>
internal static class FaultKindToolSupport
{
    /// <summary>
    /// The registered container the Nodes CA files belong to; the guard resolves a file fault through it.
    /// </summary>
    public const string NodesTargetId = "c:nodes";

    public static async Task<Result<object>> BeginAsync<T>(
        FaultBook book, string kind, string targetId, T kindArgs, int? healAfterS, CancellationToken ct)
    {
        Result heal = FaultToolSupport.CheckHealAfter(healAfterS);
        if (heal.IsFailure)
        {
            return Result<object>.Failure(heal.Error);
        }

        Result<ActiveFault> begun = await book.BeginAsync(
            kind,
            targetId,
            FaultToolSupport.ToArgs(kindArgs),
            healAfterS ?? FaultToolSupport.DefaultHealAfterS,
            ct).ConfigureAwait(false);

        return begun.IsFailure ? Result<object>.Failure(begun.Error) : Result<object>.Success(begun.Value);
    }

    public static string DatabaseTargetId(string database)
    {
        if (database.StartsWith("d:", StringComparison.Ordinal))
        {
            return database;
        }

        return "d:" + (database.StartsWith(DbFault.DatabasePrefix, StringComparison.Ordinal)
            ? database[DbFault.DatabasePrefix.Length..]
            : database);
    }
}

/// <summary>
/// <c>fault_kill</c> (Leased): kills one lab container; started again after <c>restart_after_s</c> or by heal.
/// </summary>
public sealed class FaultKillTool(FaultBook book) : LabTool<FaultKillArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_kill";

    /// <inheritdoc />
    public override string Description => "Kill a lab container with KILL or TERM; it is started again after restart_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) =>
        FaultToolSupport.ToArgs(new FaultKillArgs(targetId, "KILL", FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultKillArgs args, LabCallContext context, CancellationToken ct) =>
        FaultKindToolSupport.BeginAsync(_book, "kill", args.Target, new KillArgs(args.Signal), args.RestartAfterS, ct);
}

/// <summary>
/// <c>fault_pause</c> (Leased): pauses one lab container until heal.
/// </summary>
public sealed class FaultPauseTool(FaultBook book) : LabTool<FaultPauseArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_pause";

    /// <inheritdoc />
    public override string Description => "Pause a lab container; unpaused after heal_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) =>
        FaultToolSupport.ToArgs(new FaultPauseArgs(targetId, FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultPauseArgs args, LabCallContext context, CancellationToken ct) =>
        FaultKindToolSupport.BeginAsync(_book, "pause", args.Target, new { }, args.HealAfterS, ct);
}

/// <summary>
/// <c>fault_disk_fill</c> (Leased): fills a declared mount of a lab Linux service container until heal.
/// </summary>
public sealed class FaultDiskFillTool(FaultBook book) : LabTool<FaultDiskFillArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_disk_fill";

    /// <inheritdoc />
    public override string Description => "Fill the declared /tmp mount of a Linux service container, leaving leave_kib free; removed after heal_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) =>
        FaultToolSupport.ToArgs(new FaultDiskFillArgs(targetId, "/tmp", 64, FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultDiskFillArgs args, LabCallContext context, CancellationToken ct) =>
        FaultKindToolSupport.BeginAsync(_book, "disk_fill", args.Target, new DiskFillArgs(args.Mount, args.LeaveKib), args.HealAfterS, ct);
}

/// <summary>
/// <c>fault_corrupt_file</c> (Leased): truncates, garbles or deletes one Nodes CA logical file until heal; the original is backed up first.
/// </summary>
public sealed class FaultCorruptFileTool(FaultBook book) : LabTool<FaultCorruptFileArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_corrupt_file";

    /// <inheritdoc />
    public override string Description => "Corrupt a logical file (nodes-ca-key, nodes-ca-cert) with truncate, garbage or delete; restored after heal_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) =>
        FaultToolSupport.ToArgs(new FaultCorruptFileArgs("nodes-ca-key", "truncate", null, FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultCorruptFileArgs args, LabCallContext context, CancellationToken ct) =>
        FaultKindToolSupport.BeginAsync(
            _book, "corrupt_file", FaultKindToolSupport.NodesTargetId, new CorruptFileArgs(args.File, args.Mode, args.Member), args.HealAfterS, ct);
}

/// <summary>
/// <c>fault_db</c> (Leased): terminates backends, limits connections, locks a table or makes a lab database read-only until heal.
/// </summary>
public sealed class FaultDbTool(FaultBook book) : LabTool<FaultDbArgs>, IFaultTool
{
    private readonly FaultBook _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <inheritdoc />
    public override string Name => "fault_db";

    /// <inheritdoc />
    public override string Description => "Database fault on a lab database: terminate_backends, connection_limit, lock_table or read_only; healed after heal_after_s or by fault_heal.";

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) =>
        FaultToolSupport.ToArgs(new FaultDbArgs(targetId, DbFault.ModeConnectionLimit, null, 1, FaultToolSupport.DefaultHealAfterS));

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FaultDbArgs args, LabCallContext context, CancellationToken ct) =>
        FaultKindToolSupport.BeginAsync(
            _book,
            "db",
            FaultKindToolSupport.DatabaseTargetId(args.Database),
            new DbArgs(args.Action, args.Table, args.Limit),
            args.HealAfterS,
            ct);
}
