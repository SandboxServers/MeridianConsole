using System.Globalization;
using Dhadgar.Lab.Invariants;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Invariants;

/// <summary>
/// Arguments of <c>invariants_check</c>. <see cref="Since"/> is an ISO-8601 time; null means the last checkpoint.
/// </summary>
public sealed record InvariantsCheckArgs(string? Since = null);

/// <summary>
/// <c>invariants_check</c> (Open): runs one checkpoint of every registered invariant over <c>[since, now]</c>, records it in
/// the <see cref="InvariantLedger"/> and returns its violations and check errors.
/// </summary>
public sealed class InvariantsCheckTool : LabTool<InvariantsCheckArgs>
{
    private readonly InvariantMonitor _monitor;
    private readonly InvariantLedger _ledger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the tool over the monitor, the ledger and the clock.
    /// </summary>
    public InvariantsCheckTool(InvariantMonitor monitor, InvariantLedger ledger, TimeProvider time)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public override string Name => "invariants_check";

    /// <inheritdoc />
    public override string Description => "Runs the always-on invariants (I1-I6) over [since, now]; since defaults to the last checkpoint. Returns violations and check errors.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(InvariantsCheckArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset from = _ledger.LastCheckpoint ?? now;
        if (args.Since is not null)
        {
            if (!DateTimeOffset.TryParse(args.Since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                return Result<object>.Failure("[Lab.InvalidArgument] since must be an ISO-8601 time");
            }

            from = parsed;
        }

        var window = new InvariantWindow(from, now, null, null);
        InvariantReport report = await _monitor.CheckpointAsync(window, ct).ConfigureAwait(false);
        _ledger.Record(window, report);

        return Result<object>.Success(new
        {
            since = from,
            until = now,
            count = report.Violations.Count,
            violations = report.Violations,
            errors = report.Errors,
        });
    }
}
