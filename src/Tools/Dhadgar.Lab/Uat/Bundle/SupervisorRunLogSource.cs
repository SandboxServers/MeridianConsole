using Dhadgar.Lab.Processes;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// Reads the log rings of the supervised processes. Keeps the lines inside the window, the latest
/// <see cref="MaxLinesPerProcess"/> per process. A process whose lines cannot be read is skipped.
/// </summary>
public sealed class SupervisorRunLogSource : IRunLogSource
{
    /// <summary>The most lines kept per process.</summary>
    public const int MaxLinesPerProcess = 5000;

    private readonly LabProcessSupervisor _supervisor;

    /// <summary>
    /// Creates the source over the supervisor.
    /// </summary>
    /// <param name="supervisor">The process supervisor that owns the rings.</param>
    public SupervisorRunLogSource(LabProcessSupervisor supervisor)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    }

    /// <inheritdoc />
    public IReadOnlyList<RunLogSlice> Slices(DateTimeOffset from, DateTimeOffset to)
    {
        var slices = new List<RunLogSlice>();
        foreach (var status in _supervisor.Snapshot())
        {
            var read = _supervisor.ReadLines(status.Name);
            if (read.IsFailure)
            {
                continue;
            }

            slices.Add(new RunLogSlice(status.Name, LinesInWindow(read.Value!, from, to)));
        }

        return slices;
    }

    /// <summary>
    /// The lines whose timestamp is inside <paramref name="from"/> to <paramref name="to"/> (both inclusive), oldest first,
    /// keeping only the latest <see cref="MaxLinesPerProcess"/>.
    /// </summary>
    /// <param name="lines">The captured lines, oldest first.</param>
    /// <param name="from">The window start.</param>
    /// <param name="to">The window end.</param>
    public static IReadOnlyList<LogLine> LinesInWindow(IReadOnlyList<LogLine> lines, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var inWindow = lines
            .Where(line => line.Timestamp >= from && line.Timestamp <= to)
            .ToList();
        return inWindow.Count > MaxLinesPerProcess
            ? inWindow.GetRange(inWindow.Count - MaxLinesPerProcess, MaxLinesPerProcess)
            : inWindow;
    }
}
