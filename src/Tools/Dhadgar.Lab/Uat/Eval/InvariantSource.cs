using System.Text.Json;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>invariant</c> source (C7): the violations the checkpoints recorded for one C11 id, optionally since a C7 <c>since</c>
/// moment. Result shape: <c>{violations: [...], count}</c>. Fields: <c>id</c> (required), <c>since</c> (optional).
/// </summary>
public sealed class InvariantSource(InvariantLedger ledger) : IClauseSource
{
    private readonly InvariantLedger _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));

    /// <inheritdoc />
    public string Name => "invariant";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(window);

        Result<string> id = ClauseFields.Required(clause.SourceFields, "id");
        if (id.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(id.Error));
        }

        DateTimeOffset? since = null;
        if (clause.SourceFields.TryGetValue("since", out JsonElement sinceValue) && sinceValue.ValueKind == JsonValueKind.String)
        {
            Result<DateTimeOffset> resolved = window.SinceFor(sinceValue.GetString()!);
            if (resolved.IsFailure)
            {
                return Task.FromResult(Result<JsonElement>.Failure(resolved.Error));
            }

            since = resolved.Value;
        }

        IReadOnlyList<InvariantViolation> violations = _ledger.Violations(id.Value, since);
        return Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new
        {
            violations,
            count = violations.Count,
        })));
    }
}
