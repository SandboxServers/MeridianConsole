using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>One row of a test bundle. A <see cref="ClauseState"/> adds one required clause; <see cref="Violation"/> adds one.</summary>
internal sealed record RowSpec(string Section, string Id, string Grade, string? ClauseState = null, string? Violation = null,
    bool Allowed = false, string? Reason = null, string CheckId = "I1");

/// <summary>
/// Writes a small evidence bundle in the C9 shape (<c>run.json</c> and <c>rows\&lt;section&gt;\&lt;id&gt;.json</c>) for the ingest
/// tests. Test data only: no secrets, no real paths.
/// </summary>
internal static class BundleFixtures
{
    /// <summary>Writes the bundle folder under <paramref name="uatRuns"/> and returns its path.</summary>
    public static string Write(string uatRuns, string runId, string buildId, long seed, params RowSpec[] rows)
    {
        string folder = Path.Combine(uatRuns, "20261028-" + buildId + "-" + runId);
        Directory.CreateDirectory(folder);

        var run = new JsonObject
        {
            ["schema"] = 1,
            ["run_id"] = runId,
            ["label"] = "test",
            ["seed"] = seed,
            ["started_at"] = "2026-10-28T00:00:00+00:00",
            ["ended_at"] = "2026-10-28T00:01:00+00:00",
            ["build"] = new JsonObject { ["id"] = buildId, ["commit"] = "abc123", ["dirty"] = false, ["checkout"] = "clean" },
            ["fingerprint_sha256"] = "0000",
            ["fidelity"] = "evidence_only",
        };
        File.WriteAllText(Path.Combine(folder, "run.json"), run.ToJsonString());

        foreach (RowSpec row in rows)
        {
            var clauses = new JsonArray();
            if (row.ClauseState is { } state)
            {
                clauses.Add(new JsonObject { ["id"] = "c1", ["state"] = state, ["required"] = true });
            }

            var violations = new JsonArray();
            if (row.Violation is { } invariant)
            {
                violations.Add(new JsonObject
                {
                    ["invariant_id"] = invariant,
                    ["service"] = "nodes",
                    ["check_id"] = row.CheckId,
                    ["allowed"] = row.Allowed,
                    ["detail"] = "detail text",
                    ["at"] = "2026-10-28T00:00:30+00:00",
                });
            }

            var obj = new JsonObject
            {
                ["section"] = row.Section,
                ["id"] = row.Id,
                ["grade"] = row.Grade,
                ["reason"] = row.Reason,
                ["started_at"] = "2026-10-28T00:00:10+00:00",
                ["ended_at"] = "2026-10-28T00:00:20+00:00",
                ["clauses"] = clauses,
                ["violations"] = violations,
            };
            string dir = Path.Combine(folder, "rows", row.Section);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, row.Id + ".json"), obj.ToJsonString());
        }

        return folder;
    }
}
