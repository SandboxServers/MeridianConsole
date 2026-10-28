using System.Text.Json;

namespace Dhadgar.Lab.Uat.Spec;

/// <summary>
/// One parsed UAT spec file (C7, schema 1). <see cref="Sha256"/> is the hash of the file bytes, recorded in the run bundle.
/// </summary>
public sealed record UatSpec(int Schema, UatSection Section, IReadOnlyList<UatRow> Rows, string File, string Sha256);

/// <summary>
/// The <c>[section]</c> table of a spec.
/// </summary>
public sealed record UatSection(
    string Id,
    string Title,
    bool Sequential,
    IReadOnlyList<string> Requires,
    ulong? Seed,
    IReadOnlyList<string> AllowInvariants,
    IReadOnlyDictionary<string, JsonElement>? RequiresPool);

/// <summary>
/// One <c>[[row]]</c> table. <see cref="KnownIssue"/> is null when the row claims no known issue.
/// </summary>
public sealed record UatRow(
    string Id,
    string Title,
    string Expected,
    IReadOnlyList<string> DependsOn,
    string? KnownIssue,
    bool ReuseMember,
    IReadOnlyList<string> AllowInvariants,
    IReadOnlyList<UatFault> Faults,
    IReadOnlyList<UatStep> Steps,
    IReadOnlyList<UatClause> Expect);

/// <summary>
/// One <c>[[row.fault]]</c>: <see cref="Tool"/> and <see cref="Args"/> come from its <c>inject</c> table.
/// </summary>
public sealed record UatFault(string Id, string Tool, JsonElement Args, string Start, string Heal);

/// <summary>
/// One <c>[[row.step]]</c>. A pause step has only <see cref="WaitMs"/>; a tool step has <see cref="Tool"/>.
/// </summary>
public sealed record UatStep(
    string? Id,
    string? Tool,
    JsonElement? Args,
    IReadOnlyDictionary<string, string> Capture,
    int? WaitMs,
    string? ExpectError);

/// <summary>
/// One <c>[[row.expect]]</c> clause. <see cref="SourceFields"/> holds the source-specific keys (C7 table) as JSON.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Pointer is the C7 field name for an RFC 6901 pointer; the spec names it this way.")]
public sealed record UatClause(
    string Id,
    string Text,
    string Source,
    string Op,
    JsonElement? Value,
    string? Pointer,
    string Since,
    int TimeoutMs,
    int PollMs,
    bool Required,
    IReadOnlyDictionary<string, JsonElement> SourceFields);

/// <summary>
/// One load or validation problem (R-RESULT). Every problem names its file and line; none is an exception.
/// </summary>
public sealed record UatProblem(string File, int Line, string Message);

/// <summary>
/// The outcome of loading a spec. <see cref="Spec"/> is null when any problem was found.
/// </summary>
public sealed record SpecLoadOutcome(UatSpec? Spec, IReadOnlyList<UatProblem> Problems);
