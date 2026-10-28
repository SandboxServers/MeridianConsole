using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>tool</c> source (C7): one OPEN tool through <see cref="LabToolDispatcher"/>, so the same gate and audit apply as
/// for any caller. A tool that is not classified OPEN is refused before dispatch. The result is the tool's payload.
/// </summary>
public sealed class ToolSource : IClauseSource
{
    private readonly LabToolDispatcher _dispatcher;

    /// <summary>
    /// Creates the source over the dispatcher.
    /// </summary>
    public ToolSource(LabToolDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public string Name => "tool";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<string> tool = ClauseFields.Required(clause.SourceFields, "tool");
        if (tool.IsFailure)
        {
            return Result<JsonElement>.Failure(tool.Error);
        }

        if (ToolPolicy.GateFor(tool.Value) != LabGate.Open)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] a tool source needs an OPEN tool");
        }

        JsonElement args = JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal));
        if (clause.SourceFields.TryGetValue("args", out JsonElement rawArgs))
        {
            Result<JsonElement> expanded = vars.Expand(rawArgs);
            if (expanded.IsFailure)
            {
                return Result<JsonElement>.Failure(expanded.Error);
            }

            args = expanded.Value;
        }

        var context = new LabCallContext("clause-" + Guid.NewGuid().ToString("N"), null, "uat:" + vars.RunId);
        LabToolResult result = await _dispatcher.InvokeAsync(tool.Value, args, context, ct);
        if (result.IsError)
        {
            string error = result.Payload.ValueKind == JsonValueKind.Object
                && result.Payload.TryGetProperty("error", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                    ? message.GetString()!
                    : result.Payload.GetRawText();
            return Result<JsonElement>.Failure(error);
        }

        return Result<JsonElement>.Success(result.Payload);
    }
}
