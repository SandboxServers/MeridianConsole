using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Twins;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// The twin overlay of one run (C19, L53). It is never a tool argument: only <see cref="UatRunner"/> takes it. The <see cref="PreRows"/> run first,
/// graded and recorded like any row, without the extra faults. <see cref="ExtraFaults"/> are injected before the steps of every other row of the run
/// and heal at row end, through <see cref="RowExecutor"/>, like spec faults. <see cref="Pair"/> goes to the run's <c>run.json</c>.
/// </summary>
/// <param name="ExtraFaults">The variant's faults, each a C4 fault tool with its arguments.</param>
/// <param name="PreSpec">The spec file name the pre rows come from, or null when the pre rows are not named. The run's spec must be that file.</param>
/// <param name="PreRows">The pre rows' ids, in run order. Each must exist in the run's spec.</param>
/// <param name="Pair">The experiment pair of this run, or null.</param>
public sealed record UatRunOverlay(IReadOnlyList<FaultSpec> ExtraFaults, string? PreSpec, IReadOnlyList<string> PreRows, PairInfo? Pair);
