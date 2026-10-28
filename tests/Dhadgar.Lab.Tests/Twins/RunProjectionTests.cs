using System.Text.Json;
using Dhadgar.Lab.Twins;
using Dhadgar.Lab.Uat;
using Xunit;

namespace Dhadgar.Lab.Tests.Twins;

public sealed class RunProjectionTests
{
    [Fact]
    public void Build_RowGradeAndStepCodes_NoErrorText()
    {
        var steps = new[]
        {
            new UatStepResult("enroll", "nodes_enroll_probe", false, "secret text that must not leak", "Lab.Unavailable", null, 5, null),
            new UatStepResult("check", "http_request", true, null, null, null, 3, null),
        };
        var rows = new[] { new UatRowResult("enroll-once", RowGrade.Fail, "reason", [], steps) };

        var projection = RunProjection.Build(rows);
        string text = projection.ToJsonString();

        Assert.Equal("fail", projection["rows"]!["enroll-once"]!["grade"]!.GetValue<string>());
        Assert.Equal("error", projection["rows"]!["enroll-once"]!["steps"]!["enroll"]!["status"]!.GetValue<string>());
        Assert.Equal("Lab.Unavailable", projection["rows"]!["enroll-once"]!["steps"]!["enroll"]!["error_code"]!.GetValue<string>());
        Assert.Equal("ok", projection["rows"]!["enroll-once"]!["steps"]!["check"]!["status"]!.GetValue<string>());
        Assert.DoesNotContain("secret text", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Captures_Included()
    {
        var rows = new[] { new UatRowResult("r1", RowGrade.Pass, "ok", []) };
        var captures = new Dictionary<string, System.Text.Json.Nodes.JsonObject>(StringComparer.Ordinal)
        {
            ["r1"] = new() { ["node_id"] = "n-1" },
        };

        JsonElement element = JsonDocument.Parse(RunProjection.Build(rows, captures).ToJsonString()).RootElement;

        Assert.Equal("n-1", element.GetProperty("rows").GetProperty("r1").GetProperty("captures").GetProperty("node_id").GetString());
    }
}
