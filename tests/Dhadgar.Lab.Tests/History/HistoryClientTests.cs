using System.Net;
using System.Text;
using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Lab;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The history client (C19) against a fake barrier handler and an in-memory sink: the body is the shared request, the token
/// never reaches an observation line, and the retry keys follow the scenario.
/// </summary>
public sealed class HistoryClientTests : IDisposable
{
    private const string TokenValue = "tok-live-QWERTY-0123456789";
    private const string RunId = "9f2c4a1e-7b00-4c11-8d22-000000000042";
    private const string NodeId = "6f1c2a2e-0000-0000-0000-000000000001";

    private readonly SecretVault _vault = new();
    private readonly Redactor _redactor;
    private readonly FakeSink _sink = new();
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;
    private readonly Uri _endpoint = new("http://127.0.0.1:" + LabPorts.Barrier.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/api/v1/agents/enroll");
    private readonly string _tokenRef;

    public HistoryClientTests()
    {
        _tokenRef = _vault.Add(SecretKind.EnrollmentToken, TokenValue.AsSpan());
        _redactor = new Redactor(_vault);
        _http = new HttpClient(_handler, disposeHandler: false);
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public async Task Enroll_Body_IsTheSharedEnrollNodeRequest()
    {
        using HistoryClient client = NewClient();

        Result<HistoryClientOutcome> outcome = await client.RunAsync(HistoryRetry.None, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        EnrollNodeRequest body = Assert.Single(_handler.Bodies.Select(Deserialize));
        Assert.Equal(TokenValue, body.Token);
        Assert.Equal("linux", body.Platform);
        Assert.Equal("lab-9f2c4a-c1-1", body.Hardware.Hostname);
        Assert.False(string.IsNullOrEmpty(body.PublicKey));
    }

    [Fact]
    public async Task Enroll_Token_NeverAppearsInAnyObservationLine()
    {
        using HistoryClient client = NewClient();

        await client.RunAsync(HistoryRetry.SameKey, CancellationToken.None);

        Assert.NotEmpty(_sink.Lines);
        foreach (LabObservation line in _sink.Lines)
        {
            string args = line.Args.ValueKind == JsonValueKind.Undefined ? string.Empty : line.Args.GetRawText();
            string result = line.Result.ValueKind == JsonValueKind.Undefined ? string.Empty : line.Result.GetRawText();
            Assert.DoesNotContain(TokenValue, args + result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Invoke_Args_CarryHostnameAndKeyLabel_NeverTheKey()
    {
        using HistoryClient client = NewClient();

        await client.RunAsync(HistoryRetry.None, CancellationToken.None);

        LabObservation invoke = Assert.Single(_sink.Lines, l => l.Kind == ObservationKind.Invoke);
        Assert.Equal("lab-9f2c4a-c1-1", invoke.Args.GetProperty("hostname").GetString());
        Assert.Equal("key-a", invoke.Args.GetProperty("key_label").GetString());
        Assert.False(invoke.Args.TryGetProperty("key", out _));
        Assert.False(invoke.Args.TryGetProperty("public_key", out _));
    }

    [Fact]
    public async Task Complete_Created_IsOkWithTheNodeId()
    {
        using HistoryClient client = NewClient();

        await client.RunAsync(HistoryRetry.None, CancellationToken.None);

        LabObservation complete = Assert.Single(_sink.Lines, l => l.Kind == ObservationKind.Complete);
        Assert.Equal(OpOutcome.Ok, complete.Outcome);
        Assert.Equal(201, complete.Result.GetProperty("status").GetInt32());
        Assert.Equal(NodeId, complete.Result.GetProperty("node_id").GetString());
    }

    [Fact]
    public async Task SameKeyRetry_SendsTheSameSpki_CausedByTheFirstOp()
    {
        using HistoryClient client = NewClient();

        Result<HistoryClientOutcome> outcome = await client.RunAsync(HistoryRetry.SameKey, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, _handler.Bodies.Count);
        Assert.Equal(Deserialize(_handler.Bodies[0]).PublicKey, Deserialize(_handler.Bodies[1]).PublicKey);
        Assert.Equal(2, outcome.Value!.OpIds.Count);
        List<LabObservation> invokes = _sink.Lines.Where(l => l.Kind == ObservationKind.Invoke).ToList();
        Assert.Equal(outcome.Value.OpIds[0], invokes[1].CausedBy);
    }

    [Fact]
    public async Task NewKeyRetry_SendsADifferentSpki()
    {
        using HistoryClient client = NewClient();

        Result<HistoryClientOutcome> outcome = await client.RunAsync(HistoryRetry.NewKey, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, _handler.Bodies.Count);
        Assert.NotEqual(Deserialize(_handler.Bodies[0]).PublicKey, Deserialize(_handler.Bodies[1]).PublicKey);
    }

    [Fact]
    public async Task Enroll_UntrustedEndpoint_RefusedBeforeAnyRequest()
    {
        using var client = new HistoryClient(
            RunId, 1, "key-a", "linux", _tokenRef, new Uri("https://example.com/api/v1/agents/enroll"),
            _http, _sink, _vault, _redactor, TimeProvider.System);

        Result<HistoryClientOutcome> outcome = await client.RunAsync(HistoryRetry.None, CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.Contains("[Lab.Untrusted]", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(_handler.Bodies);
        Assert.Empty(_sink.Lines);
    }

    private HistoryClient NewClient() =>
        new(RunId, 1, "key-a", "linux", _tokenRef, _endpoint, _http, _sink, _vault, _redactor, TimeProvider.System);

    private static EnrollNodeRequest Deserialize(string body) =>
        JsonSerializer.Deserialize<EnrollNodeRequest>(body, LabJson.Options)!;

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Bodies.Add(Encoding.UTF8.GetString(bytes));
            var json = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = NodeId });
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FakeSink : ILabObservationSink
    {
        private long _seq;

        public List<LabObservation> Lines { get; } = [];

        public Result BeginRun(string runId, string runFolder) => throw new NotSupportedException();

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => throw new NotSupportedException();

        public OpId NextOp(string runId, string actor) => new(runId, actor, ++_seq);

        public Result<long> Append(LabObservation draft)
        {
            Lines.Add(draft with { Seq = Lines.Count + 1 });
            return Result<long>.Success(Lines.Count);
        }

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => throw new NotSupportedException();
    }
}
