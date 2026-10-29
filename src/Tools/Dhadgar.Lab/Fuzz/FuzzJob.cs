using System.Globalization;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Fuzz;

/// <summary>
/// Sends one fuzz case. The real implementation is <see cref="LabFuzzTransport"/> over <see cref="LabHttp"/>; tests supply a fake.
/// </summary>
public interface IFuzzTransport
{
    /// <summary>Sends the case to the target. A refused request (before any send) is a failure result.</summary>
    Task<Result<HttpProbeResult>> SendAsync(FuzzTarget target, FuzzCase fuzzCase, CancellationToken ct);
}

/// <summary>The <see cref="IFuzzTransport"/> over <see cref="LabHttp.SendRawAsync"/>: every request goes through the lab probe (R-HOST).</summary>
public sealed class LabFuzzTransport : IFuzzTransport
{
    private readonly LabHttp _http;

    /// <summary>Creates the transport over the lab probe client.</summary>
    public LabFuzzTransport(LabHttp http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public Task<Result<HttpProbeResult>> SendAsync(FuzzTarget target, FuzzCase fuzzCase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fuzzCase);
        return _http.SendRawAsync(target.Via, target.Method, target.PathTemplate, fuzzCase.Body, fuzzCase.ContentType, ct);
    }
}

/// <summary>
/// The <c>fuzz</c> job (L28 file 4): runs cases <c>[firstIndex, firstIndex + count)</c> of one target. Every case waits on the
/// shared <see cref="GatewayPacer"/> (D-ML88). A 429 waits for <c>Retry-After</c> and is sent once more; a second 429 is throttled
/// and is not a failure. Each failing case becomes a row <c>fuzz/&lt;target&gt;/&lt;n&gt;</c> with its signatures, and replays alone
/// from the seed and index. The payload holds no request or response body (R-LOG).
/// </summary>
public sealed class FuzzJob
{
    /// <summary>The job kind.</summary>
    public const string Kind = "fuzz";

    /// <summary>The default case count.</summary>
    public const int DefaultCases = 200;

    /// <summary>The largest case count in one run.</summary>
    public const int MaxCases = 5000;

    /// <summary>The <c>Retry-After</c> wait when the header is missing or unreadable.</summary>
    public const int DefaultRetryAfterSeconds = 5;

    /// <summary>The longest wait honoured from <c>Retry-After</c>.</summary>
    public const int MaxRetryAfterSeconds = 60;

    private readonly IFuzzTransport _transport;
    private readonly GatewayPacer _pacer;
    private readonly TimeProvider _time;
    private readonly KnownIssues _known;
    private readonly IReadOnlyList<string> _forbiddenMarkers;

