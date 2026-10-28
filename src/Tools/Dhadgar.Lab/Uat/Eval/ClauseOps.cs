using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The state an op produced, with the error text when the state is <see cref="ClauseState.Unverified"/>.
/// </summary>
public sealed record OpOutcome(ClauseState State, string? Error);

/// <summary>
/// The C7 operators. Numbers compare as <see cref="decimal"/>, strings ordinal. A type mismatch is <see cref="ClauseState.NotHeld"/>.
/// A missing pointer is <see cref="ClauseState.Unverified"/> for every op except <c>absent</c>, which holds.
/// </summary>
public static class ClauseOps
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Applies <paramref name="op"/>. <paramref name="target"/> is the pointed value (null when the pointer names nothing),
    /// <paramref name="value"/> is the clause <c>value</c>, and <paramref name="before"/> is the baseline pointed value for <c>delta</c>.
    /// </summary>
    public static OpOutcome Evaluate(string op, JsonElement? target, JsonElement? value, JsonElement? before)
    {
        ArgumentNullException.ThrowIfNull(op);

        if (op == "absent")
        {
            return new OpOutcome(target is null ? ClauseState.Held : ClauseState.NotHeld, null);
        }

        if (target is null)
        {
            return Unverified("pointer not found");
        }

        JsonElement t = target.Value;

        switch (op)
        {
            case "exists":
                return Bool(true);
            case "truthy":
                return Bool(IsTruthy(t));
            case "falsy":
                return Bool(!IsTruthy(t));
            case "eq":
                return value is null ? Unverified("value required") : Bool(JsonEquals(t, value.Value));
            case "ne":
                return value is null ? Unverified("value required") : Bool(!JsonEquals(t, value.Value));
            case "contains":
            case "not_contains":
                return ContainsOp(op == "contains", t, value);
            case "matches":
                return Matches(t, value);
            case "gt":
            case "gte":
            case "lt":
            case "lte":
            case "min_rows":
            case "max_rows":
                return Compare(op, t, value);
            case "len_gte":
                return LengthAtLeast(t, value);
            case "approx":
                return Approx(t, value);
            case "delta":
                return Delta(t, value, before);
            case "any":
                return Any(t, value);
            default:
                return Unverified("unknown op " + op);
        }
    }

    /// <summary>
    /// Structural JSON equality: numbers by value (decimal when they fit), strings ordinal, arrays and objects recursively.
    /// Different kinds are never equal.
    /// </summary>
    public static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
        {
            if (a.TryGetDecimal(out decimal x) && b.TryGetDecimal(out decimal y))
            {
                return x == y;
            }

            return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
        }

        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                {
                    return false;
                }

                for (int i = 0; i < a.GetArrayLength(); i++)
                {
                    if (!JsonEquals(a[i], b[i]))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Object:
                if (a.EnumerateObject().Count() != b.EnumerateObject().Count())
                {
                    return false;
                }

                foreach (JsonProperty property in a.EnumerateObject())
                {
                    if (!b.TryGetProperty(property.Name, out JsonElement other) || !JsonEquals(property.Value, other))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
        }
    }

    private static OpOutcome ContainsOp(bool wantContained, JsonElement target, JsonElement? value)
    {
        if (value is null)
        {
            return Unverified("value required");
        }

        bool? contained = null;
        if (target.ValueKind == JsonValueKind.String && value.Value.ValueKind == JsonValueKind.String)
        {
            contained = target.GetString()!.Contains(value.Value.GetString()!, StringComparison.Ordinal);
        }
        else if (target.ValueKind == JsonValueKind.Array)
        {
            contained = false;
            foreach (JsonElement item in target.EnumerateArray())
            {
                if (JsonEquals(item, value.Value))
                {
                    contained = true;
                    break;
                }
            }
        }

        if (contained is null)
        {
            return Bool(false);
        }

        return Bool(wantContained ? contained.Value : !contained.Value);
    }

    private static OpOutcome Matches(JsonElement target, JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.String)
        {
            return Unverified("matches needs a string value");
        }

        if (target.ValueKind != JsonValueKind.String)
        {
            return Bool(false);
        }

        try
        {
            var regex = new Regex(value.Value.GetString()!, RegexOptions.CultureInvariant, RegexTimeout);
            return Bool(regex.IsMatch(target.GetString()!));
        }
        catch (ArgumentException ex)
        {
            return Unverified("invalid regex: " + ex.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            return Unverified("regex timed out after 1 s");
        }
    }

    private static OpOutcome Compare(string op, JsonElement target, JsonElement? value)
    {
        if (value is null || !TryNumber(value.Value, out decimal bound))
        {
            return Unverified("value must be a number");
        }

        if (!TryNumber(target, out decimal actual))
        {
            return Bool(false);
        }

        return Bool(op switch
        {
            "gt" => actual > bound,
            "gte" or "min_rows" => actual >= bound,
            "lt" => actual < bound,
            _ => actual <= bound,
        });
    }

    private static OpOutcome LengthAtLeast(JsonElement target, JsonElement? value)
    {
        if (value is null || !TryNumber(value.Value, out decimal bound))
        {
            return Unverified("value must be a number");
        }

        decimal length;
        if (target.ValueKind == JsonValueKind.String)
        {
            length = target.GetString()!.Length;
        }
        else if (target.ValueKind == JsonValueKind.Array)
        {
            length = target.GetArrayLength();
        }
        else
        {
            return Bool(false);
        }

        return Bool(length >= bound);
    }

    private static OpOutcome Approx(JsonElement target, JsonElement? value)
    {
        if (value is null
            || value.Value.ValueKind != JsonValueKind.Array
            || value.Value.GetArrayLength() != 2
            || !TryNumber(value.Value[0], out decimal expected)
            || !TryNumber(value.Value[1], out decimal tolerance))
        {
            return Unverified("approx value must be [target, tolerance]");
        }

        if (!TryNumber(target, out decimal actual))
        {
            return Bool(false);
        }

        return Bool(Math.Abs(actual - expected) <= tolerance);
    }

    private static OpOutcome Delta(JsonElement target, JsonElement? value, JsonElement? before)
    {
        if (value is null || !TryNumber(value.Value, out decimal expectedChange))
        {
            return Unverified("value must be a number");
        }

        if (before is null)
        {
            return Unverified("baseline pointer not found");
        }

        if (!TryNumber(target, out decimal after) || !TryNumber(before.Value, out decimal start))
        {
            return Bool(false);
        }

        return Bool(after - start == expectedChange);
    }

    private static OpOutcome Any(JsonElement target, JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array)
        {
            return Unverified("any needs a list value");
        }

        foreach (JsonElement candidate in value.Value.EnumerateArray())
        {
            if (JsonEquals(target, candidate))
            {
                return Bool(true);
            }
        }

        return Bool(false);
    }

    private static bool TryNumber(JsonElement element, out decimal number)
    {
        number = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out number);
    }

    private static bool IsTruthy(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => element.TryGetDecimal(out decimal n) ? n != 0 : true,
            JsonValueKind.String => element.GetString()!.Length > 0,
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            _ => false,
        };
    }

    private static OpOutcome Bool(bool held) =>
        new(held ? ClauseState.Held : ClauseState.NotHeld, null);

    private static OpOutcome Unverified(string error) =>
        new(ClauseState.Unverified, error);
}
