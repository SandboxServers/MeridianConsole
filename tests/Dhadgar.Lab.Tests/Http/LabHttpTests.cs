using System.Net;
using System.Net.Sockets;
using System.Text;
using Dhadgar.Lab.Http;
using Xunit;

namespace Dhadgar.Lab.Tests.Http;

public sealed class LabHttpTests
{
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
}
