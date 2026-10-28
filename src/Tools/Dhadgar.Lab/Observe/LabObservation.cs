using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// The kind of one observation line (C18). Serialised as snake_case strings (<c>invoke</c>, <c>fault_prepare</c>).
/// </summary>
public enum ObservationKind { Invoke, Complete, FaultPrepare, FaultInject, FaultHeal, Observe, Note }

/// <summary>
/// The outcome of an operation (C18). <c>Unknown</c> means the operation may or may not have taken effect.
/// </summary>
public enum OpOutcome { Ok, Fail, Unknown }

/// <summary>
/// How closely a replay can follow the original run (C18). Serialised as <c>exact</c>, <c>schedule_approximate</c>, <c>evidence_only</c>.
/// </summary>
public enum ReplayFidelity { Exact, ScheduleApproximate, EvidenceOnly }

/// <summary>
/// One ordered line of <c>events.jsonl</c> (C18). <see cref="Seq"/> is assigned by the sink: 1, 2, 3 ... per run.
/// </summary>
/// <param name="Seq">Assigned by the sink; the total order of Append calls for the run.</param>
/// <param name="RunId">The run the line belongs to.</param>
/// <param name="At">The time from <see cref="TimeProvider"/>.</param>
/// <param name="Actor">The logical actor (<see cref="ActorIds"/>).</param>
/// <param name="OpId">Set on Invoke and Complete, and on Observe lines caused by an op.</param>
/// <param name="Kind">The observation kind.</param>
/// <param name="Op">The tool or operation name, e.g. <c>enroll</c>, <c>fault_toxic_add</c>.</param>
/// <param name="Args">Redacted arguments.</param>
/// <param name="Outcome">Set on Complete only.</param>
/// <param name="Result">Redacted result: status, error code, ids.</param>
/// <param name="CausedBy">The op id or fault id this line follows from.</param>
public sealed record LabObservation(
    long Seq,
    string RunId,
    DateTimeOffset At,
    string Actor,
    string? OpId,
    ObservationKind Kind,
    string Op,
    JsonElement Args,
    OpOutcome? Outcome,
    JsonElement Result,
    string? CausedBy);

/// <summary>
/// The pairing of two runs in an experiment (C18). <c>Role</c> is <c>base</c> or <c>variant</c>.
/// </summary>
public sealed record PairInfo(string ExperimentId, string Role, string PartnerRunId, string Relation);

/// <summary>
/// JSON options for observation lines and fingerprints: snake_case names, string enums.
/// </summary>
public static class ObserveJson
{
    /// <summary>
    /// The shared serializer options.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
