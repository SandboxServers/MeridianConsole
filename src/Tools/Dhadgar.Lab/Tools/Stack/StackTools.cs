using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Stack;

/// <summary>
/// Arguments of <c>stack_up</c>. <c>wipe</c> removes the registered containers and their anonymous volumes first.
/// </summary>
public sealed record StackUpArgs(bool? Wipe = null);

/// <summary>
/// Arguments of <c>stack_down</c>. <c>wipe</c> removes the registered containers and their anonymous volumes.
/// </summary>
public sealed record StackDownArgs(bool? Wipe = null);

/// <summary>
/// <c>stack_up</c> (Leased): creates or starts the lab infrastructure and returns the stack status.
/// </summary>
public sealed class StackUpTool : LabTool<StackUpArgs>
{
    private readonly LabStack _stack;

    /// <summary>
    /// Creates the tool over the stack.
    /// </summary>
    public StackUpTool(LabStack stack)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
    }

    /// <inheritdoc />
    public override string Name => "stack_up";

    /// <inheritdoc />
    public override string Description => "Create or start the lab network, Postgres, RabbitMQ, Redis and Toxiproxy; optional wipe removes the lab containers first.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(StackUpArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<StackStatusDto> result = await _stack.UpAsync(args.Wipe ?? false, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value);
    }
}

/// <summary>
/// <c>stack_down</c> (Leased): stops the registered lab containers, or removes them with <c>wipe</c>.
/// </summary>
public sealed class StackDownTool : LabTool<StackDownArgs>
{
    private readonly LabStack _stack;

    /// <summary>
    /// Creates the tool over the stack.
    /// </summary>
    public StackDownTool(LabStack stack)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
    }

    /// <inheritdoc />
    public override string Name => "stack_down";

    /// <inheritdoc />
    public override string Description => "Stop the lab stack; wipe removes its registered containers, proxies, databases and passwords.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(StackDownArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<StackStatusDto> result = await _stack.DownAsync(args.Wipe ?? false, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value);
    }
}

/// <summary>
/// Arguments of <c>stack_reset_data</c>. <c>databases</c> defaults to every lab database.
/// </summary>
public sealed record StackResetDataArgs(IReadOnlyList<string>? Databases = null);

/// <summary>
/// <c>stack_reset_data</c> (Leased): truncates the lab databases (the migrations history is kept) and returns what was emptied.
/// </summary>
public sealed class StackResetDataTool : LabTool<StackResetDataArgs>
{
    private readonly LabDataReset _reset;

    /// <summary>
    /// Creates the tool over the data reset.
    /// </summary>
    public StackResetDataTool(LabDataReset reset)
    {
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
    }

    /// <inheritdoc />
    public override string Name => "stack_reset_data";

    /// <inheritdoc />
    public override string Description => "Truncate the lab databases (default all) and keep the migrations history; returns the tables emptied per database.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(StackResetDataArgs args, LabCallContext context, CancellationToken ct)
    {
        IReadOnlyCollection<string> databases = args.Databases ?? LabStack.LabDatabaseServices;
        Result<IReadOnlyList<TruncatedDatabase>> result = await _reset.ResetAsync(databases, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<object>.Failure(result.Error);
        }

        var truncated = result.Value.Select(db => new { database = db.Database, tables = db.Tables }).ToArray();
        return Result<object>.Success(new { truncated });
    }
}

/// <summary>
/// <c>stack_status</c> (Open): the network and each registered lab container with its live state.
/// </summary>
public sealed class StackStatusTool : LabTool<NoArguments>
{
    private readonly LabStack _stack;

    /// <summary>
    /// Creates the tool over the stack.
    /// </summary>
    public StackStatusTool(LabStack stack)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
    }

    /// <inheritdoc />
    public override string Name => "stack_status";

    /// <inheritdoc />
    public override string Description => "Lab stack state, network and each registered lab container (never the dev stack).";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(NoArguments args, LabCallContext context, CancellationToken ct)
    {
        Result<StackStatusDto> result = await _stack.StatusAsync(ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value);
    }
}
