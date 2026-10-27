using Dhadgar.Agent.Windows.Uat.Cleanup;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>One step of a run. Evidence values are strings, numbers or booleans only, never a secret.</summary>
internal sealed record StepResult(
    string Id,
    string Name,
    string Status,
    IReadOnlyDictionary<string, object?> Evidence,
    string? Message,
    long DurationMs);

/// <summary>The cleanup object of <c>results.json</c>.</summary>
internal sealed record CleanupSummary(
    string Status,
    int Undone,
    IReadOnlyList<CleanupFailure> Failed,
    IReadOnlyList<string> Remaining,
    IReadOnlyList<string> RootAdded,
    IReadOnlyList<string> RootRemoved)
{
    /// <summary>Cleanup of a run that never changed anything.</summary>
    public static CleanupSummary NotRun() => new("not-run", 0, [], [], [], []);
}

/// <summary>The schema of <c>results.json</c> (schemaVersion 1).</summary>
internal sealed record UatResults(
    int SchemaVersion,
    string RunId,
    string Verb,
    string? Mode,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    string GitSha,
    bool GitDirty,
    int WindowsBuild,
    string DotnetVersion,
    string LaunchedBy,
    bool Elevated,
    string Overall,
    int ExitCode,
    IReadOnlyList<StepResult> Steps,
    CleanupSummary Cleanup,
    IReadOnlyList<string> Warnings);
