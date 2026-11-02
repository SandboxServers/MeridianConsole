using System.Collections.Generic;
using Dhadgar.Lab.Probe;
using Xunit;

namespace Dhadgar.Lab.Tests.Probe;

/// <summary>
/// The webhook sink's store (L36): <c>Authorization</c> is dropped, credential values are redacted, bodies are cut,
/// and the store keeps only the last <see cref="SinkRecorder.MaxRecords"/> requests.
/// </summary>
public sealed class SinkTests
{
    [Fact]
    public void Sink_DropsAuthorization()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Authorization", "Bearer abc.def.ghi"),
            new("X-Admin-Api-Key", "lab-admin-key-value"),
            new("Content-Type", "application/json"),
        };

        SinkRequest stored = recorder.Record("/webhook", headers, "{}");

        Assert.False(stored.Headers.ContainsKey("Authorization"));
        Assert.Equal("[redacted]", stored.Headers["X-Admin-Api-Key"]);
        Assert.Equal("application/json", stored.Headers["Content-Type"]);
        Assert.DoesNotContain("abc.def.ghi", stored.Headers.Values);
    }

    [Fact]
    public void Redact_RemovesCredentialsFromBodyAndHeaders()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("X-Note", "relay Bearer abc.def.ghi"),
        };

        SinkRequest stored = recorder.Record("/webhook", headers, "{\"client_secret\":\"s3cret-value\",\"content\":\"hi\"}");

        Assert.DoesNotContain("abc.def.ghi", stored.Headers["X-Note"], StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-value", stored.Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", stored.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"hi\"", stored.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_IsCutToTheLimit()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        string body = new string('x', SinkRecorder.MaxBodyChars + 100);

        SinkRequest stored = recorder.Record("/webhook", [], body);

        Assert.Equal(SinkRecorder.MaxBodyChars, stored.Body.Length);
    }

    [Fact]
    public void Store_KeepsOnlyTheLastMaxRecords()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        for (int i = 0; i < SinkRecorder.MaxRecords + 10; i++)
        {
            recorder.Record("/webhook", [], i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        IReadOnlyList<SinkRequest> all = recorder.Since(0);

        Assert.Equal(SinkRecorder.MaxRecords, all.Count);
        Assert.Equal(11, all[0].Seq);
        Assert.Equal(SinkRecorder.MaxRecords + 10, all[^1].Seq);
    }

    [Fact]
    public void Since_ReturnsOnlyNewerRequests_AndClearKeepsCounting()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        recorder.Record("/a", [], "1");
        recorder.Record("/b", [], "2");

        Assert.Single(recorder.Since(1));

        recorder.Clear();
        Assert.Empty(recorder.Since(0));

        SinkRequest next = recorder.Record("/c", [], "3");
        Assert.Equal(3, next.Seq);
    }
}
