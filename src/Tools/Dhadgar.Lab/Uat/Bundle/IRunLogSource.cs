using Dhadgar.Lab.Processes;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// The captured lines of one supervised process that fall inside a run window.
/// </summary>
/// <param name="Process">The supervised process name.</param>
/// <param name="Lines">The lines, oldest first.</param>
public sealed record RunLogSlice(string Process, IReadOnlyList<LogLine> Lines);

/// <summary>
/// Supplies the log slices a bundle writes under <c>logs\</c>.
/// </summary>
public interface IRunLogSource
{
    /// <summary>
    /// The slices of every process for the window <paramref name="from"/> to <paramref name="to"/> (both inclusive).
    /// </summary>
    /// <param name="from">The window start.</param>
    /// <param name="to">The window end.</param>
    IReadOnlyList<RunLogSlice> Slices(DateTimeOffset from, DateTimeOffset to);
}
