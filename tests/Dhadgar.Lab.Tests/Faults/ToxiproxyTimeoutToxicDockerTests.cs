using System.Net.Sockets;
using System.Text;
using Dhadgar.Lab.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// Checks the C19 assumption behind the mTLS fallback: a Toxiproxy <c>timeout</c> toxic with <c>timeout = 0</c> added AFTER a
/// connection is open also holds that connection's data. Needs the lab stack's <c>agent-ingress</c> proxy (25000). If the second
/// request completes, the assumption is false: delete this test and record "Toxiproxy does not apply new toxics to open connections".
/// </summary>
[Trait("RequiresDocker", "true")]
public sealed class ToxiproxyTimeoutToxicDockerTests
{
    private const string Proxy = "agent-ingress";
    private const string ToxicName = "l51-timeout";
    private const int Port = 25000;
    private static readonly byte[] Request = Encoding.ASCII.GetBytes("GET /healthz HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: keep-alive\r\n\r\n");

    [Fact]
    public async Task TimeoutToxic_AddedToAnOpenConnection_HoldsTheNextRequest()
    {
        using var toxiproxy = new ToxiproxyClient();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(Request);
        string first = await ReadSomeAsync(stream, TimeSpan.FromSeconds(10));
        Assert.StartsWith("HTTP/1.1 200", first, StringComparison.Ordinal);

        var spec = new ToxicSpec(ToxicName, "timeout", "downstream", 1.0, new Dictionary<string, long> { ["timeout"] = 0 });
        Result added = await toxiproxy.AddToxicAsync(Proxy, spec, CancellationToken.None);
        Assert.True(added.IsSuccess, added.Error);
        try
        {
            await stream.WriteAsync(Request);
            string second = await ReadSomeAsync(stream, TimeSpan.FromSeconds(3));
            Assert.Equal(string.Empty, second);
        }
        finally
        {
            _ = await toxiproxy.RemoveToxicAsync(Proxy, ToxicName, CancellationToken.None);
        }
    }

    private static async Task<string> ReadSomeAsync(NetworkStream stream, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        var buffer = new byte[4096];
        try
        {
            int read = await stream.ReadAsync(buffer, cts.Token);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
