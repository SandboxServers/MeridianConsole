using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Lab.Tools;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Dhadgar.Lab.Uat.Spec;

/// <summary>
/// Parses a UAT spec (C7) into the records of <see cref="UatSpec"/>. Every problem is a <see cref="UatProblem"/> with its
/// file and line (R-RESULT); the loader never throws for bad input. Structural checks that need the tool registry live in
/// <c>SpecValidator</c>.
/// </summary>
public static class SpecLoader
{
    private const int SupportedSchema = 1;
    private const int DefaultPollMs = 500;

    private static readonly HashSet<string> ClauseKeys = new(StringComparer.Ordinal)
    {
        "id", "text", "source", "op", "value", "pointer", "since", "timeout_ms", "poll_ms", "required",
    };

    /// <summary>
    /// Parses spec text. <paramref name="file"/> is the name used in problems (a file name or <c>catalog/&lt;file&gt;</c>).
    /// </summary>
    public static SpecLoadOutcome LoadText(string file, string text)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(text);

        var problems = new List<UatProblem>();
        DocumentSyntax document = SyntaxParser.Parse(text, file, validate: true);
        if (document.HasErrors)
        {
            foreach (DiagnosticMessage diagnostic in document.Diagnostics)
            {
                problems.Add(new UatProblem(file, diagnostic.Span.Start.Line + 1, "invalid TOML: " + diagnostic.Message));
            }

            return new SpecLoadOutcome(null, problems);
        }

