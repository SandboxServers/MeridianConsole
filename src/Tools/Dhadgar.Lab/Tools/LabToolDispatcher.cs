using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// The outcome of one call. When <see cref="IsError"/> is true the payload is the C3 error JSON. The payload is already redacted.
/// </summary>
public sealed record LabToolResult(bool IsError, JsonElement Payload);

/// <summary>
/// The only path that runs a tool, for MCP, the UAT runner and the CLI. Seven steps (C3): resolve the tool, read its gate,
/// check the lease, audit the start, invoke, redact the payload, audit the end. Every refusal is an error payload, never a throw,
/// and every refusal is audited as a <c>lab.call</c> with no arguments.
/// </summary>
public sealed class LabToolDispatcher
{
    /// <summary>
    /// The reserved envelope argument for Leased and OwnLease tools (C3). The dispatcher reads it, and strips it from the
    /// arguments of a Leased tool before the tool binds them. The MCP projection and the UAT runner use the same name.
    /// </summary>
    public const string LeaseIdArgument = "lease_id";
    private const string LeaseHeldCode = "Lab.LeaseHeld";
    private const string LeaseLostCode = "Lab.LeaseLost";
    private const string DefaultErrorCode = "Lab.Unavailable";
    private const string InternalErrorCode = "Lab.Internal";
    private const string CancelledOutcome = "Lab.Cancelled";
    private const string UnknownToolName = "<unknown>";

    private readonly LabToolRegistry _registry;
    private readonly LeaseBook _leases;
    private readonly AuditLog _audit;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;
    private readonly Func<string, LabGate?> _gateFor;

    /// <summary>
    /// Creates the dispatcher that gates tools with <see cref="ToolPolicy"/>.
    /// </summary>
    public LabToolDispatcher(LabToolRegistry registry, LeaseBook leases, AuditLog audit, Redactor redactor, TimeProvider time)
        : this(registry, leases, audit, redactor, time, ToolPolicy.GateFor)
    {
    }

