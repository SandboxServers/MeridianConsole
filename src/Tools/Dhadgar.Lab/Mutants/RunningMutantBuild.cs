namespace Dhadgar.Lab.Mutants;

/// <summary>The mutant build serving the lab's services, set by mutation_run from build to restore (D-ML155).</summary>
public sealed record MutantMark(string BuildId, string MutantId, string Commit);

/// <summary>
/// Holds the <see cref="MutantMark"/> while a mutant build serves the lab, so detector bundles and the flake check can tell
/// that the services run mutant code. Thread-safe; a singleton in the MCP host.
/// </summary>
public sealed class RunningMutantBuild
{
    private readonly Lock _gate = new();
    private MutantMark? _mark;

    /// <summary>The current mark, or null when the services run the plain build.</summary>
    public MutantMark? Current { get { lock (_gate) { return _mark; } } }

    /// <summary>Records that a mutant build now serves the services.</summary>
    /// <param name="mark">The mark to record.</param>
    public void Set(MutantMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_gate) { _mark = mark; }
    }

    /// <summary>Clears the mark after the plain build is restored.</summary>
    public void Clear() { lock (_gate) { _mark = null; } }
}
