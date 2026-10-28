using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.History;

/// <summary>
/// The op ids of one client's enroll attempts, first attempt first.
/// </summary>
/// <param name="OpIds">The op ids in attempt order; one entry without a retry, two with one.</param>
public sealed record HistoryClientOutcome(IReadOnlyList<string> OpIds);

/// <summary>
/// One history client (C19): an actor <c>client:&lt;n&gt;</c> that enrolls through the barrier proxy. Each attempt writes an
/// <c>Invoke</c> line before the request and a <c>Complete</c> line after it. The request body is the shared
/// <see cref="EnrollNodeRequest"/>; the token is read from the vault into a <c>char[]</c> that is cleared after the body is
/// serialised, and the key material never leaves as anything but the SPKI. The token never appears in observation lines.
/// </summary>
/// <remarks>
/// The caller passes an <see cref="HttpClient"/> over <see cref="LabHttp.CreateHandler"/> (no redirects, no proxy). The
/// endpoint is checked with <see cref="LabHosts.IsLabEndpoint"/> before any request.
/// </remarks>
public sealed class HistoryClient : IDisposable
{
    /// <summary>The operation name on the observation lines.</summary>
    public const string Operation = "enroll";

    /// <summary>The status a successful enroll returns.</summary>
    public const int SuccessStatus = 201;

    private const int RunPrefixLength = 6;
    private const int LabCpuCores = 2;
    private const long LabMemoryBytes = 4L * 1024 * 1024 * 1024;
    private const long LabDiskBytes = 40L * 1024 * 1024 * 1024;

    private static readonly HashSet<int> SuccessStatuses = [SuccessStatus];

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly ILabObservationSink _sink;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;
    private readonly SecretVault _vault;
    private readonly string _tokenRef;
    private readonly string _runId;
    private readonly string _run6;
    private readonly string _actor;
    private readonly int _index;
    private readonly string _keyLabel;
    private readonly string _platform;
    private readonly List<ECDsa> _keys = [];
    private string? _reservedFirstOp;
    private string? _reservedRetryOp;

    /// <summary>
    /// Creates the client for client number <paramref name="index"/> of a run.
    /// </summary>
    /// <param name="runId">The run id; its first six hex characters form the hostname prefix.</param>
    /// <param name="index">The client number <c>n</c> (actor <c>client:n</c>, hostname <c>lab-&lt;run6&gt;-c&lt;n&gt;-&lt;seq&gt;</c>).</param>
    /// <param name="keyLabel">The label of the first key, from the seeded stream; never the key itself.</param>
    /// <param name="platform">The platform sent in the body (<c>windows</c> or <c>linux</c>).</param>
    /// <param name="tokenRef">The vault reference of the enrollment token.</param>
    /// <param name="endpoint">The enroll URI on the barrier proxy.</param>
    /// <param name="http">The HTTP client over <see cref="LabHttp.CreateHandler"/>.</param>
    /// <param name="sink">The observation sink.</param>
    /// <param name="vault">The secret vault that holds the token.</param>
    /// <param name="redactor">The redactor for every argument and result.</param>
    /// <param name="time">The clock for the observation lines.</param>
    public HistoryClient(
        string runId,
        int index,
        string keyLabel,
        string platform,
        string tokenRef,
        Uri endpoint,
        HttpClient http,
        ILabObservationSink sink,
        SecretVault vault,
        Redactor redactor,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(keyLabel);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(tokenRef);
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _runId = runId;
        _run6 = new string(runId.Where(char.IsAsciiHexDigitLower).Take(RunPrefixLength).ToArray());
        _index = index;
        _actor = "client:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _keyLabel = keyLabel;
        _platform = platform;
        _tokenRef = tokenRef;
    }

    /// <summary>
    /// Runs the first enroll and, when <paramref name="retry"/> is not <see cref="HistoryRetry.None"/>, one retry caused by it.
    /// The retry uses the first key (<see cref="HistoryRetry.SameKey"/>) or a new key (<see cref="HistoryRetry.NewKey"/>).
    /// </summary>
    public async Task<Result<HistoryClientOutcome>> RunAsync(HistoryRetry retry, CancellationToken ct)
    {
        // The barrier proxy (25080) is an infra port; the Nodes and gateway routes are service ports.
        if (!LabHosts.IsLabEndpoint(_endpoint) && !LabHosts.IsLabInfraEndpoint(_endpoint))
        {
            return Result<HistoryClientOutcome>.Failure("[Lab.Untrusted] the enroll URI is not a lab endpoint");
        }

        var opIds = new List<string>();
        ECDsa firstKey = NewKey();

        string? reserved = _reservedFirstOp;
        _reservedFirstOp = null;
        string? reservedRetry = _reservedRetryOp;
        _reservedRetryOp = null;
        Result<string> first = await EnrollOnceAsync(1, firstKey, _keyLabel, causedBy: null, ct, reserved).ConfigureAwait(false);
        if (first.IsFailure)
        {
            return Result<HistoryClientOutcome>.Failure(first.Error);
        }

        opIds.Add(first.Value!);
        if (retry == HistoryRetry.None)
        {
            return Result<HistoryClientOutcome>.Success(new HistoryClientOutcome(opIds));
        }

        ECDsa retryKey = retry == HistoryRetry.SameKey ? firstKey : NewKey();
        string retryLabel = retry == HistoryRetry.SameKey ? _keyLabel : _keyLabel + "-retry";
        Result<string> second = await EnrollOnceAsync(2, retryKey, retryLabel, first.Value, ct, reservedOpId: reservedRetry).ConfigureAwait(false);
        if (second.IsFailure)
        {
            return Result<HistoryClientOutcome>.Failure(second.Error);
        }

        opIds.Add(second.Value!);
        return Result<HistoryClientOutcome>.Success(new HistoryClientOutcome(opIds));
    }

