using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Coverage;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// L52: <c>coverage_report</c> computes unhit keys as the universe minus the hits; a 5xx or an unmatched path is listed as
/// outside the universe and never counts as a hit. The universe is read from the source folder (the tool's test seam).
/// </summary>
public sealed class CoverageReportToolTests : IDisposable
{
    private const string EnrollStatus2xx = "nodes POST /api/v1/agents/enroll 2xx";
    private const string EnrollStatus4xx = "nodes POST /api/v1/agents/enroll 4xx";
    private const string EnrollStatus5xx = "nodes POST /api/v1/agents/enroll 5xx";
    private const string GatewayAnyEnroll2xx = "gateway * /api/v1/agents/enroll 2xx";

    private static readonly LabCallContext Context = new("call-cov-report", null, "test");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-coverage-report-" + Path.GetRandomFileName());
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly SecretVault _vault = new();
    private readonly ResultsService _results;
    private readonly CoverageReportTool _tool;

    public CoverageReportToolTests()
    {
        Directory.CreateDirectory(_root);
        _results = new ResultsService(LabPaths.FromRoot(_root, repoRoot: ""), _validator, new Redactor(_vault),
            new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));
        string labRoot = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Tools", "Dhadgar.Lab");
        _tool = new CoverageReportTool(_results, _validator, labRoot);
    }

    public void Dispose()
    {
        _results.Dispose();
        _vault.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // The temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task Hit_CoversItsRouteStatusAndLeavesTheOthersUnhit()
    {
        await WriteHits(("route_status", EnrollStatus2xx, 3));

        JsonElement route = await DimensionOf("route_status");

        Assert.Equal(1, route.GetProperty("hit").GetInt32());
        string[] unhit = Unhit(route);
        Assert.DoesNotContain(EnrollStatus2xx, unhit);
        Assert.Contains(EnrollStatus4xx, unhit);
        Assert.Equal(route.GetProperty("total").GetInt32() - unhit.Length, route.GetProperty("hit").GetInt32());
    }

    [Fact]
    public async Task WildcardRoute_IsCoveredByAHitOfAnyMethod()
    {
        await WriteHits(("route_status", "gateway POST /api/v1/agents/enroll 2xx", 1));

        JsonElement route = await DimensionOf("route_status");

        Assert.DoesNotContain(GatewayAnyEnroll2xx, Unhit(route));
    }

    [Fact]
    public async Task ServerError_IsOutsideTheUniverseAndNeverAHit()
    {
        await WriteHits(("route_status", EnrollStatus5xx, 4));

        JsonElement result = await Report(new { kind = "route_status" });

        JsonElement route = result.GetProperty("dimensions")[0];
        Assert.Equal(0, route.GetProperty("hit").GetInt32());
        Assert.Contains(EnrollStatus2xx, Unhit(route));
        JsonElement outside = Assert.Single(result.GetProperty("outside_universe").EnumerateArray());
        Assert.Equal(EnrollStatus5xx, outside.GetProperty("key").GetString());
        Assert.Equal(4, outside.GetProperty("hits").GetInt64());
    }

    [Fact]
    public async Task UnmatchedPath_IsOutsideTheUniverseWithoutThePath()
    {
        await WriteHits(("route_status", "nodes GET ?unmatched 4xx", 2));

        JsonElement result = await Report(new { kind = "route_status" });

        JsonElement outside = Assert.Single(result.GetProperty("outside_universe").EnumerateArray());
        Assert.Equal("nodes GET ?unmatched 4xx", outside.GetProperty("key").GetString());
        Assert.Equal(0, result.GetProperty("dimensions")[0].GetProperty("hit").GetInt32());
    }

    [Fact]
    public async Task NoHits_EveryUniverseKeyIsUnhit()
    {
        JsonElement route = await DimensionOf("route_status");

        Assert.Equal(0, route.GetProperty("hit").GetInt32());
        Assert.Equal(route.GetProperty("total").GetInt32(), Unhit(route).Length);
    }

    [Fact]
    public async Task KindFilter_ReturnsOnlyThatDimension_AndAKindWithoutAUniverseHasANullTotal()
    {
        await WriteHits(("invariant_check", "I1/unhandled", 2), ("route_status", EnrollStatus2xx, 1));

        JsonElement result = await Report(new { kind = "invariant_check" });

        JsonElement only = Assert.Single(result.GetProperty("dimensions").EnumerateArray());
        Assert.Equal("invariant_check", only.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("total").ValueKind);
        Assert.Equal(1, only.GetProperty("hit").GetInt32());
    }

    [Fact]
    public async Task UnknownKind_IsRefused()
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse("""{"kind":"not_a_kind"}"""), Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    /// <summary>Writes the hits of one run (all in one replace, so the run's rows are the given set).</summary>
    private async Task WriteHits(params (string Kind, string Key, long Hits)[] hits)
    {
        IReadOnlyList<CoverageRecord> records = hits.Select(h => new CoverageRecord("run-1", h.Kind, h.Key, h.Hits)).ToList();
        Result written = await _results.WriteAsync(db => db.ReplaceCoverage("run-1", records), CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error);
    }

    private async Task<JsonElement> DimensionOf(string kind)
    {
        JsonElement result = await Report(new { kind });
        return Assert.Single(result.GetProperty("dimensions").EnumerateArray());
    }

    private async Task<JsonElement> Report(object arguments)
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse(JsonSerializer.Serialize(arguments)), Context, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static string[] Unhit(JsonElement dimension) =>
        dimension.GetProperty("unhit").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
