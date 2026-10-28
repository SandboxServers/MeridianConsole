using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tests.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// <c>results_query</c> reads the index through the results service: views, filters, limits and refusals.
/// </summary>
public sealed class ResultsQueryToolTests : IDisposable
{
    private static readonly LabCallContext Context = new("call-rq", null, "test");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-results-query-" + Path.GetRandomFileName());
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly SecretVault _vault = new();
    private readonly ResultsService _results;
    private readonly ResultsQueryTool _tool;
    private readonly string _uatRuns;

    public ResultsQueryToolTests()
    {
        _uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(_uatRuns);
        _results = new ResultsService(LabPaths.FromRoot(_root, repoRoot: ""), _validator, new Redactor(_vault),
            new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));
        _tool = new ResultsQueryTool(_results);
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
    public async Task Runs_AfterIngest_ReturnsRunWithParsedKnownIssuesAndEvidence()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-q1", "b1", 7, new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld"));
        Result<IngestResult> ingested = await _results.IngestAsync(folder, CancellationToken.None);
        Assert.True(ingested.IsSuccess, ingested.Error);

        JsonElement result = await Query("""{"view":"runs"}""");

        JsonElement item = result.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("run-q1", item.GetProperty("run_id").GetString());
        Assert.False(item.GetProperty("known_issues").GetProperty("checked").GetBoolean());
        Assert.True(item.GetProperty("evidence").TryGetProperty("built_kinds", out _));
        Assert.False(item.TryGetProperty("known_issues_json", out _));
    }

    [Fact]
    public async Task Rows_FilterByGrade_ReturnsOnlyThatGrade()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-q2", "b1", 7,
            new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld"), new RowSpec("nodes", "enroll", "PASS"));
        Assert.True((await _results.IngestAsync(folder, CancellationToken.None)).IsSuccess);

        JsonElement result = await Query("""{"view":"rows","filter":{"grade":"FAIL"}}""");

        JsonElement item = result.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("heartbeat", item.GetProperty("row_id").GetString());
    }

    [Fact]
    public async Task Findings_Empty_ReturnsNoItems()
    {
        JsonElement result = await Query("""{"view":"findings"}""");

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task UnknownView_IsInvalidArgument()
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse("""{"view":"nope"}"""), Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    [Fact]
    public async Task UnknownFilterKey_IsInvalidArgument()
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse("""{"view":"rows","filter":{"x":"y"}}"""), Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("unknown filter key", result.Error);
    }

    [Fact]
    public async Task LimitAboveMax_IsInvalidArgument()
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse("""{"view":"runs","limit":501}"""), Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("limit must be between", result.Error);
    }

    [Fact]
    public async Task FilterValueWithSqlText_IsBoundNotExecuted()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-q3", "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
        Assert.True((await _results.IngestAsync(folder, CancellationToken.None)).IsSuccess);

        JsonElement result = await Query("""{"view":"runs","filter":{"kind":"x' or '1'='1"}}""");

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await Query("""{"view":"runs"}""")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Trend_WithoutKeys_IsInvalidArgument()
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse("""{"view":"trend"}"""), Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("trend needs", result.Error);
    }

    private async Task<JsonElement> Query(string json)
    {
        Result<JsonElement> result = await _tool.InvokeAsync(Parse(json), Context, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
