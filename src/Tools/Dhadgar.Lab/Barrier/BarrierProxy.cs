using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Primitives;

namespace Dhadgar.Lab.Barrier;

/// <summary>
/// The lab-only HTTP barrier proxy (L49, C19, D-ML47). An in-daemon Kestrel listener on loopback that forwards plain HTTP to a
/// lab upstream, one operation at a time: each request carries <c>X-Lab-Op-Id</c>, which selects an armed rule. The rule can hold
/// the request until a named barrier is released, hold the response, or drop it after the upstream answered. Bodies are never
/// logged; observations carry the status and the byte counts only. The proxy never follows redirects and never uses a system proxy.
/// </summary>
public sealed class BarrierProxy : IAsyncDisposable
{
    /// <summary>The largest request or response body the proxy carries.</summary>
    public const int MaxBodyBytes = 1 << 20;

    private static readonly TimeSpan DefaultHoldCap = TimeSpan.FromSeconds(30);
    private const string OpHeader = "X-Lab-Op-Id";

    private static readonly HashSet<string> StrippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "TE", "Trailer", "Host", "Content-Length", OpHeader,
    };

    private readonly Uri _upstream;
    private readonly ILabObservationSink _sink;
    private readonly TimeProvider _time;
    private readonly TimeSpan _holdCap;
    private readonly LabListenerPorts? _listeners;
    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Armed> _armed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BarrierGate> _barriers = new(StringComparer.Ordinal);
    private WebApplication? _app;
    private bool _disposed;

    private BarrierProxy(Uri upstream, ILabObservationSink sink, TimeProvider time, TimeSpan holdCap, LabListenerPorts? listeners)
    {
        _upstream = upstream;
        _sink = sink;
        _time = time;
        _holdCap = holdCap;
        _listeners = listeners;
        _handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };
        _client = new HttpClient(_handler);
    }

    /// <summary>
    /// The loopback port the proxy listens on (the bound port, which differs from the requested one when 0 was passed).
    /// </summary>
    public int BoundPort { get; private set; }

    /// <summary>
    /// Binds <c>127.0.0.1:port</c> (only <see cref="LabPorts.Barrier"/>, or 0 in tests) and forwards to <paramref name="upstream"/>,
    /// which must pass <see cref="LabHosts.IsLabEndpoint"/> over plain HTTP. After the bind the port is held in
    /// <paramref name="listeners"/> (D-ML69), and released when the proxy is disposed.
    /// </summary>
    public static Task<Result<BarrierProxy>> StartAsync(
        Uri upstream,
        int port,
        ILabObservationSink sink,
        TimeProvider time,
        CancellationToken ct,
        LabListenerPorts? listeners = null) =>
        StartWithHoldCapAsync(upstream, port, sink, time, DefaultHoldCap, listeners, ct);

    /// <summary>
    /// The start with the request hold cap as a parameter; tests use a short cap to exercise the 504 path.
    /// </summary>
    internal static async Task<Result<BarrierProxy>> StartWithHoldCapAsync(
        Uri upstream,
        int port,
        ILabObservationSink sink,
        TimeProvider time,
        TimeSpan holdCap,
        LabListenerPorts? listeners,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(time);

        if (port != 0 && port != LabPorts.Barrier)
        {
            return Result<BarrierProxy>.Failure("[Lab.BlastRadius] the barrier proxy binds only lab port 25080");
        }

        if (upstream.Scheme != Uri.UriSchemeHttp || !LabHosts.IsLabEndpoint(upstream))
        {
            return Result<BarrierProxy>.Failure("[Lab.BlastRadius] the barrier upstream must be a lab endpoint over plain HTTP");
        }

        if (holdCap <= TimeSpan.Zero)
        {
            return Result<BarrierProxy>.Failure("[Lab.InvalidArgument] the hold cap must be positive");
        }

#pragma warning disable CA2000 // ownership passes to the caller on success; every failure path below disposes it
        var proxy = new BarrierProxy(upstream, sink, time, holdCap, listeners);
#pragma warning restore CA2000
        WebApplication? app = null;
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();

            // S-N5: no configuration source (Kestrel__Endpoints__* and the like) may add a listener; the one explicit loopback
            // listener below is the only endpoint, and BoundPortOf refuses any other address.
            builder.Configuration.Sources.Clear();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, port);
                k.Limits.MaxRequestBodySize = MaxBodyBytes;
            });
            app = builder.Build();
            app.Map("/{**path}", proxy.HandleAsync);
            await app.StartAsync(ct).ConfigureAwait(false);

            Result<int> bound = BoundPortOf(app);
            if (bound.IsFailure)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                await proxy.DisposeAsync().ConfigureAwait(false);
                return Result<BarrierProxy>.Failure(bound.Error);
            }

            proxy._app = app;
            proxy.BoundPort = bound.Value;
            listeners?.Hold(proxy.BoundPort);
            return Result<BarrierProxy>.Success(proxy);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            if (app is not null && proxy._app is null)
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }

            await proxy.DisposeAsync().ConfigureAwait(false);
            return Result<BarrierProxy>.Failure("[Lab.Unavailable] the barrier proxy could not bind its loopback port");
        }
    }

    /// <summary>
    /// Replaces the armed rule set for a run. Each rule's op id must belong to <paramref name="runId"/>, and a hold needs a barrier.
    /// </summary>
    public Result Arm(string runId, IReadOnlyList<BarrierRule> rules)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(rules);

        var armed = new Dictionary<string, Armed>(StringComparer.Ordinal);
        foreach (BarrierRule? rule in rules)
        {
            if (rule is null)
            {
                return Result.Failure("[Lab.InvalidArgument] a barrier rule is null");
            }

            Result<OpId> op = OpId.Parse(rule.OpId);
            if (op.IsFailure)
            {
                return Result.Failure(op.Error);
            }

            if (op.Value.RunId != runId)
            {
                return Result.Failure("[Lab.InvalidArgument] a barrier rule belongs to another run");
            }

            if (rule.Action is BarrierAction.HoldRequest or BarrierAction.HoldResponse && string.IsNullOrEmpty(rule.Barrier))
            {
                return Result.Failure("[Lab.InvalidArgument] a hold needs a barrier name");
            }

            if (rule.HoldMs < 0)
            {
                return Result.Failure("[Lab.InvalidArgument] the hold time cannot be negative");
            }

            if (armed.ContainsKey(rule.OpId))
            {
                return Result.Failure("[Lab.InvalidArgument] an op id is armed twice");
            }

            armed[rule.OpId] = new Armed(op.Value, rule);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return Result.Failure("[Lab.Unavailable] the barrier proxy is stopped");
            }

            _armed.Clear();
            foreach (KeyValuePair<string, Armed> entry in armed)
            {
                _armed[entry.Key] = entry.Value;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Completes when <paramref name="count"/> held requests of <paramref name="barrier"/> have arrived, or fails at the timeout.
    /// </summary>
    public async Task<Result> WaitArrivedAsync(string barrier, int count, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(barrier);
        if (count < 1 || timeout <= TimeSpan.Zero)
        {
            return Result.Failure("[Lab.InvalidArgument] the count and timeout must be positive");
        }

        Task reached;
        lock (_gate)
        {
            reached = GateFor(barrier).WhenArrived(count);
        }

        bool done = await WaitOrTimeoutAsync(reached, timeout, ct).ConfigureAwait(false);
        return done ? Result.Success() : Result.Failure("[Lab.Timeout] the barrier did not reach the count in time");
    }

    /// <summary>
    /// Releases every request held at <paramref name="barrier"/>; later arrivals pass straight through.
    /// </summary>
    public Result Release(string barrier)
    {
        ArgumentException.ThrowIfNullOrEmpty(barrier);
        lock (_gate)
        {
            GateFor(barrier).Open();
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        bool first;
        lock (_gate)
        {
            first = !_disposed;
            _disposed = true;
            foreach (BarrierGate gate in _barriers.Values)
            {
                gate.Open();
            }
        }

        if (!first)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        _client.Dispose();
        _handler.Dispose();
        _listeners?.Release(BoundPort);
        _lifetime.Dispose();
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        CancellationToken ct = ctx.RequestAborted;
        string? opText = ctx.Request.Headers[OpHeader] is { Count: 1 } values ? values[0] : null;

        Armed? armed;
        lock (_gate)
        {
            armed = opText is not null && _armed.TryGetValue(opText, out Armed? found) ? found : null;
        }

        if (armed is null)
        {
            await WriteProblemAsync(ctx, 400, "lab.barrier.no_op", "no armed lab op id on this request").ConfigureAwait(false);
            return;
        }

        Armed op = armed;
        string path = ctx.Request.Path.Value ?? string.Empty;
        string method = ctx.Request.Method;
        BoundedBody inbound = await ReadBoundedAsync(ctx.Request.Body, ct).ConfigureAwait(false);
        if (inbound.TooLarge)
        {
            await WriteProblemAsync(ctx, 413, "lab.barrier.body_too_large", "request body is over 1 MiB").ConfigureAwait(false);
            return;
        }

        if (!Observe(op.Op, "proxy_request_arrived", new JsonObject
        {
            ["method"] = method,
            ["path"] = path,
            ["bytes"] = JsonValue.Create(inbound.Count),
        }))
        {
            await WriteProblemAsync(ctx, 503, "lab.barrier.observe_failed", "the observation sink refused the request").ConfigureAwait(false);
            return;
        }

        if (op.Rule.Action == BarrierAction.HoldRequest)
        {
            bool released = await HoldAtBarrierAsync(op.Rule.Barrier!, ct).ConfigureAwait(false);
            if (!released)
            {
                await WriteProblemAsync(ctx, 504, "lab.barrier.timeout", "the barrier was not released in time").ConfigureAwait(false);
                return;
            }
        }

        HttpResponseMessage response;
        try
        {
            using HttpRequestMessage? outbound = BuildOutbound(ctx, inbound.Bytes);
            if (outbound is null)
            {
                await WriteProblemAsync(ctx, 400, "lab.barrier.bad_target", "the request target does not stay on the lab upstream").ConfigureAwait(false);
                return;
            }

            response = await _client.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            await WriteProblemAsync(ctx, 502, "lab.barrier.upstream_failed", "the upstream did not answer").ConfigureAwait(false);
            return;
        }

        using (response)
        {
            await using Stream upstreamBody = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            BoundedBody reply = await ReadBoundedAsync(upstreamBody, ct).ConfigureAwait(false);
            if (!Observe(op.Op, "proxy_upstream_completed", new JsonObject
            {
                ["status"] = JsonValue.Create((int)response.StatusCode),
                ["bytes"] = JsonValue.Create(reply.Count),
            }))
            {
                await WriteProblemAsync(ctx, 503, "lab.barrier.observe_failed", "the observation sink refused the response").ConfigureAwait(false);
                return;
            }

            if (reply.TooLarge)
            {
                await WriteProblemAsync(ctx, 502, "lab.barrier.too_large", "the upstream response is over 1 MiB").ConfigureAwait(false);
                return;
            }

            switch (op.Rule.Action)
            {
                case BarrierAction.DropResponse:
                    // The upstream answered and the history knows it; the client gets a reset after sending.
                    Observe(op.Op, "proxy_response_dropped", new JsonObject());
                    ctx.Abort();
                    return;
                case BarrierAction.HoldResponse:
                    await WaitOrTimeoutAsync(BarrierSignal(op.Rule.Barrier), TimeSpan.FromMilliseconds(op.Rule.HoldMs), ct).ConfigureAwait(false);
                    break;
            }

            await CopyResponseAsync(ctx, response, reply.Bytes).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the forwarded request, or returns null when its target does not resolve to the upstream's scheme, host and port
    /// (S-N1: a path that begins with two slashes is a network-path reference and would name another host).
    /// </summary>
    private HttpRequestMessage? BuildOutbound(HttpContext ctx, byte[] body)
    {
        string target = ctx.Request.Path.ToUriComponent() + ctx.Request.QueryString.ToUriComponent();
        var destination = new Uri(_upstream, target);
        if (!StaysOnUpstream(destination))
        {
            return null;
        }

        var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), destination);
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
        }

        foreach (KeyValuePair<string, StringValues> header in ctx.Request.Headers)
        {
            if (IsStripped(header.Key))
            {
                continue;
            }

            string[] headerValues = header.Value.Select(value => value ?? string.Empty).ToArray();
            if (IsContentHeader(header.Key))
            {
                request.Content?.Headers.TryAddWithoutValidation(header.Key, headerValues);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(header.Key, headerValues);
            }
        }

        // The upstream is a loopback lab port (StaysOnUpstream). The Gateway's AllowedHosts refuse the IP-literal authority that
        // HttpClient would derive from the destination, so the proxy names the lab probe host, as LabHttp does (R-HOST, F81).
        request.Headers.Host = Dhadgar.Lab.Services.LabServices.ProbeHostHeader;
        return request;
    }

    private static async Task CopyResponseAsync(HttpContext ctx, HttpResponseMessage response, byte[] body)
    {
        ctx.Response.StatusCode = (int)response.StatusCode;
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            if (!IsStripped(header.Key))
            {
                ctx.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
        {
            if (!IsStripped(header.Key))
            {
                ctx.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        ctx.Response.ContentLength = body.Length;
        await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsStripped(string name) =>
        StrippedHeaders.Contains(name) || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentHeader(string name) =>
        name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Expires", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase);

    private bool StaysOnUpstream(Uri destination) =>
        Uri.Compare(destination, _upstream, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0
        && LabHosts.IsLabEndpoint(destination);

    private static Result<int> BoundPortOf(WebApplication app)
    {
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        string[] all = addresses?.Addresses.ToArray() ?? [];
        if (all.Length == 0)
        {
            return Result<int>.Failure("[Lab.Unavailable] the barrier proxy has no bound address");
        }

        // S-N5: every bound address must be loopback, whatever the configuration asked for.
        if (!all.All(IsLoopbackAddress))
        {
            return Result<int>.Failure("[Lab.BlastRadius] the barrier proxy must bind only loopback addresses");
        }

        return Uri.TryCreate(all[0], UriKind.Absolute, out Uri? bound)
            ? Result<int>.Success(bound.Port)
            : Result<int>.Failure("[Lab.Unavailable] the barrier proxy has no bound address");
    }

    private static bool IsLoopbackAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip)
        && IPAddress.IsLoopback(ip);

    private static async Task<BoundedBody> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return new BoundedBody(buffer.Length, buffer.ToArray(), TooLarge: false);
                }

                if (buffer.Length + read > MaxBodyBytes)
                {
                    return new BoundedBody(buffer.Length, [], TooLarge: true);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }
    }

    private static async Task WriteProblemAsync(HttpContext ctx, int status, string type, string title)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        byte[] bytes = Encoding.UTF8.GetBytes(
            $"{{\"type\":\"{type}\",\"title\":\"{title}\",\"status\":{status.ToString(CultureInfo.InvariantCulture)}}}");
        ctx.Response.ContentLength = bytes.Length;
        await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted).ConfigureAwait(false);
    }

    private bool Observe(OpId op, string name, JsonObject args)
    {
        var line = new LabObservation(
            Seq: 0,
            RunId: op.RunId,
            At: _time.GetUtcNow(),
            Actor: op.Actor,
            OpId: null,
            Kind: ObservationKind.Observe,
            Op: name,
            Args: ToElement(args),
            Outcome: null,
            Result: ToElement(new JsonObject()),
            CausedBy: op.ToString());
        return _sink.Append(line).IsSuccess;
    }

    private static JsonElement ToElement(JsonObject node)
    {
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private async Task<bool> HoldAtBarrierAsync(string barrier, CancellationToken ct)
    {
        Task opened;
        lock (_gate)
        {
            BarrierGate gate = GateFor(barrier);
            gate.Arrive();
            opened = gate.Opened;
        }

        return await WaitOrTimeoutAsync(opened, _holdCap, ct).ConfigureAwait(false);
    }

    private Task BarrierSignal(string? barrier)
    {
        if (barrier is null)
        {
            return new TaskCompletionSource().Task;
        }

        lock (_gate)
        {
            return GateFor(barrier).Opened;
        }
    }

    /// <summary>
    /// True when <paramref name="signal"/> completes before the delay on the proxy's clock; false on expiry or cancellation.
    /// </summary>
    private async Task<bool> WaitOrTimeoutAsync(Task signal, TimeSpan delay, CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using ITimer timer = _time.CreateTimer(_ => expired.TrySetResult(), null, delay, Timeout.InfiniteTimeSpan);
        using CancellationTokenRegistration stop = linked.Token.Register(() => expired.TrySetResult());
        Task winner = await Task.WhenAny(signal, expired.Task).ConfigureAwait(false);
        return ReferenceEquals(winner, signal);
    }

    /// <summary>
    /// The gate for a barrier name. Callers hold <c>_gate</c>.
    /// </summary>
    private BarrierGate GateFor(string barrier)
    {
        if (!_barriers.TryGetValue(barrier, out BarrierGate? gate))
        {
            gate = new BarrierGate();
            _barriers[barrier] = gate;
        }

        return gate;
    }

    private sealed record Armed(OpId Op, BarrierRule Rule);

    private sealed record BoundedBody(long Count, byte[] Bytes, bool TooLarge);

    /// <summary>
    /// The arrivals and the release of one barrier. Callers hold the proxy's lock.
    /// </summary>
    private sealed class BarrierGate
    {
        private readonly List<(int Count, TaskCompletionSource Done)> _waiters = [];
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Arrived { get; private set; }

        public Task Opened => _opened.Task;

        public void Arrive()
        {
            Arrived++;
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                if (Arrived >= _waiters[i].Count)
                {
                    _waiters[i].Done.TrySetResult();
                    _waiters.RemoveAt(i);
                }
            }
        }

        public Task WhenArrived(int count)
        {
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Arrived >= count)
            {
                reached.TrySetResult();
            }
            else
            {
                _waiters.Add((count, reached));
            }

            return reached.Task;
        }

        public void Open() => _opened.TrySetResult();
    }
}