    /// <summary>
    /// Creates the job. <paramref name="known"/> is the shipped known-issue list (or <see cref="KnownIssues.Empty"/>);
    /// <paramref name="forbiddenMarkers"/> are secret values that must not appear in any response.
    /// </summary>
    public FuzzJob(IFuzzTransport transport, GatewayPacer pacer, TimeProvider time, KnownIssues known, IReadOnlyList<string> forbiddenMarkers)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _known = known ?? throw new ArgumentNullException(nameof(known));
        _forbiddenMarkers = forbiddenMarkers ?? throw new ArgumentNullException(nameof(forbiddenMarkers));
    }

    /// <summary>
    /// Runs the cases. Overall is <c>pass</c> with no failing row, else <c>fail</c>. A cancelled run or a refused request
    /// returns a failure result.
    /// </summary>
    public async Task<Result<JobOutcome>> RunAsync(ulong seed, FuzzTarget target, int firstIndex, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (firstIndex < 0 || count < 1 || count > MaxCases)
        {
            return Result<JobOutcome>.Failure("[Lab.InvalidArgument] cases must be 1 to 5000 and the index must not be negative");
        }

        var failures = new JsonArray();
        int sent = 0;
        int throttled = 0;
        int known = 0;

        for (int index = firstIndex; index < firstIndex + count; index++)
        {
            if (ct.IsCancellationRequested)
            {
                return Result<JobOutcome>.Failure("[Lab.Cancelled] the fuzz run was cancelled");
            }

            FuzzCase fuzzCase = Mutators.Build(target, seed, index);
            Result<FuzzVerdict> judged = await JudgeAsync(target, fuzzCase, ct).ConfigureAwait(false);
            if (judged.IsFailure)
            {
                return Result<JobOutcome>.Failure(judged.Error);
            }

            FuzzVerdict verdict = judged.Value!;
            sent++;
            switch (verdict.Kind)
            {
                case FuzzVerdictKind.Throttled:
                    throttled++;
                    break;
                case FuzzVerdictKind.Known:
                    known++;
                    break;
                case FuzzVerdictKind.Failure:
                    failures.Add(RowOf(target, fuzzCase, seed, verdict));
                    break;
            }
        }

        var payload = new JsonObject
        {
            ["target"] = target.Name,
            ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
            ["first_index"] = firstIndex,
            ["cases"] = count,
            ["sent"] = sent,
            ["throttled"] = throttled,
            ["known"] = known,
            ["failures"] = failures,
        };
        return Result<JobOutcome>.Success(new JobOutcome(failures.Count == 0 ? "pass" : "fail", payload));
    }

    /// <summary>Paces, sends, honours one <c>Retry-After</c> on a 429, and judges the observed response.</summary>
    private async Task<Result<FuzzVerdict>> JudgeAsync(FuzzTarget target, FuzzCase fuzzCase, CancellationToken ct)
    {
        Result paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
        if (paced.IsFailure)
        {
            return Result<FuzzVerdict>.Failure(paced.Error);
        }

        Result<HttpProbeResult> sent = await _transport.SendAsync(target, fuzzCase, ct).ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return Result<FuzzVerdict>.Failure(sent.Error);
        }

        HttpProbeResult probe = sent.Value!;
        if (probe.Status == 429)
        {
            try
            {
                await Task.Delay(RetryAfter(probe), _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Result<FuzzVerdict>.Failure("[Lab.Cancelled] the fuzz run was cancelled while waiting for Retry-After");
            }

            paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
            if (paced.IsFailure)
            {
                return Result<FuzzVerdict>.Failure(paced.Error);
            }

            sent = await _transport.SendAsync(target, fuzzCase, ct).ConfigureAwait(false);
            if (sent.IsFailure)
            {
                return Result<FuzzVerdict>.Failure(sent.Error);
            }

            probe = sent.Value!;
        }

        string? contentType = probe.Headers.TryGetValue("Content-Type", out string? value) ? value : null;
        string body = probe.Body is { } element ? element.GetRawText() : string.Empty;
        var observation = new FuzzObservation(probe.Status, contentType, body, probe.DurationMs, probe.Error);
        return Result<FuzzVerdict>.Success(FuzzOracle.Judge(target, fuzzCase, observation, _known, _forbiddenMarkers));
    }

    /// <summary>The <c>Retry-After</c> seconds, clamped to 1 to 60; the default when it is missing or not a number.</summary>
    internal static TimeSpan RetryAfter(HttpProbeResult probe)
    {
        if (!probe.Headers.TryGetValue("Retry-After", out string? raw)
            || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            return TimeSpan.FromSeconds(DefaultRetryAfterSeconds);
        }

        return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, MaxRetryAfterSeconds));
    }

    /// <summary>One failing row: its id, the mutation, the status, the reasons and the signature texts. No body.</summary>
    private static JsonObject RowOf(FuzzTarget target, FuzzCase fuzzCase, ulong seed, FuzzVerdict verdict)
    {
        var reasons = new JsonArray();
        foreach (string reason in verdict.Reasons)
        {
            reasons.Add(reason);
        }

        var signatures = new JsonArray();
        foreach (FailureSignature signature in verdict.Signatures)
        {
            signatures.Add(signature.Text);
        }

        return new JsonObject
        {
            ["id"] = FuzzOracle.RowId(target, fuzzCase),
            ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
            ["index"] = fuzzCase.Index,
            ["mutation"] = fuzzCase.Mutation,
            ["reasons"] = reasons,
            ["signatures"] = signatures,
        };
    }
}