    /// <summary>
    /// Creates the dispatcher with an explicit gate lookup. Tests use it to exercise gates no C4 entry carries.
    /// </summary>
    internal LabToolDispatcher(
        LabToolRegistry registry,
        LeaseBook leases,
        AuditLog audit,
        Redactor redactor,
        TimeProvider time,
        Func<string, LabGate?> gateFor)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gateFor = gateFor ?? throw new ArgumentNullException(nameof(gateFor));
    }

    /// <summary>
    /// Runs one tool call through the gate. Arguments are never written to the audit log.
    /// </summary>
    public async Task<LabToolResult> InvokeAsync(string toolName, JsonElement arguments, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(context);

        // 1: unknown names are refused before any policy lookup. The caller's string is never written to the audit log.
        ILabTool? tool = _registry.Find(toolName);
        if (tool is null)
        {
            return Refuse(context, UnknownToolName, gate: null, "Lab.UnknownTool", "no tool has that name", holder: null);
        }

        // 2: a tool with no gate in the policy table is refused, never run.
        LabGate? classified = _gateFor(toolName);
        if (classified is not { } gate)
        {
            return Refuse(context, toolName, gate: null, "Lab.Unclassified", "the tool has no gate in the policy table", holder: null);
        }

        // 3: the machine gate is reserved; no tool may carry it (D-ML43).
        if (gate == LabGate.Machine)
        {
            return Refuse(context, toolName, gate, "Lab.MachineReserved", "no tool may use the machine gate", holder: null);
        }

        // A Leased tool always needs a lease_id. An OwnLease tool is checked when the caller passes one (N1); it acquires
        // its own lease otherwise. A lease_id that is present but not a non-empty string is refused, never ignored.
        bool needsLease = gate == LabGate.Leased || (gate == LabGate.OwnLease && HasLeaseIdField(arguments));

        // The watch subscribes before Touch, so a lease that ends at any point after Touch cancels the call (N2).
        using LeaseWatch? watch = needsLease ? new LeaseWatch(_leases, ct) : null;
        string? leaseOwner = null;
        string? leaseId = null;
        if (needsLease)
        {
            if (!TryReadLeaseId(arguments, out string candidate))
            {
                return Refuse(context, toolName, gate, "Lab.LeaseRequired", "lease_id is required for this tool", holder: null);
            }

            Result<LeaseInfo> touched = _leases.Touch(candidate);
            if (touched.IsFailure)
            {
                ParseError(touched.Error, out string code, out string message);
                return Refuse(context, toolName, gate, code, message, code == LeaseHeldCode ? _leases.Current : null);
            }

            leaseOwner = touched.Value.Owner;
            leaseId = candidate;
            watch?.Hold(touched.Value);
        }

        // Leased tools take lease_id as an envelope field (C3): it is consumed here and never reaches the tool's binder.
        JsonElement toolArguments = gate == LabGate.Leased ? WithoutLeaseId(arguments) : arguments;

        // 4: the audit start carries no arguments. A start that cannot be written refuses the call. The decision reads this
        // write's own result, not the shared LastFailure (N5).
        DateTimeOffset started = _time.GetUtcNow();
        long startTicks = _time.GetTimestamp();
        Result begin = _audit.TryEmit(new LabEvent("lab.call.begin", started, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["call_id"] = context.CallId,
            ["tool"] = toolName,
            ["gate"] = gate.ToString(),
            ["caller"] = context.Caller,
            ["lease_owner"] = leaseOwner,
        }));

        if (begin.IsFailure)
        {
            return Refusal(DefaultErrorCode, "the audit log is unavailable", holder: null);
        }

        // 5 to 6: invoke, then redact. 7: the audit end runs on every path that began, including a throw.
        string outcomeCode = InternalErrorCode;
        try
        {
            LabToolResult outcome = await RunToolAsync(tool, toolArguments, context, leaseId, watch, ct).ConfigureAwait(false);
            outcomeCode = outcome.IsError ? ErrorCodeOf(outcome.Payload) : "ok";
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcomeCode = CancelledOutcome;
            throw;
        }
        finally
        {
            long durationMs = (long)_time.GetElapsedTime(startTicks).TotalMilliseconds;
            _ = _audit.RecordCall(context.CallId, toolName, gate, context.Caller, leaseOwner, outcomeCode, durationMs);
        }
    }

    /// <summary>
    /// Invokes the tool on the call's token, linked to the lease watch, and maps its outcome. A lost lease becomes
    /// <c>Lab.LeaseLost</c>. Any other exception becomes <c>Lab.Internal</c> with no text: exception messages can carry URLs,
    /// connection strings or tokens. Cancellation by the caller propagates.
    /// </summary>
    private async Task<LabToolResult> RunToolAsync(
        ILabTool tool,
        JsonElement arguments,
        LabCallContext context,
        string? leaseId,
        LeaseWatch? watch,
        CancellationToken ct)
    {
        CancellationToken toolCt = watch?.Token ?? ct;
        LabCallContext toolContext = leaseId is null ? context : context with { LeaseId = leaseId };

        try
        {
            Result<JsonElement> invoked = await tool.InvokeAsync(arguments, toolContext, toolCt).ConfigureAwait(false);
            if (invoked.IsSuccess)
            {
                return new LabToolResult(false, _redactor.Redact(invoked.Value));
            }

            ParseError(invoked.Error, out string code, out string message);
            return Refusal(code, message, code == LeaseHeldCode ? _leases.Current : null);
        }
        catch (OperationCanceledException) when (watch is { IsLost: true } && !ct.IsCancellationRequested)
        {
            return Refusal(LeaseLostCode, "the lease ended while the call was running", holder: null);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Refusal(InternalErrorCode, "the tool failed", holder: null);
        }
    }

    /// <summary>
    /// Records one refused attempt as a <c>lab.call</c> with no arguments, then returns the error payload. The lease owner is
    /// never set for a refusal.
    /// </summary>
    private LabToolResult Refuse(LabCallContext context, string tool, LabGate? gate, string code, string message, LeaseInfo? holder)
    {
        _ = _audit.RecordCall(context.CallId, tool, gate, context.Caller, leaseOwner: null, code, durationMs: 0);
        return Refusal(code, message, holder);
    }

    /// <summary>
    /// Writes the object again without every top-level <c>lease_id</c> property. Duplicate keys are all dropped, and nothing
    /// throws on them, unlike <c>JsonNode.Parse</c>. A non-object is returned unchanged.
    /// </summary>
    private static JsonElement WithoutLeaseId(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return arguments;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in arguments.EnumerateObject())
            {
                if (!string.Equals(property.Name, LeaseIdArgument, StringComparison.Ordinal))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool HasLeaseIdField(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(LeaseIdArgument, out _);

    private static bool TryReadLeaseId(JsonElement arguments, out string leaseId)
    {
        leaseId = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(LeaseIdArgument, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        leaseId = text;
        return true;
    }

    /// <summary>
    /// Splits <c>[Lab.Code] message</c>. A message without a code becomes <c>Lab.Unavailable</c>.
    /// </summary>
    private static void ParseError(string error, out string code, out string message)
    {
        int close = error.StartsWith('[') ? error.IndexOf(']', StringComparison.Ordinal) : -1;
        if (close > 1)
        {
            code = error[1..close];
            message = error[(close + 1)..].Trim();
            return;
        }

        code = DefaultErrorCode;
        message = error;
    }

    private static string ErrorCodeOf(JsonElement payload) =>
        payload.TryGetProperty("error", out JsonElement error)
        && error.TryGetProperty("code", out JsonElement code)
        && code.GetString() is { } text
            ? text
            : DefaultErrorCode;

    private LabToolResult Refusal(string code, string message, LeaseInfo? holder)
    {
        // holder appears only for Lab.LeaseHeld (C3). It carries no lease id.
        LeaseHolder? body = holder is null || code != LeaseHeldCode
            ? null
            : new LeaseHolder(holder.Owner, holder.Purpose, holder.Since, holder.ExpiresAt);

        JsonElement payload = JsonSerializer.SerializeToElement(new ErrorEnvelope(new ErrorBody(code, message, body)), LabJson.Options);
        return new LabToolResult(true, _redactor.Redact(payload));
    }

    private sealed record ErrorEnvelope(ErrorBody Error);

    private sealed record ErrorBody(
        string Code,
        string Message,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LeaseHolder? Holder);

    private sealed record LeaseHolder(string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Cancels one call's token when the lease the call holds ends by expiry, release or takeover (security N2). The watch
    /// is created per call and disposed when the call returns, so it never outlives the call.
    /// </summary>
    private sealed class LeaseWatch : IDisposable
    {
        private readonly LeaseBook _leases;
        private readonly CancellationTokenSource _source;
        private volatile LeaseInfo? _held;
        private volatile bool _lost;

        public LeaseWatch(LeaseBook leases, CancellationToken ct)
        {
            _leases = leases;
            _source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _leases.LeaseEnded += OnLeaseEnded;
        }

        /// <summary>
        /// The token the tool runs on. It is cancelled when the held lease ends.
        /// </summary>
        public CancellationToken Token => _source.Token;

        /// <summary>
        /// True when the held lease ended while the call ran.
        /// </summary>
        public bool IsLost => _lost;

        /// <summary>
        /// Records the lease that Touch just confirmed. A lease that ended between Touch and this line raised its event
        /// before the lease was recorded, so it is checked here.
        /// </summary>
        public void Hold(LeaseInfo info)
        {
            _held = info;
            LeaseInfo? current = _leases.Current;
            if (current is null || !SameLease(current, info))
            {
                MarkLost();
            }
        }

        public void Dispose()
        {
            _leases.LeaseEnded -= OnLeaseEnded;
            _source.Dispose();
        }

        private void OnLeaseEnded(object? sender, LeaseHistoryEntry entry)
        {
            LeaseInfo? held = _held;
            if (held is not null && entry.Owner == held.Owner && entry.Since == held.Since)
            {
                MarkLost();
            }
        }

        private void MarkLost()
        {
            _lost = true;
            _source.Cancel();
        }

        private static bool SameLease(LeaseInfo current, LeaseInfo held) =>
            current.Owner == held.Owner && current.Since == held.Since;
    }
}
