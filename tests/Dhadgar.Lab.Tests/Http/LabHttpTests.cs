using System.Net;
using System.Net.Http;
using System.Text;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Http;

/// <summary>
/// <c>http_request</c> behaviour without a live stack: the lab-endpoint check, the Host override refusal, redirects and
/// placeholder redaction. The fake handler records every request that would have left the process.
/// </summary>
public sealed class LabHttpTests
{
    [Fact]
    public async Task HttpRequest_NonLabHost_IsRefused()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var vault = new SecretVault();
        var http = CreateProbe(handler, vault);

        Result<HttpProbeResult> result = await http.SendAsync("gateway", "GET", "//evil.example/steal", null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Untrusted]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HttpRequest_HostHeaderOverride_IsRefused()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var vault = new SecretVault();
        var http = CreateProbe(handler, vault);
        var headers = new Dictionary<string, string> { ["Host"] = "evil.example" };

        Result<HttpProbeResult> result = await http.SendAsync("gateway", "GET", "/api/v1/health", headers, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HttpRequest_Redirect_IsNotFollowed()
    {
        using var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri($"http://127.0.0.1:{LabPorts.Gateway}/moved");
            return response;
        });
        using var vault = new SecretVault();
        var http = CreateProbe(handler, vault);

        Result<HttpProbeResult> result = await http.SendAsync("gateway", "GET", "/old", null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(302, result.Value.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void CreateHandler_Production_DisallowsAutoRedirect()
    {
        using HttpMessageHandler created = LabHttp.CreateHandler();
        var handler = Assert.IsType<SocketsHttpHandler>(created);

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task HttpRequest_SecretPlaceholder_IsResolvedAndRedactedInEcho()
    {
        const string secret = "s3cret-value-123";
        using var handler = new RecordingHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), Encoding.UTF8, "application/json"),
        });
        using var vault = new SecretVault();
        string reference = vault.Add(SecretKind.Password, secret.AsSpan());
        var http = CreateProbe(handler, vault);
        using var body = System.Text.Json.JsonDocument.Parse("{\"password\":\"${secret:" + reference + "}\"}");

        Result<HttpProbeResult> result = await http.SendAsync("gateway", "POST", "/login", null, body.RootElement.Clone(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(secret, handler.LastBody, StringComparison.Ordinal);
        string echoed = result.Value.Body!.Value.GetRawText();
        Assert.DoesNotContain(secret, echoed, StringComparison.Ordinal);
        Assert.Contains("***" + reference, echoed, StringComparison.Ordinal);
    }

    private static LabHttp CreateProbe(HttpMessageHandler handler, SecretVault vault) =>
        new(new StubFactory(handler), vault, new Redactor(vault), new FakeTimeProvider());

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
