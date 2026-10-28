using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "Both tasks are awaited inside the try before the listeners leave scope; the analyzer cannot see the awaits.")]
    public async Task CreateHandler_IgnoresHttpProxyEnvironment_AndReachesLoopbackDirectly()
    {
        // A host HTTP_PROXY must not capture lab requests to 127.0.0.1: the proxy listener must see nothing and the target must answer.
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        using var target = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        target.Start();
        int proxyPort = ((IPEndPoint)proxy.LocalEndpoint).Port;
        int targetPort = ((IPEndPoint)target.LocalEndpoint).Port;

        string? previous = Environment.GetEnvironmentVariable("HTTP_PROXY");
        Environment.SetEnvironmentVariable("HTTP_PROXY", $"http://127.0.0.1:{proxyPort}");
        try
        {
            using HttpMessageHandler handler = LabHttp.CreateHandler();
            using var client = new HttpClient(handler);

            Task<HttpResponseMessage> send = client.GetAsync($"http://127.0.0.1:{targetPort}/healthz");
            Task<string> served = ServeOneRequestAsync(target);

            string requestLine = await served.WaitAsync(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await send.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("GET /healthz HTTP/1.1", requestLine);
            Assert.False(proxy.Pending(), "the request was routed through HTTP_PROXY");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", previous);
        }
    }

    private static async Task<string> ServeOneRequestAsync(TcpListener listener)
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = accepted.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string requestLine = await reader.ReadLineAsync() ?? string.Empty;
        string? header;
        do
        {
            header = await reader.ReadLineAsync();
        }
        while (!string.IsNullOrEmpty(header));

        byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(reply);
        return requestLine;
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
