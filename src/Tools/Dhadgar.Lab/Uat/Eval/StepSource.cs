using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>step</c> source (C7): the outcome of one step of this row, read from <see cref="ClauseWindow.Steps"/>. A row can assert
/// on an expected error or on a duration. Result shape: <c>{ok, error, error_code, duration_ms, result}</c>.
/// </summary>
public sealed class StepSource : IClauseSource
{
    /// <inheritdoc />
    public string Name => "step";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<string> step = ClauseFields.Required(clause.SourceFields, "step");
        if (step.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(step.Error));
        }

        if (!window.Steps.TryGetValue(step.Value, out StepOutcome? outcome))
        {
            return Task.FromResult(Result<JsonElement>.Failure($"step {step.Value} did not run"));
        }

        return Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new
        {
            ok = outcome.Ok,
            error = outcome.Error,
            error_code = outcome.ErrorCode,
            duration_ms = outcome.DurationMs,
            result = outcome.Result,
        })));
    }
}
