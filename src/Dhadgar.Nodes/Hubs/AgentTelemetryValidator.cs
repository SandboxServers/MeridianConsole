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

        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