    /// <summary>
    /// Runs the first enroll with a reserved op id (the job reserves it to arm the barrier rule before the request), then the retry.
    /// </summary>
    public Task<Result<HistoryClientOutcome>> RunAsync(HistoryRetry retry, string firstOpId, CancellationToken ct)
    {
        return RunAsync(retry, firstOpId, null, ct);
    }

    /// <summary>
    /// Runs the first enroll with a reserved op id and the retry with a reserved op id (the job arms both on the barrier proxy
    /// before the requests; an unarmed retry op is answered 400 and never forwarded).
    /// </summary>
    /// <param name="retry">The retry mode.</param>
    /// <param name="firstOpId">The reserved op id of the first enroll.</param>
    /// <param name="retryOpId">The reserved op id of the retry, or null to allocate one.</param>
    /// <param name="ct">The cancellation token.</param>
    public Task<Result<HistoryClientOutcome>> RunAsync(HistoryRetry retry, string firstOpId, string? retryOpId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(firstOpId);
        _reservedFirstOp = firstOpId;
        _reservedRetryOp = retryOpId;
        return RunAsync(retry, ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (ECDsa key in _keys)
        {
            key.Dispose();
        }

        _keys.Clear();
    }

    private ECDsa NewKey()
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _keys.Add(key);
        return key;
    }

    private async Task<Result<string>> EnrollOnceAsync(
        int attempt,
        ECDsa key,
        string keyLabel,
        string? causedBy,
        CancellationToken ct,
        string? reservedOpId = null)
    {
        string hostname = "lab-" + _run6 + "-c" + _index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string opId = reservedOpId ?? _sink.NextOp(_runId, _actor).ToString();

        // The arguments carry the hostname and the key label only; the key and the token never reach the line.
        JsonElement invokeArgs = _redactor.Redact(JsonSerializer.SerializeToElement(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["hostname"] = hostname, ["key_label"] = keyLabel },
            LabJson.Options));
        Result<long> invoke = _sink.Append(new LabObservation(
            0, _runId, _time.GetUtcNow(), _actor, opId, ObservationKind.Invoke, Operation,
            invokeArgs, null, default, causedBy));
        if (invoke.IsFailure)
        {
            return Result<string>.Failure(invoke.Error);
        }

        HttpResponseMessage? response = null;
        Exception? error = null;
        try
        {
            Result<byte[]> body = BuildBody(hostname, key);
            if (body.IsFailure)
            {
                // Nothing was sent, so the op failed before the request: a Fail, not an Unknown.
                return await CompleteAsync(opId, null, null, "pre_send", ct).ConfigureAwait(false);
            }

            try
            {
                using var content = new ByteArrayContent(body.Value!);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
                _ = request.Headers.TryAddWithoutValidation("X-Lab-Op-Id", opId);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body.Value!);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // The transport message can carry the URL; only the outcome is kept.
            error = ex;
        }

        using (response)
        {
            return await CompleteAsync(opId, response, error, null, ct).ConfigureAwait(false);
        }
    }

    private Result<byte[]> BuildBody(string hostname, ECDsa key)
    {
        Result<char[]> token = _vault.Use(_tokenRef, span => span.ToArray());
        if (token.IsFailure)
        {
            return Result<byte[]>.Failure(token.Error);
        }

        char[] tokenChars = token.Value!;
        try
        {
            string spki = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            var request = new EnrollNodeRequest(
                new string(tokenChars),
                _platform,
                new HardwareInventoryRequest(hostname, null, LabCpuCores, LabMemoryBytes, LabDiskBytes, null),
                spki);
            return Result<byte[]>.Success(JsonSerializer.SerializeToUtf8Bytes(request, LabJson.Options));
        }
        finally
        {
            Array.Clear(tokenChars);
        }
    }

    private async Task<Result<string>> CompleteAsync(
        string opId,
        HttpResponseMessage? response,
        Exception? error,
        string? preSendCode,
        CancellationToken ct)
    {
        OpOutcome outcome = preSendCode is not null
            ? OpOutcome.Fail
            : OutcomeClassifier.Classify(response, error, SuccessStatuses);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = response is null ? 0 : (int)response.StatusCode,
        };

        if (preSendCode is not null)
        {
            result["error_code"] = preSendCode;
        }
        else if (error is not null)
        {
            result["error_code"] = error is OperationCanceledException ? "cancelled" : "transport";
        }
        else if (response is not null && response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? nodeId = ReadString(text, "nodeId");
            if (nodeId is not null)
            {
                result["node_id"] = nodeId;
            }
        }
        else if (response is not null)
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? code = ReadString(text, "code");
            if (code is not null)
            {
                result["error_code"] = code;
            }
        }

        JsonElement resultJson = _redactor.Redact(JsonSerializer.SerializeToElement(result, LabJson.Options));
        Result<long> complete = _sink.Append(new LabObservation(
            0, _runId, _time.GetUtcNow(), _actor, opId, ObservationKind.Complete, Operation,
            default, outcome, resultJson, null));
        return complete.IsFailure
            ? Result<string>.Failure(complete.Error)
            : Result<string>.Success(opId);
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(property, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
