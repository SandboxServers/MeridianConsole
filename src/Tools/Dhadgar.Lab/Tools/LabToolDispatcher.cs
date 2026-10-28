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
/// check the lease, audit the start, invoke, redact the payload, audit the end. Every refusal is an error payload, never a throw.
/// </summary>
public sealed class LabToolDispatcher
{
    private const string LeaseIdField = "lease_id";
    private const string LeaseHeldCode = "Lab.LeaseHeld";
    private const string DefaultErrorCode = "Lab.Unavailable";

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

        // 1: unknown names are refused before any policy lookup.
        ILabTool? tool = _registry.Find(toolName);
        if (tool is null)
        {
            return Refusal("Lab.UnknownTool", "no tool has that name", holder: null);
        }

        // 2: a tool with no gate in the policy table is refused, never run.
        LabGate? gate = _gateFor(toolName);
        if (gate is null)
        {
            return Refusal("Lab.Unclassified", "the tool has no gate in the policy table", holder: null);
        }

        // 3: the machine gate is reserved; no tool may carry it (D-ML43).
        if (gate == LabGate.Machine)
        {
            return Refusal("Lab.MachineReserved", "no tool may use the machine gate", holder: null);
        }

        string? leaseOwner = null;
        if (gate == LabGate.Leased)
        {
            if (!TryReadLeaseId(arguments, out string? leaseId))
            {
                return Refusal("Lab.LeaseRequired", "lease_id is required for this tool", holder: null);
            }

            Result<LeaseInfo> touched = _leases.Touch(leaseId);
            if (touched.IsFailure)
            {
                ParseError(touched.Error, out string code, out string message);
                return Refusal(code, message, code == LeaseHeldCode ? _leases.Current : null);
            }

            leaseOwner = touched.Value.Owner;
        }

        // 4: the audit start carries no arguments. A start that cannot be written refuses the call.
        DateTimeOffset started = _time.GetUtcNow();
        long startTicks = _time.GetTimestamp();
        _audit.Emit(new LabEvent("lab.call.begin", started, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["call_id"] = context.CallId,
            ["tool"] = toolName,
            ["gate"] = gate.ToString(),
            ["caller"] = context.Caller,
            ["lease_owner"] = leaseOwner,
        }));

        if (_audit.LastFailure is not null)
        {
            return Refusal(DefaultErrorCode, "the audit log is unavailable", holder: null);
        }

        // 5: invoke. 6: redact whatever the tool returned.
        Result<JsonElement> invoked = await tool.InvokeAsync(arguments, context, ct).ConfigureAwait(false);
        LabToolResult outcome;
        if (invoked.IsSuccess)
        {
            outcome = new LabToolResult(false, _redactor.Redact(invoked.Value));
        }
        else
        {
            ParseError(invoked.Error, out string code, out string message);
            outcome = Refusal(code, message, code == LeaseHeldCode ? _leases.Current : null);
        }

        // 7: the audit end. The outcome is "ok" or the error code.
        string outcomeCode = outcome.IsError ? ErrorCodeOf(outcome.Payload) : "ok";
        long durationMs = (long)_time.GetElapsedTime(startTicks).TotalMilliseconds;
        _ = _audit.RecordCall(context.CallId, toolName, gate, context.Caller, leaseOwner, outcomeCode, durationMs);

        return outcome;
    }

    private static bool TryReadLeaseId(JsonElement arguments, out string leaseId)
    {
        leaseId = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(LeaseIdField, out JsonElement value)
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
}
