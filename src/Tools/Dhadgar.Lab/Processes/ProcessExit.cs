namespace Dhadgar.Lab.Processes;

/// <summary>
/// Raised once for every child exit. <c>Expected</c> is true for a stop the lab asked for and for an exit under a fault;
/// L21's I6 counts only unexpected exits.
/// </summary>
/// <param name="Name">The supervised process name.</param>
/// <param name="ExitCode">The exit code, when the OS reported one.</param>
/// <param name="Expected">True when the exit was requested or fault-excused.</param>
/// <param name="At">When the exit was observed.</param>
public sealed record ProcessExit(string Name, int? ExitCode, bool Expected, DateTimeOffset At);
