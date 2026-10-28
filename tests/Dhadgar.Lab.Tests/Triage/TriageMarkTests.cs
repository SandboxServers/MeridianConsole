using System.Text.Json;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Triage;

/// <summary>
/// <c>triage_mark</c>, <c>triage_show</c> and <c>results_known_issue_set</c>: argument refusals and the states they write.
/// </summary>
public sealed class TriageMarkTests : IDisposable
{
    private static readonly LabCallContext Context = new("call-mark", null, "test");

    private readonly TriageToolsFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData("https://github.com/SandboxServers/MeridianConsole/issues/94", true)]
    [InlineData("https://github.com/SandboxServers/OtherRepo/issues/94", false)]
    [InlineData("https://github.com/evil/MeridianConsole/issues/94", false)]
    [InlineData("https://evil.example/SandboxServers/MeridianConsole/issues/94", false)]
    [InlineData("http://github.com/SandboxServers/MeridianConsole/issues/94", false)]
    [InlineData("https://github.com/SandboxServers/MeridianConsole/issues/94/extra", false)]
    [InlineData("https://github.com/SandboxServers/MeridianConsole/issues/abc", false)]
    [InlineData("https://github.com/SandboxServers/MeridianConsole/issues/94\n", false)]
    public void IssueUrl_OnlyTheProjectIssuesPass(string candidate, bool expected) =>
        Assert.Equal(expected, TriageMarkTool.IsIssueUrl(candidate));

    [Fact]
    public async Task Mark_OtherHostOrRepo_IsRefusedAndNothingChanges()
    {
        await _fixture.PlantSignatureAsync("sig-m1", "row fails");
        var tool = new TriageMarkTool(_fixture.Results);

        Result<JsonElement> result = await tool.InvokeAsync(
            Args(new { bucket_id = "sig-m1", state = "filed", issue_url = "https://evil.example/SandboxServers/MeridianConsole/issues/1" }),
            Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Result<ResultsDb.BucketRecord> bucket = await _fixture.Results.ReadAsync(db => db.BucketOf("sig-m1"), CancellationToken.None);
        Assert.Equal("new", bucket.Value.State);
        Assert.Null(bucket.Value.IssueLink);
    }

    [Fact]
    public async Task Mark_ValidUrl_SetsStateAndLink()
    {
        await _fixture.PlantSignatureAsync("sig-m2", "row fails");
        var tool = new TriageMarkTool(_fixture.Results);
        const string url = "https://github.com/SandboxServers/MeridianConsole/issues/94";

        Result<JsonElement> result = await tool.InvokeAsync(Args(new { bucket_id = "sig-m2", state = "filed", issue_url = url }),
            Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("filed", result.Value.GetProperty("state").GetString());
        Result<ResultsDb.BucketRecord> bucket = await _fixture.Results.ReadAsync(db => db.BucketOf("sig-m2"), CancellationToken.None);
        Assert.Equal("filed", bucket.Value.State);
        Assert.Equal(url, bucket.Value.IssueLink);
    }

    [Fact]
    public async Task Mark_UnknownState_IsRefused()
    {
        await _fixture.PlantSignatureAsync("sig-m3", "row fails");
        var tool = new TriageMarkTool(_fixture.Results);

        Result<JsonElement> result = await tool.InvokeAsync(Args(new { bucket_id = "sig-m3", state = "closed" }), Context,
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Show_ReturnsTheBucketFields()
    {
        await _fixture.PlantSignatureAsync("sig-v1", "row fails: heartbeat");
        var tool = new TriageShowTool(_fixture.Results);

        Result<JsonElement> result = await tool.InvokeAsync(Args(new { bucket_id = "sig-v1" }), Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("sig-v1", result.Value.GetProperty("bucket").GetString());
        Assert.Equal("row fails: heartbeat", result.Value.GetProperty("signature").GetString());
        Assert.Equal(1, result.Value.GetProperty("hits").GetInt64());
        Assert.Equal("run-plant", result.Value.GetProperty("runs")[0].GetString());
    }

    [Fact]
    public async Task KnownIssueSet_IdNotInTheShippedFile_IsRefused()
    {
        await _fixture.PlantSignatureAsync("sig-k1", "row fails");
        var tool = new ResultsKnownIssueSetTool(_fixture.Results);

        Result<JsonElement> result = await tool.InvokeAsync(Args(new { bucket_id = "sig-k1", known_issue_id = "KI-NOPE" }), Context,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Result<ResultsDb.BucketRecord> bucket = await _fixture.Results.ReadAsync(db => db.BucketOf("sig-k1"), CancellationToken.None);
        Assert.Null(bucket.Value.KnownIssueId);
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
}
