using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat;

/// <summary>Everything a scenario needs for one run. Scenarios never write outside <see cref="Paths"/>.</summary>
internal sealed record RunContext(RunPaths Paths, string RunId, string Mode, CleanupManifest Manifest, Redactor Redactor);

/// <summary>
/// One run mode's steps (P19 onward). The skeleton has no scenarios: <c>run</c> records U1 as skipped.
/// </summary>
internal interface IUatScenario
{
    /// <summary>The mode this scenario serves: <c>process</c>, <c>service</c> or <c>msi</c>.</summary>
    string Mode { get; }

    /// <summary>Runs the scenario's steps and returns its evidence, or a failure.</summary>
    Task<Result<Dictionary<string, object?>>> RunAsync(RunContext context, CancellationToken ct);
}
