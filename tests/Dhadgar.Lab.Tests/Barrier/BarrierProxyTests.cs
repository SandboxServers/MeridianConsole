using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Dhadgar.Lab.Barrier;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Tests.Barrier;

/// <summary>
/// The lab barrier proxy (L49): real sockets, a test upstream on <see cref="LabPorts.AgentIngress"/> (the only lab endpoint the
/// proxy accepts), and a recording observation sink. The upstream and the proxy live in one class so their tests run one at a time.
/// </summary>
public sealed class BarrierProxyTests
{
    private const string RunId = "run-barrier";
    private const string Actor = "client:1";
    private static readonly Uri UpstreamUri = new($"http://127.0.0.1:{LabPorts.AgentIngress.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    [Fact]
    public async Task HoldRequest_ReleasedTogether()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.HoldRequest, "start"), Rule(2, BarrierAction.HoldRequest, "start"), Rule(3, BarrierAction.HoldRequest, "start")]).IsSuccess);

        Task<HttpResponseMessage>[] sends = [Send(proxy, 1), Send(proxy, 2), Send(proxy, 3)];
        Result waited = await proxy.WaitArrivedAsync("start", 3, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(waited.IsSuccess, waited.Error);
        Assert.Equal(0, up.Hits);

        Assert.True(proxy.Release("start").IsSuccess);
        HttpResponseMessage[] responses = await Task.WhenAll(sends);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(3, up.Hits);
    }

    [Fact]
    public async Task DropResponse_UpstreamCompletedClientUnknown()
    {
        await using Upstream up = await Upstream.StartAsync(201);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.DropResponse, null)]).IsSuccess);

        await Assert.ThrowsAsync<HttpRequestException>(() => Send(proxy, 1));

        Assert.Equal(1, up.Hits);
        LabObservation completed = Assert.Single(sink.Lines, line => line.Op == "proxy_upstream_completed");
        Assert.Equal(201, completed.Args.GetProperty("status").GetInt32());
        Assert.Contains(sink.Lines, line => line.Op == "proxy_response_dropped");
    }

    [Fact]
    public async Task NoOpHeader_Refused400()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using HttpResponseMessage response = await SendRawAsync(proxy, opId: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lab.barrier.no_op", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, up.Hits);
    }

    [Fact]
    public async Task UnknownOpId_Refused400()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using HttpResponseMessage response = await SendRawAsync(proxy, OpText(9));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, up.Hits);
    }

    [Fact]
    public async Task OpHeader_StrippedBeforeUpstream()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using HttpResponseMessage response = await Send(proxy, 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyDictionary<string, string> seen = Assert.Single(up.Seen);
        Assert.False(seen.ContainsKey("X-Lab-Op-Id"));
        Assert.True(response.Headers.Contains("X-Upstream-Probe"));
    }

    [Fact]
    public async Task HopByHopHeaders_Stripped()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyUri(proxy, "/probe"));
        request.Headers.Add("X-Lab-Op-Id", OpText(1));
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic x");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyDictionary<string, string> seen = Assert.Single(up.Seen);
        Assert.False(seen.ContainsKey("Keep-Alive"));
        Assert.False(seen.ContainsKey("Proxy-Authorization"));
    }

    [Fact]
    public async Task NonLabUpstream_Refused()
    {
        var sink = new RecordingSink();
        Result<BarrierProxy> started = await BarrierProxy.StartAsync(new Uri("http://example.com"), 0, sink, TimeProvider.System, CancellationToken.None);

        Assert.True(started.IsFailure);
        Assert.Contains("[Lab.BlastRadius]", started.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Port_NotBarrier_Refused()
    {
        var sink = new RecordingSink();
        Result<BarrierProxy> started = await BarrierProxy.StartAsync(UpstreamUri, 25081, sink, TimeProvider.System, CancellationToken.None);

        Assert.True(started.IsFailure);
        Assert.Contains("[Lab.BlastRadius]", started.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldRequest_Timeout_504()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        Result<BarrierProxy> started = await BarrierProxy.StartWithHoldCapAsync(
            UpstreamUri, 0, sink, TimeProvider.System, TimeSpan.FromMilliseconds(500), null, CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error);
        await using BarrierProxy proxy = started.Value;
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.HoldRequest, "never")]).IsSuccess);

        using HttpResponseMessage response = await Send(proxy, 1);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("lab.barrier.timeout", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, up.Hits);
    }

    [Fact]
    public async Task Body_Over1MiB_413()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ProxyUri(proxy, "/probe"))
        {
            Content = new ByteArrayContent(new byte[BarrierProxy.MaxBodyBytes + 1]),
        };
        request.Headers.Add("X-Lab-Op-Id", OpText(1));
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, up.Hits);
    }

    [Fact]
    public async Task BarrierProxy_HoldsItsPortWhileRunning()
    {
        await using Upstream up = await Upstream.StartAsync(200);
        var sink = new RecordingSink();
        var listeners = new LabListenerPorts();
        Result<BarrierProxy> started = await BarrierProxy.StartAsync(UpstreamUri, 0, sink, TimeProvider.System, CancellationToken.None, listeners);
        Assert.True(started.IsSuccess, started.Error);
        BarrierProxy proxy = started.Value;

        Assert.Contains(proxy.BoundPort, listeners.Held);
        await proxy.DisposeAsync();
        Assert.DoesNotContain(proxy.BoundPort, listeners.Held);
    }

    [Fact]
    public async Task Pass_CopiesStatusAndBodyBack()
    {
        await using Upstream up = await Upstream.StartAsync(202);
        var sink = new RecordingSink();
        await using BarrierProxy proxy = await StartProxyAsync(sink);
        Assert.True(proxy.Arm(RunId, [Rule(1, BarrierAction.Pass, null)]).IsSuccess);

        using HttpResponseMessage response = await Send(proxy, 1);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("ok-body", await response.Content.ReadAsStringAsync());
        Assert.Contains(sink.Lines, line => line.Op == "proxy_request_arrived");
        Assert.Contains(sink.Lines, line => line.Op == "proxy_upstream_completed");
    }

    private static async Task<BarrierProxy> StartProxyAsync(RecordingSink sink)
    {
        Result<BarrierProxy> started = await BarrierProxy.StartAsync(UpstreamUri, 0, sink, TimeProvider.System, CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }

    private static BarrierRule Rule(int seq, BarrierAction action, string? barrier) => new(OpText(seq), action, barrier, HoldMs: 0);

    private static string OpText(int seq) => $"{RunId}/{Actor}/{seq.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static Uri ProxyUri(BarrierProxy proxy, string path) =>
        new($"http://127.0.0.1:{proxy.BoundPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}{path}");

    private static HttpClient NewClient() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false });

    private static async Task<HttpResponseMessage> Send(BarrierProxy proxy, int seq) => await SendRawAsync(proxy, OpText(seq));

    private static async Task<HttpResponseMessage> SendRawAsync(BarrierProxy proxy, string? opId)
    {
        using HttpClient client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyUri(proxy, "/probe"));
        if (opId is not null)
        {
            request.Headers.Add("X-Lab-Op-Id", opId);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// The test upstream on the agent-ingress port: counts calls, records the request headers, and answers with a fixed status.
    /// </summary>
    private sealed class Upstream : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly int _status;
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _seen = new();
        private int _hits;

        private Upstream(WebApplication app, int status)
        {
            _app = app;
            _status = status;
        }

        public int Hits => _hits;

        public IEnumerable<IReadOnlyDictionary<string, string>> Seen => _seen;

        public static async Task<Upstream> StartAsync(int status)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, LabPorts.AgentIngress));
            WebApplication app = builder.Build();
            var upstream = new Upstream(app, status);
            app.Map("/{**path}", upstream.HandleAsync);
            await app.StartAsync();
            return upstream;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext ctx)
        {
            Interlocked.Increment(ref _hits);
            _seen.Enqueue(ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase));
            ctx.Response.StatusCode = _status;
            ctx.Response.Headers["X-Upstream-Probe"] = "yes";
            await ctx.Response.WriteAsync("ok-body");
        }
    }

    /// <summary>
    /// An observation sink that keeps every appended line in memory.
    /// </summary>
    private sealed class RecordingSink : ILabObservationSink
    {
        public ConcurrentQueue<LabObservation> Lines { get; } = new();

        public Result BeginRun(string runId, string runFolder) => Result.Success();

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => Task.FromResult(Result<long>.Success(0));

        public OpId NextOp(string runId, string actor) => new(runId, actor, 1);

        public Result<long> Append(LabObservation draft)
        {
            Lines.Enqueue(draft);
            return Result<long>.Success(draft.Seq);
        }

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => [];
    }
}
