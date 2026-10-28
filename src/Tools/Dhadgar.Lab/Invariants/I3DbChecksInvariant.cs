using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// One read-only <c>[[check]]</c> from an invariant TOML file (C11). Any returned row is a violation.
/// </summary>
public sealed record DbCheck(string Service, string CheckId, string Database, string Text, string Sql);

/// <summary>
/// I3: DB consistency (C11). Every <c>[[check]]</c> in <c>docs/guides/uat-specs/invariants/&lt;service&gt;.toml</c> must return zero rows.
/// Each check runs with <see cref="MaxRows"/> as its row cap; the first <see cref="EvidenceRows"/> rows are the evidence.
/// A check whose query fails makes the whole invariant a check error; the other checks still run.
/// </summary>
public sealed class I3DbChecksInvariant : ISkippableInvariant
{
    /// <summary>The row cap for each check.</summary>
    public const int MaxRows = 50;

    /// <summary>How many returned rows are kept as evidence.</summary>
    public const int EvidenceRows = 5;

    private const int MaxDetailLength = 512;
    private const string SoakFile = "soak.toml";

    private readonly string _directory;
    private readonly InvariantSqlQuery _query;
    private readonly Redactor _redactor;
    private readonly InvariantDbGate? _gate;

    /// <summary>
    /// Creates the check over the TOML files in <paramref name="checksDirectory"/>. <c>soak.toml</c> belongs to I7 and is skipped.
    /// The optional gate answers whether a database can be checked yet (D-ML80); without it every database is checked.
    /// </summary>
    public I3DbChecksInvariant(string checksDirectory, InvariantSqlQuery query, Redactor redactor, InvariantDbGate? gate = null)
    {
        _directory = checksDirectory;
        _query = query;
        _redactor = redactor;
        _gate = gate;
    }

    /// <inheritdoc />
    public string Id => "I3";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        Result<InvariantOutcome> outcome = await CheckWithSkipsAsync(window, ct).ConfigureAwait(false);
        return outcome.IsSuccess
            ? Result<IReadOnlyList<InvariantViolation>>.Success(outcome.Value.Violations)
            : Result<IReadOnlyList<InvariantViolation>>.Failure(outcome.Error);
    }

    /// <inheritdoc />
    public async Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct)
    {
        Result<IReadOnlyList<DbCheck>> checks = LoadChecks(_directory);
        if (checks.IsFailure)
        {
            return Result<InvariantOutcome>.Failure(checks.Error);
        }

        var violations = new List<InvariantViolation>();
        var skipped = new List<InvariantSkip>();
        var gated = new Dictionary<string, DbApplicability>(StringComparer.Ordinal);
        string? firstError = null;

        foreach (DbCheck check in checks.Value)
        {
            ct.ThrowIfCancellationRequested();
            if (_gate is not null)
            {
                if (!gated.TryGetValue(check.Database, out DbApplicability state))
                {
                    Result<DbApplicability> answer = await _gate(check.Database, ct).ConfigureAwait(false);
                    if (answer.IsFailure)
                    {
                        firstError ??= $"{check.Service}/{check.CheckId}: {answer.Error}";
                        continue;
                    }

                    state = answer.Value;
                    gated[check.Database] = state;
                    if (state != DbApplicability.Applicable)
                    {
                        skipped.Add(new InvariantSkip(Id, "db:" + check.Database, I2OutboxInvariant.SkipReason(state)));
                    }
                }

                if (state != DbApplicability.Applicable)
                {
                    continue;
                }
            }

            Result<DbQueryResult> result = await _query(check.Database, check.Sql, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                firstError ??= $"{check.Service}/{check.CheckId}: {result.Error}";
                continue;
            }

            if (result.Value.RowCount == 0)
            {
                continue;
            }

            string detail = $"{check.Text}: {result.Value.RowCount} row(s)";
            JsonElement evidence = _redactor.Redact(JsonSerializer.SerializeToElement(new
            {
                rows = result.Value.Rows.Take(EvidenceRows).ToArray(),
            }));

            violations.Add(new InvariantViolation(
                Id,
                check.Service,
                check.CheckId,
                detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail,
                window.To,
                evidence));
        }

        return firstError is null
            ? Result<InvariantOutcome>.Success(new InvariantOutcome(violations, skipped))
            : Result<InvariantOutcome>.Failure(firstError);
    }

    /// <summary>
    /// Reads every invariant TOML file in the directory, except <c>soak.toml</c>, in name order.
    /// </summary>
    internal static Result<IReadOnlyList<DbCheck>> LoadChecks(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Result<IReadOnlyList<DbCheck>>.Failure($"[Lab.NotFound] invariant directory is missing: {Path.GetFileName(directory)}");
        }

        var checks = new List<DbCheck>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.toml").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (string.Equals(Path.GetFileName(file), SoakFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Result<IReadOnlyList<DbCheck>> parsed = ParseChecks(Path.GetFileName(file), File.ReadAllText(file));
            if (parsed.IsFailure)
            {
                return Result<IReadOnlyList<DbCheck>>.Failure(parsed.Error);
            }

            checks.AddRange(parsed.Value);
        }

        return Result<IReadOnlyList<DbCheck>>.Success(checks);
    }

    /// <summary>
    /// Parses one invariant file: <c>schema = 1</c>, a <c>service</c> name, and <c>[[check]]</c> tables with <c>id</c>, <c>database</c> and <c>sql</c> (<c>text</c> is optional).
    /// </summary>
    internal static Result<IReadOnlyList<DbCheck>> ParseChecks(string file, string text)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            return Result<IReadOnlyList<DbCheck>>.Failure($"{file}: invalid TOML: {ex.Message}");
        }

        if (!root.TryGetValue("schema", out object? schema) || schema is not long schemaVersion || schemaVersion != 1)
        {
            return Result<IReadOnlyList<DbCheck>>.Failure($"{file}: schema must be 1");
        }

        if (!TryString(root, "service", out string service))
        {
            return Result<IReadOnlyList<DbCheck>>.Failure($"{file}: service is required");
        }

        var checks = new List<DbCheck>();
        if (root.TryGetValue("check", out object? raw) && raw is TomlTableArray array)
        {
            foreach (TomlTable table in array)
            {
                if (!TryString(table, "id", out string id)
                    || !TryString(table, "database", out string database)
                    || !TryString(table, "sql", out string sql))
                {
                    return Result<IReadOnlyList<DbCheck>>.Failure($"{file}: every [[check]] needs id, database and sql");
                }

                string label = TryString(table, "text", out string explicitText) ? explicitText : id;
                checks.Add(new DbCheck(service, id, database, label, sql));
            }
        }

        return Result<IReadOnlyList<DbCheck>>.Success(checks);
    }

    private static bool TryString(TomlTable table, string key, out string value)
    {
        if (table.TryGetValue(key, out object? raw) && raw is string text && text.Length > 0)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
