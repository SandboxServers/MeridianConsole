using System.Text.Json.Nodes;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>results_query</c>: a view, an optional string filter (keys depend on the view) and a limit (default 50,
/// max 500).
/// </summary>
public sealed record ResultsQueryArgs(string? View = null, Dictionary<string, string>? Filter = null, int? Limit = null);

/// <summary>
/// <c>results_query</c> (Open): reads the results index. Views are <c>runs</c>, <c>rows</c>, <c>buckets</c>, <c>flakes</c>,
/// <c>trend</c>, <c>known_issues</c> and <c>findings</c> (empty until L26). Every read goes through
/// <see cref="ResultsService"/>; the tool takes no path and never writes.
/// </summary>
public sealed class ResultsQueryTool : LabTool<ResultsQueryArgs>
{
    private readonly ResultsService _results;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    public ResultsQueryTool(ResultsService results)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <inheritdoc />
    public override string Name => "results_query";

    /// <inheritdoc />
    public override string Description =>
        "Queries the results index. view: runs, rows, buckets, flakes, trend, known_issues or findings; filter (string values); limit (50, max 500). Returns {view, items}.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(ResultsQueryArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.View) || !ResultsDb.Views.Contains(args.View, StringComparer.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] view must be one of " + string.Join(", ", ResultsDb.Views));
        }

        Result<IReadOnlyList<JsonObject>> items = await _results.QueryAsync(args.View, args.Filter, args.Limit, ct).ConfigureAwait(false);
        if (items.IsFailure)
        {
            return Result<object>.Failure(items.Error);
        }

        return Result<object>.Success(new { view = args.View, items = items.Value });
    }
}
