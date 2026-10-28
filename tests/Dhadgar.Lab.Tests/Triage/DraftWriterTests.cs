using System.Text.Json;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Triage;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Triage;

/// <summary>
/// Issue drafts: the golden text, the closing line, the secret refusal and the draft path.
/// </summary>
public sealed class DraftWriterTests : IDisposable
{
    private static readonly LabCallContext Context = new("call-draft", null, "test");

    private readonly TriageToolsFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Render_Golden_HasEverySectionAndEndsWithTheDraftOnlyLine()
    {
        var bucket = new ResultsDb.BucketRecord("sig-g1", "row", "2026-10-27T00:00:00Z", "2026-10-28T00:00:00Z", 4, "new", null, null,
            "new", "C:\\bundle\\repro\\repro.toml", null);
        var source = new DraftWriter.DraftSource(bucket, "row fails: heartbeat", "run-1", "run-4", "build-9", 3);

        string text = DraftWriter.Render(source);

        const string expected = """
            # row failure: row fails: heartbeat

            - Bucket: sig-g1
            - Signature: row fails: heartbeat
            - First seen: 2026-10-27T00:00:00Z (run run-1)
            - Last seen: 2026-10-28T00:00:00Z (run run-4)
            - Hits: 4
            - Build: build-9
            - Seed: 3
            - Repro: C:\bundle\repro\repro.toml

            ## Replay

            ```text
            meridian-lab uat run --replay C:\bundle\repro\repro.toml
            ```

            ## Excerpt (redacted)

            ```text
            row fails: heartbeat
            ```

            Draft only. Filed by the owner after approval (D-ML28, D-ML42).

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), text);
        Assert.EndsWith(DraftWriter.DraftOnlyLine + "\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_ForABucket_IsWrittenUnderTriageAndRecordedOnTheFinding()
    {
        await _fixture.PlantSignatureAsync("sig-d1", "row fails: heartbeat");
        var tool = new TriageDraftIssueTool(_fixture.Writer);

        Result<JsonElement> result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { bucket_id = "sig-d1" }), Context,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string path = result.Value.GetProperty("draft_path").GetString()!;
        Assert.StartsWith(_fixture.Paths.Triage, path, StringComparison.OrdinalIgnoreCase);
        string text = await File.ReadAllTextAsync(path);
        Assert.EndsWith("Draft only. Filed by the owner after approval (D-ML28, D-ML42).\n", text.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Result<ResultsDb.BucketRecord> bucket = await _fixture.Results.ReadAsync(db => db.BucketOf("sig-d1"), CancellationToken.None);
        Assert.Equal("drafted", bucket.Value.FindingState);
        Assert.Equal(path, bucket.Value.DraftPath);
    }

    [Fact]
    public async Task Draft_PlantedSecret_IsRefused()
    {
        _ = _fixture.Vault.Add(SecretKind.Password, TriageToolsFixture.PlantedSecret.AsSpan());
        await _fixture.PlantSignatureAsync("sig-s1", "row fails with " + TriageToolsFixture.PlantedSecret);

        Result<string> result = await _fixture.Writer.WriteAsync("sig-s1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(TriageToolsFixture.PlantedSecret, result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_fixture.Paths.Triage, "drafts", "sig-s1.md")));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task Draft_BucketIdThatIsNotAFileName_IsRefused(string bucketId)
    {
        Result<string> result = await _fixture.Writer.WriteAsync(bucketId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_UnknownBucket_IsNotFound()
    {
        Result<string> result = await _fixture.Writer.WriteAsync("sig-missing", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
    }
}
