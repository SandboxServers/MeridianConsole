using System.Text.Json;
using Dhadgar.Lab.Twins;
using Xunit;

namespace Dhadgar.Lab.Tests.Twins;

public sealed class TwinComparerTests
{
    private const string EqualSpec = """
        schema = 1
        id = "t-equal"
        relation = "equal_outcome"
        [base]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        [variant]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        [[compare]]
        pointer = "/rows/enroll-once/grade"
        [[compare]]
        pointer = "/rows/enroll-once/steps/enroll/status"
        """;

    private const string IsolatedSpec = """
        schema = 1
        id = "t-isolated"
        relation = "isolated"
        [base]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        [variant]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        [[compare]]
        variant_pointers = ["/a", "/b"]
        op = "disjoint"
        """;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void GuidsMasked_Equal()
    {
        var spec = TwinSpecLoader.Load(EqualSpec).Value!;
        JsonElement baseProjection = Json("""{"rows":{"enroll-once":{"grade":"pass","steps":{"enroll":{"status":"201","id":"1b4e28ba-2fa1-11d2-883f-0016d3cca427"}}}}}""");
        JsonElement variant = Json("""{"rows":{"enroll-once":{"grade":"pass","steps":{"enroll":{"status":"201","id":"9c858901-8a57-4791-81fe-4c455b099bc9"}}}}}""");

        var result = TwinComparer.Compare(spec, baseProjection, variant);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Held);
        Assert.Empty(result.Value.Diffs);
    }

    [Fact]
    public void TimestampsAndDurationsMasked_Equal()
    {
        var spec = TwinSpecLoader.Load(EqualSpec).Value!;
        JsonElement baseProjection = Json("""{"rows":{"enroll-once":{"grade":"pass","steps":{"enroll":{"status":"201","at":"2026-10-28T10:00:00.123Z","duration_ms":12}}}}}""");
        JsonElement variant = Json("""{"rows":{"enroll-once":{"grade":"pass","steps":{"enroll":{"status":"201","at":"2026-10-28T11:30:00Z","duration_ms":980}}}}}""");

        var result = TwinComparer.Compare(spec, baseProjection, variant);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Held);
    }

    [Fact]
    public void GradeDiffers_NotHeld()
    {
        var spec = TwinSpecLoader.Load(EqualSpec).Value!;
        JsonElement baseProjection = Json("""{"rows":{"enroll-once":{"grade":"pass","steps":{"enroll":{"status":"201"}}}}}""");
        JsonElement variant = Json("""{"rows":{"enroll-once":{"grade":"fail","steps":{"enroll":{"status":"201"}}}}}""");

        var result = TwinComparer.Compare(spec, baseProjection, variant);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.Held);
        Assert.Single(result.Value.Diffs);
        Assert.Equal("/rows/enroll-once/grade", result.Value.Diffs[0].Locator);
    }

    [Fact]
    public void Disjoint_Overlap_NotHeld()
    {
        var spec = TwinSpecLoader.Load(IsolatedSpec).Value!;
        JsonElement variant = Json("""{"a":["t1","t2"],"b":["t2","t3"]}""");

        var result = TwinComparer.Compare(spec, Json("{}"), variant);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.Held);
    }

    [Fact]
    public void Disjoint_NoOverlap_Held()
    {
        var spec = TwinSpecLoader.Load(IsolatedSpec).Value!;
        JsonElement variant = Json("""{"a":["t1"],"b":["t2"]}""");

        var result = TwinComparer.Compare(spec, Json("{}"), variant);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Held);
    }

    [Fact]
    public void Resolve_EscapedSegments_Resolve()
    {
        JsonElement root = Json("""{"a/b":{"c~d":5},"list":[10,20]}""");

        Assert.Equal(5, TwinComparer.Resolve(root, "/a~1b/c~0d")!.Value.GetInt32());
        Assert.Equal(20, TwinComparer.Resolve(root, "/list/1")!.Value.GetInt32());
        Assert.Null(TwinComparer.Resolve(root, "/missing"));
    }
}
