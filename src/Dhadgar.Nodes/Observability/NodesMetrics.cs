using System.Diagnostics.Metrics;

namespace Dhadgar.Nodes.Observability;

/// <summary>
/// Custom metrics for the Nodes service.
/// Provides counters and histograms for monitoring node operations,
/// enrollments, heartbeats, and status transitions.
/// </summary>
public static class NodesMetrics
{
    public const string MeterName = "Dhadgar.Nodes";
    public const string Version = "1.0.0";

    private static readonly Meter Meter = new(MeterName, Version);

    // Enrollment metrics
    public static readonly Counter<long> EnrollmentAttempts = Meter.CreateCounter<long>(
        "nodes.enrollment.attempts",
        unit: "{attempts}",
        description: "Number of node enrollment attempts");

    public static readonly Counter<long> EnrollmentSuccesses = Meter.CreateCounter<long>(
        "nodes.enrollment.successes",
        unit: "{enrollments}",
        description: "Number of successful node enrollments");

    public static readonly Counter<long> EnrollmentFailures = Meter.CreateCounter<long>(
        "nodes.enrollment.failures",
        unit: "{failures}",
        description: "Number of failed node enrollments");

    public static readonly Counter<long> TokensCreated = Meter.CreateCounter<long>(
        "nodes.tokens.created",
        unit: "{tokens}",
        description: "Number of enrollment tokens created");

    public static readonly Counter<long> TokensRevoked = Meter.CreateCounter<long>(
        "nodes.tokens.revoked",
        unit: "{tokens}",
        description: "Number of enrollment tokens revoked");

    // Heartbeat metrics
    public static readonly Counter<long> HeartbeatsReceived = Meter.CreateCounter<long>(
        "nodes.heartbeats.received",
        unit: "{heartbeats}",
        description: "Number of heartbeats received from agents");

    public static readonly Histogram<double> HeartbeatProcessingTime = Meter.CreateHistogram<double>(
        "nodes.heartbeats.processing_time",
        unit: "ms",
        description: "Time taken to process heartbeats");

    // Node status metrics
    public static readonly Counter<long> StatusTransitions = Meter.CreateCounter<long>(
        "nodes.status.transitions",
        unit: "{transitions}",
        description: "Number of node status transitions");

    public static readonly Counter<long> NodesDecommissioned = Meter.CreateCounter<long>(
        "nodes.decommissioned",
        unit: "{nodes}",
        description: "Number of nodes decommissioned");

    public static readonly Counter<long> MaintenanceEntered = Meter.CreateCounter<long>(
        "nodes.maintenance.entered",
        unit: "{events}",
        description: "Number of times nodes entered maintenance mode");

    public static readonly Counter<long> MaintenanceExited = Meter.CreateCounter<long>(
        "nodes.maintenance.exited",
        unit: "{events}",
        description: "Number of times nodes exited maintenance mode");

    // Stale node detection
    public static readonly Counter<long> StaleNodesDetected = Meter.CreateCounter<long>(
        "nodes.stale.detected",
        unit: "{nodes}",
        description: "Number of stale nodes detected by background service");

    // Capacity reservation metrics
    public static readonly Counter<long> ReservationsCreated = Meter.CreateCounter<long>(
        "nodes.reservations.created",
        unit: "{reservations}",
        description: "Number of capacity reservations created");

    public static readonly Counter<long> ReservationsClaimed = Meter.CreateCounter<long>(
        "nodes.reservations.claimed",
        unit: "{reservations}",
        description: "Number of capacity reservations claimed");

    public static readonly Counter<long> ReservationsReleased = Meter.CreateCounter<long>(
        "nodes.reservations.released",
        unit: "{reservations}",
        description: "Number of capacity reservations explicitly released");

    public static readonly Counter<long> ReservationsExpired = Meter.CreateCounter<long>(
        "nodes.reservations.expired",
        unit: "{reservations}",
        description: "Number of capacity reservations that expired");

    public static readonly Counter<long> ReservationFailures = Meter.CreateCounter<long>(
        "nodes.reservations.failures",
        unit: "{failures}",
        description: "Number of failed reservation attempts");

    // Health scoring metrics
    public static readonly Histogram<int> HealthScoreDistribution = Meter.CreateHistogram<int>(
        "nodes.health.score",
        unit: "{score}",
        description: "Distribution of node health scores");

    public static readonly Counter<long> HealthStatusTransitions = Meter.CreateCounter<long>(
        "nodes.health.status_transitions",
        unit: "{transitions}",
        description: "Number of health-based status transitions");

    public static readonly Counter<long> HealthTrendChanges = Meter.CreateCounter<long>(
        "nodes.health.trend_changes",
        unit: "{changes}",
        description: "Number of health trend changes");

    /// <summary>
    /// Records a health score for a node.
    /// </summary>
    public static void RecordHealthScore(int score, string platform, string category)
    {
        HealthScoreDistribution.Record(score,
            new KeyValuePair<string, object?>("platform", platform),
            new KeyValuePair<string, object?>("category", category));
    }

    /// <summary>
    /// Records a health-based status transition.
    /// </summary>
    public static void RecordHealthStatusTransition(string fromStatus, string toStatus, string category)
    {
        HealthStatusTransitions.Add(1,
            new KeyValuePair<string, object?>("from_status", fromStatus),
            new KeyValuePair<string, object?>("to_status", toStatus),
            new KeyValuePair<string, object?>("health_category", category));
    }

    /// <summary>
    /// Records a health trend change.
    /// </summary>
    public static void RecordHealthTrendChange(string fromTrend, string toTrend)
    {
        HealthTrendChanges.Add(1,
            new KeyValuePair<string, object?>("from_trend", fromTrend),
            new KeyValuePair<string, object?>("to_trend", toTrend));
    }

    /// <summary>
    /// Records a node enrollment attempt.
    /// </summary>
    public static void RecordEnrollmentAttempt(string platform)
    {
        EnrollmentAttempts.Add(1, new KeyValuePair<string, object?>("platform", platform));
    }

    /// <summary>
    /// Records a successful node enrollment.
    /// </summary>
    public static void RecordEnrollmentSuccess(string platform)
    {
        EnrollmentSuccesses.Add(1, new KeyValuePair<string, object?>("platform", platform));
    }

    /// <summary>
    /// Records a failed node enrollment.
    /// </summary>
    public static void RecordEnrollmentFailure(string platform, string reason)
    {
        EnrollmentFailures.Add(1,
            new KeyValuePair<string, object?>("platform", platform),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Records a heartbeat received from an agent.
    /// </summary>
    public static void RecordHeartbeat(string platform, double processingTimeMs)
    {
        HeartbeatsReceived.Add(1, new KeyValuePair<string, object?>("platform", platform));
        HeartbeatProcessingTime.Record(processingTimeMs, new KeyValuePair<string, object?>("platform", platform));
    }

    /// <summary>
    /// Records a node status transition.
    /// </summary>
    public static void RecordStatusTransition(string fromStatus, string toStatus)
    {
        StatusTransitions.Add(1,
            new KeyValuePair<string, object?>("from_status", fromStatus),
            new KeyValuePair<string, object?>("to_status", toStatus));
    }
}
