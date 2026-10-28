using System.Text.Json;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.History.Models;

/// <summary>
/// The enrollment state of one token (C19). Keys are token ids.
/// </summary>
public abstract record EnrollState
{
    /// <summary>The token has not been used and is not revoked.</summary>
    public sealed record Unused : EnrollState;

    /// <summary>The token was used by the node with the given (sanitised) name, enrolled with the given public key.</summary>
    /// <param name="NodeName">The sanitised hostname of the node the winning enroll created.</param>
    /// <param name="PublicKey">The public key the winning enroll bound.</param>
    public sealed record Used(string NodeName, string PublicKey) : EnrollState;

    /// <summary>The token is revoked before the run.</summary>
    public sealed record Revoked : EnrollState;
}

/// <summary>
/// The sequential specification of <c>POST /api/v1/agents/enroll</c> per token (C19). Today's product spec: an enroll
/// on an unused token is <c>Ok</c> and takes it; on a used or revoked token it is refused with 401. With
/// <paramref name="idempotentRetry"/> set (D-ML49 "yes") a retry with the same public key on a used token is <c>Ok</c>.
/// Pure: no clock, no I/O.
/// </summary>
public sealed class EnrollmentHistoryModel : IHistoryModel<EnrollState>
{
    /// <summary>The model name used in signatures.</summary>
    public const string ModelName = "enrollment";

    private const int UnauthorizedStatus = 401;

    private readonly bool _idempotentRetry;

    /// <summary>
    /// Creates the model. <paramref name="idempotentRetry"/> <c>false</c> is today's spec; <c>true</c> is D-ML49 "yes".
    /// </summary>
    public EnrollmentHistoryModel(bool idempotentRetry)
    {
        _idempotentRetry = idempotentRetry;
    }

    /// <summary>Whether a retry with the same key after a success is accepted (D-ML49).</summary>
    public bool IdempotentRetry => _idempotentRetry;

    /// <inheritdoc />
    public string Name => ModelName;

    /// <inheritdoc />
    public EnrollState Initial(string key, JsonElement finalStateForKey)
    {
        var token = Prop(finalStateForKey, "token");
        return IsTrue(Prop(token, "revoked")) ? new EnrollState.Revoked() : new EnrollState.Unused();
    }

    /// <inheritdoc />
    public bool TryStep(EnrollState s, HistoryOp op, out EnrollState next)
    {
        next = s;
        switch (op.Outcome)
        {
            case OpOutcome.Ok:
                if (s is EnrollState.Unused)
                {
                    next = new EnrollState.Used(NodeNameOf(Hostname(op)), PublicKeyOf(op));
                    return true;
                }

                return IsSameKeyRetry(s, op);

            case OpOutcome.Fail:
                if (Status(op) != UnauthorizedStatus)
                {
                    // A definite failure other than 401 has no effect on the token.
                    return true;
                }

                // A 401 is only valid once the token is spent (or revoked), and never for a D-ML49 retry.
                return s is not EnrollState.Unused && !IsSameKeyRetry(s, op);

            default:
                // Unknown: the enroll may have taken effect (Ok on an unused token) or not (any other answer, no change).
                if (s is EnrollState.Unused)
                {
                    next = new EnrollState.Used(NodeNameOf(Hostname(op)), PublicKeyOf(op));
                }

                return true;
        }
    }

    /// <inheritdoc />
    public bool FinalMatches(EnrollState s, JsonElement finalStateForKey)
    {
        var token = Prop(finalStateForKey, "token");
        var usedAt = HasValue(Prop(token, "used_at"));
        var names = NodeNames(finalStateForKey);
        return s switch
        {
            EnrollState.Used used => usedAt && names.Count > 0 && names.All(n => n == used.NodeName),
            EnrollState.Unused or EnrollState.Revoked => !usedAt && names.Count == 0,
            _ => false,
        };
    }

    /// <summary>The redacted hostname of an op's input, or an empty string.</summary>
    internal static string Hostname(HistoryOp op) => Text(Prop(op.Input, "hostname")) ?? string.Empty;

    /// <summary>The key identity of an op: the public key when recorded, else the key label.</summary>
    internal static string PublicKeyOf(HistoryOp op) =>
        Text(Prop(op.Input, "public_key")) ?? Text(Prop(op.Input, "key_label")) ?? string.Empty;

    /// <summary>The node id in an op's output, when present.</summary>
    internal static string? NodeIdOf(HistoryOp op) => Text(Prop(op.Output, "node_id"));

    /// <summary>The HTTP status in an op's output, when present.</summary>
    internal static int? Status(HistoryOp op)
    {
        var status = Prop(op.Output, "status");
        return status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var value) ? value : null;
    }

    /// <summary>Sanitises a hostname the way Nodes does: lower case, only <c>[a-z0-9-]</c> kept.</summary>
    internal static string NodeNameOf(string hostname) =>
        new(hostname.ToLowerInvariant().Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-').ToArray());

    /// <summary>The node rows of a final state, in the order given.</summary>
    internal static IReadOnlyList<JsonElement> NodeRows(JsonElement finalState)
    {
        var nodes = Prop(finalState, "nodes");
        return nodes.ValueKind == JsonValueKind.Array ? nodes.EnumerateArray().ToList() : [];
    }

    /// <summary>The names of the node rows of a final state.</summary>
    internal static List<string> NodeNames(JsonElement finalState) =>
        NodeRows(finalState).Select(n => Text(Prop(n, "name")) ?? string.Empty).ToList();

    /// <summary>A property of an object, or an undefined element when the value is not an object or lacks it.</summary>
    internal static JsonElement Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    /// <summary>The text of a string or number element, or <c>null</c>.</summary>
    internal static string? Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _ => null,
    };

    /// <summary>Whether the element is <c>true</c>.</summary>
    internal static bool IsTrue(JsonElement element) => element.ValueKind == JsonValueKind.True;

    /// <summary>Whether the element is present and not <c>null</c>.</summary>
    internal static bool HasValue(JsonElement element) =>
        element.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private bool IsSameKeyRetry(EnrollState s, HistoryOp op) =>
        s is EnrollState.Used used && _idempotentRetry && used.PublicKey == PublicKeyOf(op);
}
