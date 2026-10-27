using System.Text.Json.Serialization;
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

/// <summary>The agent-run object of <c>results.json</c>, present only when <c>launchedBy</c> is <c>agent</c> (D-S239).</summary>
internal sealed record AgentRunInfo(string Authority, string DetectedBy, bool TreeClean);

/// <summary>
/// The service object of <c>results.json</c>, present only in service mode (P22, D-S222). <c>DecoyRemoved</c> is U3's
/// result; <c>TlsRootRemovedAtCleanup</c> is the post-cleanup <c>LocalMachine\Root</c> check of the TLS leaf by thumbprint.
/// </summary>
internal sealed record ServiceRunInfo(string Name, string Account, bool DecoyRemoved, bool TlsRootRemovedAtCleanup);

/// <summary>
/// The msi object of <c>results.json</c>, present only in MSI mode (P23, D-S223). A value the run did not reach is null
/// (the exit codes are -1 and the residue is empty). <c>Residue</c> lists what the uninstall left behind, as warnings.
/// </summary>
internal sealed record MsiRunInfo(
    string? ProductCode,
    string? ProductVersion,
    int InstallExitCode,
    int UninstallExitCode,
    IReadOnlyList<string> Residue);

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
    IReadOnlyList<string> Warnings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentRunInfo? AgentRun = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ServiceRunInfo? Service = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MsiRunInfo? Msi = null);
