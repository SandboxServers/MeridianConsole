using System.Text.Json.Nodes;
using Dhadgar.Lab.Uat;

namespace Dhadgar.Lab.Twins;

/// <summary>
/// Builds the run projection that C19 pointers resolve against:
/// <c>{rows:{&lt;row id&gt;:{grade, steps:{&lt;step id&gt;:{status, error_code, outcome}}, captures:{&lt;var&gt;:&lt;value&gt;}}}}</c>.
/// Only codes and grades go in: step error text is never copied, so nothing a redactor missed reaches a comparison.
/// </summary>
public static class RunProjection
{
    /// <summary>
    /// Projects a finished run's rows.
    /// </summary>
    /// <param name="rows">The graded rows of the run, in run order.</param>
    /// <param name="captures">Captured values per row id, already redacted by the bundle writer; optional.</param>
    public static JsonObject Build(IReadOnlyList<UatRowResult> rows, IReadOnlyDictionary<string, JsonObject>? captures = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var rowObjects = new JsonObject();
        foreach (UatRowResult row in rows)
        {
            var steps = new JsonObject();
            foreach (UatStepResult step in row.Steps ?? [])
            {
                string status = step.Ok ? "ok" : "error";
                steps[step.Id] = new JsonObject
                {
                    ["status"] = status,
                    ["error_code"] = step.ErrorCode,
                    ["outcome"] = step.ExpectedError is null ? status : "expected_error",
                };
            }

            var rowObject = new JsonObject
            {
                ["grade"] = row.Grade.ToString().ToLowerInvariant(),
                ["steps"] = steps,
                ["captures"] = captures is not null && captures.TryGetValue(row.RowId, out JsonObject? values)
                    ? values.DeepClone()
                    : new JsonObject(),
            };
            rowObjects[row.RowId] = rowObject;
        }

        return new JsonObject { ["rows"] = rowObjects };
    }
}
