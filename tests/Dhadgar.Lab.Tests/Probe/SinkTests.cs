using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Lab.Probe;
using Microsoft.AspNetCore.Builder;
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
        Assert.DoesNotContain(stored.Headers.Values, v => v.Contains("abc.def.ghi", StringComparison.Ordinal));
    }

    [Fact]
    public void Sink_RedactsSecretNamedHeaders_AndKeepsPlainOnes()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("X-Api-Key", "k3y-123"),
            new("X-Auth-Token", "eyJhbGciOi"),
            new("X-Amz-Security-Token", "session-value"),
            new("Content-Type", "application/json"),
            new("User-Agent", "lab-probe/1.0"),
        };

        SinkRequest stored = recorder.Record("/webhook", headers, "{}");

        Assert.Equal("[redacted]", stored.Headers["X-Api-Key"]);
        Assert.Equal("[redacted]", stored.Headers["X-Auth-Token"]);
        Assert.Equal("[redacted]", stored.Headers["X-Amz-Security-Token"]);
        Assert.Equal("application/json", stored.Headers["Content-Type"]);
        Assert.Equal("lab-probe/1.0", stored.Headers["User-Agent"]);
    }

    [Fact]
    public void Redact_RemovesCredentialsFromBodyAndHeaders()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("X-Note", "relay Bearer abc.def.ghi"),
        };

        SinkRequest stored = recorder.Record(
            "/webhook",
            headers,
            "{\"client_secret\":\"s3cret-value\",\"password\":\"my pass word\",\"content\":\"hi\"}");

        Assert.DoesNotContain("abc.def.ghi", stored.Headers["X-Note"], StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-value", stored.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pass word", stored.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("my pass", stored.Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", stored.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"hi\"", stored.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_CoversSpacedPasswordsEscapedJson_BasicAndTruncatedPem()
    {
        string body = @"{""password"":""correct horse battery""}"
            + @"{""payload"":""{\""token\"":\""abc123\""}""}"
            + "authorization: Basic dXNlcjpwdw==;"
            + "bearer lowerCaseTok.en"
            + "\nauthorization: Bearer hdrTok.en.value;"
            + "\n-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEF";

        string redacted = SinkRecorder.Redact(body);

        Assert.DoesNotContain("correct horse", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("battery", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("dXNlcjpwdw==", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("lowerCaseTok.en", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hdrTok.en.value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("MIIEvQ", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("authorization: Bearer abc123SECRET")]
    [InlineData("Authorization=Bearer abc123SECRET&x=1")]
    [InlineData("bearer abc123SECRET")]
    public void Redact_BearerAfterAuthorizationKey_IsRedacted(string input)
    {
        string redacted = SinkRecorder.Redact(input);

        Assert.DoesNotContain("abc123SECRET", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"password\":\"correct horse batt")]
    [InlineData("{\"a\":1,\"token\":\"abc123SEC")]
    [InlineData("{\"a\":1,\"token\":\"abc123SEC\\")]
    public void Redact_UnclosedQuotedValue_IsRedacted(string input)
    {
        string redacted = SinkRecorder.Redact(input);

        Assert.DoesNotContain("correct horse", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123SEC", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_SecretStraddlingTheBodyCut_IsNotStored()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        string body = new string('x', SinkRecorder.MaxBodyChars - 20) + "\"token\":\"abc123SECRETSECRET";

        SinkRequest stored = recorder.Record("/webhook", [], body);

        Assert.DoesNotContain("abc123", stored.Body, StringComparison.Ordinal);
        Assert.EndsWith("\"token\":\"[redacted]", stored.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_StripsDiscordWebhookTokenFromPath()
    {
        var recorder = new SinkRecorder(TimeProvider.System);

        SinkRequest stored = recorder.Record("/api/webhooks/123/abcDEF.ghi", [], "{}");
        SinkRequest versioned = recorder.Record("/api/v10/webhooks/123/abcDEF.ghi", [], "{}");

        Assert.DoesNotContain("abcDEF.ghi", stored.Path, StringComparison.Ordinal);
        Assert.StartsWith("/api/webhooks/123/", stored.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("abcDEF.ghi", versioned.Path, StringComparison.Ordinal);
        Assert.StartsWith("/api/v10/webhooks/123/", versioned.Path, StringComparison.Ordinal);
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

    [Fact]
    public async Task SinkHost_PostThenGet_ReturnsRecordedRequestWithoutAuthorization()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        await using WebApplication app = SinkHost.Build(recorder, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var post = new HttpRequestMessage(HttpMethod.Post, "/webhook")
        {
            Content = new StringContent("{\"content\":\"hi\"}", Encoding.UTF8, "application/json"),
        };
        post.Headers.TryAddWithoutValidation("Authorization", "Bearer abc.def.ghi");
        using HttpResponseMessage posted = await http.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        using HttpResponseMessage listed = await http.GetAsync("/_sink/requests?since=0");
        string json = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("abc.def.ghi", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", json, StringComparison.OrdinalIgnoreCase);

        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal("/webhook", doc.RootElement[0].GetProperty("path").GetString());
            Assert.Contains("\"content\":\"hi\"", doc.RootElement[0].GetProperty("body").GetString(), StringComparison.Ordinal);
        }

        using HttpResponseMessage cleared = await http.DeleteAsync("/_sink/requests");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        using HttpResponseMessage empty = await http.GetAsync("/_sink/requests?since=0");
        Assert.Equal("[]", await empty.Content.ReadAsStringAsync());

        using HttpResponseMessage negative = await http.GetAsync("/_sink/requests?since=-1");
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task SinkHost_LargeBody_IsReadOnlyUpToTheLimit()
    {
        var recorder = new SinkRecorder(TimeProvider.System);
        await using WebApplication app = SinkHost.Build(recorder, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var post = new HttpRequestMessage(HttpMethod.Post, "/webhook")
        {
            Content = new StringContent(new string('y', 1024 * 1024), Encoding.UTF8, "text/plain"),
        };
        using HttpResponseMessage posted = await http.SendAsync(post);

        IReadOnlyList<SinkRequest> stored = recorder.Since(0);
        Assert.Single(stored);
        Assert.True(stored[0].Body.Length <= SinkRecorder.MaxBodyChars);
    }

    [Fact]
    public async Task ReadBoundedAsync_ReadsOnlyUpToTheLimit_NotTheWholeBody()
    {
        var body = new CountingStream(new string('y', 1024 * 1024));

        string text = await SinkHost.ReadBoundedAsync(body, CancellationToken.None);

        Assert.Equal(SinkRecorder.MaxBodyChars, text.Length);
        Assert.True(
            body.TotalBytesRead <= SinkRecorder.MaxBodyChars * 4 + 4096,
            $"read {body.TotalBytesRead} bytes; the bound is {SinkRecorder.MaxBodyChars * 4 + 4096}");
    }

    /// <summary>A read-only stream over ASCII text that counts the bytes handed out.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public CountingStream(string text)
        {
            _data = Encoding.UTF8.GetBytes(text);
        }

        public long TotalBytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n = Math.Min(buffer.Length, _data.Length - _position);
            _data.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            TotalBytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
