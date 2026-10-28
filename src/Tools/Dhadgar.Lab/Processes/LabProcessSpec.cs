namespace Dhadgar.Lab.Processes;

/// <summary>
/// A host child process the supervisor runs (L07). Arguments reach the child only through <c>ProcessStartInfo.ArgumentList</c>.
/// </summary>
/// <param name="Name">Supervisor key and log file name: letters, digits, <c>-</c> and <c>_</c>, at most 64 characters.</param>
/// <param name="FileName">Executable to start (no shell).</param>
/// <param name="Arguments">Argument vector, one entry per argument.</param>
/// <param name="Environment">Extra environment variables for the child. <c>MERIDIAN_LAB_TOKEN</c> is always removed.</param>
/// <param name="WorkingDirectory">Working directory of the child.</param>
/// <param name="Readiness">Optional readiness probe; a false result stops the child and fails the start.</param>
/// <param name="Relaunch">False disables the watchdog relaunch for this process (log followers).</param>
public sealed record LabProcessSpec(
    string Name,
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string WorkingDirectory,
    Func<CancellationToken, Task<bool>>? Readiness,
    bool Relaunch = true);