        var reader = new Reader(file, text, problems);
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            problems.Add(new UatProblem(file, 1, "invalid TOML: " + ex.Message));
            return new SpecLoadOutcome(null, problems);
        }

        int schema = (int)reader.Int(root, "schema", 0, "schema");
        if (schema != SupportedSchema)
        {
            reader.Add("schema", "[Lab.InvalidArgument] schema must be 1");
        }

        UatSection? section = ReadSection(reader, root);
        List<UatRow> rows = ReadRows(reader, root);

        if (problems.Count > 0 || section is null)
        {
            return new SpecLoadOutcome(null, problems);
        }

        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new SpecLoadOutcome(new UatSpec(schema, section, rows, file, sha), problems);
    }

    private static UatSection? ReadSection(Reader reader, TomlTable root)
    {
        if (!root.TryGetValue("section", out object? raw) || raw is not TomlTable table)
        {
            reader.Add("section", "[Lab.InvalidArgument] the [section] table is missing");
            return null;
        }

        string id = reader.Str(table, "id", "section.id") ?? string.Empty;
        string title = reader.Str(table, "title", "section.title") ?? string.Empty;
        bool sequential = reader.Bool(table, "sequential", true, "sequential");
        IReadOnlyList<string> requires = reader.Strings(table, "requires", "requires");
        IReadOnlyList<string> allow = reader.Strings(table, "allow_invariants", "allow_invariants");
        long? seed = reader.OptionalLong(table, "seed", "seed");

        IReadOnlyDictionary<string, JsonElement>? pool = null;
        if (table.TryGetValue("requires_pool", out object? poolRaw) && poolRaw is TomlTable poolTable)
        {
            pool = poolTable.ToDictionary(kv => kv.Key, kv => ToJson(kv.Value), StringComparer.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            reader.Add("id", "[Lab.InvalidArgument] section id is required");
            return null;
        }

        return new UatSection(
            id,
            title,
            sequential,
            requires,
            seed is { } s && s >= 0 ? (ulong)s : null,
            allow,
            pool);
    }

    private static List<UatRow> ReadRows(Reader reader, TomlTable root)
    {
        var rows = new List<UatRow>();
        if (!root.TryGetValue("row", out object? raw) || raw is not TomlTableArray array)
        {
            return rows;
        }

        foreach (TomlTable rowTable in array)
        {
            string id = reader.Str(rowTable, "id", "row.id") ?? string.Empty;
            string title = reader.Str(rowTable, "title", "row.title") ?? string.Empty;
            string expected = reader.Str(rowTable, "expected", "row.expected") ?? string.Empty;
            string? knownIssue = reader.Str(rowTable, "known_issue", "known_issue");
            if (string.IsNullOrEmpty(knownIssue))
            {
                knownIssue = null;
            }

            rows.Add(new UatRow(
                id,
                title,
                expected,
                reader.Strings(rowTable, "depends_on", "depends_on"),
                knownIssue,
                reader.Bool(rowTable, "reuse_member", false, "reuse_member"),
                reader.Strings(rowTable, "allow_invariants", "allow_invariants"),
                ReadFaults(reader, rowTable),
                ReadSteps(reader, rowTable),
                ReadClauses(reader, rowTable)));
        }

        return rows;
    }

    private static List<UatFault> ReadFaults(Reader reader, TomlTable row)
    {
        var faults = new List<UatFault>();
        if (!row.TryGetValue("fault", out object? raw) || raw is not TomlTableArray array)
        {
            return faults;
        }

        foreach (TomlTable table in array)
        {
            string id = reader.Str(table, "id", "fault.id") ?? string.Empty;
            string start = reader.Str(table, "start", "start") ?? string.Empty;
            string heal = reader.Str(table, "heal", "heal") ?? string.Empty;
            string tool = string.Empty;
            JsonElement args = ToJson(new Dictionary<string, object?>(StringComparer.Ordinal));
            if (table.TryGetValue("inject", out object? injectRaw) && injectRaw is TomlTable inject)
            {
                tool = reader.Str(inject, "tool", "tool") ?? string.Empty;
                if (inject.TryGetValue("args", out object? argsRaw))
                {
                    args = ToJson(argsRaw);
                }
            }
            else
            {
                reader.Add("inject", "[Lab.InvalidArgument] a fault needs an inject table");
            }

            faults.Add(new UatFault(id, tool, args, start, heal));
        }

        return faults;
    }

    private static List<UatStep> ReadSteps(Reader reader, TomlTable row)
    {
        var steps = new List<UatStep>();
        if (!row.TryGetValue("step", out object? raw) || raw is not TomlTableArray array)
        {
            return steps;
        }

        foreach (TomlTable table in array)
        {
            var capture = new Dictionary<string, string>(StringComparer.Ordinal);
            if (table.TryGetValue("capture", out object? captureRaw) && captureRaw is TomlTable captureTable)
            {
                foreach (KeyValuePair<string, object> pair in captureTable)
                {
                    capture[pair.Key] = pair.Value as string ?? string.Empty;
                }
            }

            JsonElement? args = table.TryGetValue("args", out object? argsRaw) ? ToJson(argsRaw) : null;
            long? wait = reader.OptionalLong(table, "wait_ms", "wait_ms");
            steps.Add(new UatStep(
                reader.Str(table, "id", "step.id"),
                reader.Str(table, "tool", "tool"),
                args,
                capture,
                wait is { } w ? (int)Math.Clamp(w, int.MinValue, int.MaxValue) : null,
                reader.Str(table, "expect_error", "expect_error")));
        }

        return steps;
    }

    private static List<UatClause> ReadClauses(Reader reader, TomlTable row)
    {
        var clauses = new List<UatClause>();
        if (!row.TryGetValue("expect", out object? raw) || raw is not TomlTableArray array)
        {
            return clauses;
        }

        foreach (TomlTable table in array)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in table)
            {
                if (!ClauseKeys.Contains(pair.Key))
                {
                    fields[pair.Key] = ToJson(pair.Value);
                }
            }

            clauses.Add(new UatClause(
                reader.Str(table, "id", "expect.id") ?? string.Empty,
                reader.Str(table, "text", "text") ?? string.Empty,
                reader.Str(table, "source", "source") ?? string.Empty,
                reader.Str(table, "op", "op") ?? string.Empty,
                table.TryGetValue("value", out object? value) ? ToJson(value) : null,
                reader.Str(table, "pointer", "pointer"),
                reader.Str(table, "since", "since") ?? "row_start",
                (int)reader.Int(table, "timeout_ms", 0, "timeout_ms"),
                (int)reader.Int(table, "poll_ms", DefaultPollMs, "poll_ms"),
                reader.Bool(table, "required", true, "required"),
                fields));
        }

        return clauses;
    }

    /// <summary>
    /// Converts a Tomlyn value into JSON. Tables become objects, arrays become arrays, dates become ISO 8601 strings.
    /// </summary>
    internal static JsonElement ToJson(object? value) =>
        JsonSerializer.SerializeToElement(Plain(value), LabJson.Options);

    private static object? Plain(object? value) => value switch
    {
        TomlTable table => table.ToDictionary(kv => kv.Key, kv => Plain(kv.Value), StringComparer.Ordinal),
        TomlTableArray array => array.Select(Plain).ToList(),
        IEnumerable<object> list when value is not string => list.Select(Plain).ToList(),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        TomlDateTime date => date.ToString(),
        _ => value,
    };

    /// <summary>
    /// Reads typed values from one table and records a problem for each wrong type. Lines are found by key name in the text.
    /// </summary>
    private sealed class Reader
    {
        private readonly string _file;
        private readonly string[] _lines;
        private readonly List<UatProblem> _problems;

        public Reader(string file, string text, List<UatProblem> problems)
        {
            _file = file;
            _lines = text.Split('\n');
            _problems = problems;
        }

        public void Add(string key, string message) =>
            _problems.Add(new UatProblem(_file, LineOf(key), message));

        public string? Str(TomlTable table, string key, string label)
        {
            if (!table.TryGetValue(key, out object? value))
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            Add(label, "[Lab.InvalidArgument] " + key + " must be a string");
            return null;
        }

        public bool Bool(TomlTable table, string key, bool fallback, string label)
        {
            if (!table.TryGetValue(key, out object? value))
            {
                return fallback;
            }

            if (value is bool flag)
            {
                return flag;
            }

            Add(label, "[Lab.InvalidArgument] " + key + " must be true or false");
            return fallback;
        }

        public long Int(TomlTable table, string key, long fallback, string label)
        {
            if (!table.TryGetValue(key, out object? value))
            {
                return fallback;
            }

            if (value is long number)
            {
                return number;
            }

            Add(label, "[Lab.InvalidArgument] " + key + " must be an integer");
            return fallback;
        }

        public long? OptionalLong(TomlTable table, string key, string label) =>
            table.ContainsKey(key) ? Int(table, key, 0, label) : null;

        public List<string> Strings(TomlTable table, string key, string label)
        {
            if (!table.TryGetValue(key, out object? value))
            {
                return [];
            }

            if (value is not IEnumerable<object> items || value is string)
            {
                Add(label, "[Lab.InvalidArgument] " + key + " must be an array of strings");
                return [];
            }

            return items.Select(item => item as string ?? string.Empty).ToList();
        }

        private int LineOf(string key)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                string line = _lines[i].TrimStart();
                if (line.StartsWith(key + " ", StringComparison.Ordinal) || line.StartsWith(key + "=", StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }

            return 1;
        }
    }
}
