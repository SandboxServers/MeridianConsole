using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>human</c> source (C7). A human clause has no evidence to read: the evaluator reports it <c>Pending</c> until
/// <c>uat_attest</c> records a verdict (C8). This source is registered so the name is known, and it refuses any read.
/// </summary>
public sealed class HumanSource : IClauseSource
{
    /// <inheritdoc />
    public string Name => "human";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct) =>
        Task.FromResult(Result<JsonElement>.Failure("[Lab.InvalidArgument] human clauses are attested, not read"));
}
