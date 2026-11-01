using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// Refuses a <c>rows</c> filter that names a row the spec does not have, or keeps a row without one of its
/// <c>depends_on</c> rows (D-ML123). A kept row whose dependency is dropped would be SKIPPED by <see cref="RowExecutor"/>,
/// so the run would look as if it had run the row. Each kept row is checked, so transitive dependencies are covered too.
/// </summary>
internal static class RowFilterCheck
{
    /// <summary>
    /// Closes a filter over <c>depends_on</c> (F115): returns the filter plus its transitive dependencies, in spec file
    /// order. Ids the spec does not have pass through after the known rows, so <see cref="Check"/> still reports them as
    /// <c>[Lab.NotFound]</c>.
    /// </summary>
    /// <param name="spec">The loaded spec.</param>
    /// <param name="filter">The row ids the caller selected.</param>
    internal static IReadOnlyList<string> Close(UatSpec spec, IReadOnlyCollection<string> filter)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(filter);

        var kept = new HashSet<string>(filter, StringComparer.Ordinal);
        bool added = true;
        while (added)
        {
            added = false;
            foreach (UatRow row in spec.Rows)
            {
                if (!kept.Contains(row.Id))
                {
                    continue;
                }

                foreach (string dependency in row.DependsOn)
                {
                    added |= kept.Add(dependency);
                }
            }
        }

        var known = new HashSet<string>(spec.Rows.Select(row => row.Id), StringComparer.Ordinal);
        var closed = spec.Rows.Where(row => kept.Contains(row.Id)).Select(row => row.Id).ToList();
        closed.AddRange(kept.Where(id => !known.Contains(id)).Order(StringComparer.Ordinal));
        return closed;
    }

    /// <summary>
    /// Checks a filter against a spec. Returns the first offender in file order, or success.
    /// </summary>
    /// <param name="spec">The loaded spec.</param>
    /// <param name="filter">The row ids the run keeps.</param>
    internal static Result Check(UatSpec spec, IReadOnlyCollection<string> filter)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(filter);

        var known = new HashSet<string>(spec.Rows.Select(row => row.Id), StringComparer.Ordinal);
        var kept = new HashSet<string>(filter, StringComparer.Ordinal);

        foreach (string id in filter)
        {
            if (!known.Contains(id))
            {
                return Result.Failure("[Lab.NotFound] row " + id + " is not in the spec");
            }
        }

        foreach (UatRow row in spec.Rows)
        {
            if (!kept.Contains(row.Id))
            {
                continue;
            }

            foreach (string dependency in row.DependsOn)
            {
                if (!kept.Contains(dependency))
                {
                    return Result.Failure("[Lab.InvalidArgument] rows filter keeps " + row.Id + " without its dependency " + dependency);
                }
            }
        }

        return Result.Success();
    }
}
