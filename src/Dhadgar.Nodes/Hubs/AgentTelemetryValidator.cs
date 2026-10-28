using Dhadgar.Contracts.Agents;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Range and size checks on <see cref="AgentTelemetry"/> before it is acknowledged. The hub answers
/// <c>AgentHubErrorCodes.InvalidTelemetry</c> when this returns false.
/// </summary>
public static class AgentTelemetryValidator
{
    /// <summary>Returns true when the telemetry is within the limits in <see cref="AgentMessageLimits"/>.</summary>
    public static bool IsValid(AgentTelemetry? telemetry)
    {
        if (telemetry is null || telemetry.Timestamp == default || telemetry.Metrics is null)
        {
            return false;
        }

        if (telemetry.Metrics.Count > AgentMessageLimits.MaxTelemetryMetrics)
        {
            return false;
        }

        foreach (var (name, value) in telemetry.Metrics)
        {
            if (!IsValidName(name) || !double.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidName(string? name)
    {
        if (name is null || name.Length < 1 || name.Length > AgentMessageLimits.MaxMetricNameLength)
        {
            return false;
        }

        return !HasDisallowedCharacter(name);
    }

    /// <summary>
    /// True when the text holds a control, format, line or paragraph separator, or an unpaired surrogate. These
    /// are rejected because they can forge log lines, hide or reorder text in a UI, or break UTF-8 on re-encoding.
    /// A valid surrogate pair (a supplementary character such as an emoji) is allowed.
    /// </summary>
    internal static bool HasDisallowedCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                return true;
            }

            switch (char.GetUnicodeCategory(c))
            {
                case System.Globalization.UnicodeCategory.Control:
                case System.Globalization.UnicodeCategory.Format:
                case System.Globalization.UnicodeCategory.LineSeparator:
                case System.Globalization.UnicodeCategory.ParagraphSeparator:
                    return true;
            }
        }

        return false;
    }
}
