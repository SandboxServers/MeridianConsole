using Dhadgar.Lab.Uat.Spec;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Dhadgar.Lab.History;

/// <summary>
/// The result of loading a scenario file: the scenarios read and every problem found (R-RESULT).
/// </summary>
/// <param name="Scenarios">The scenarios; empty when any problem was found.</param>
/// <param name="Problems">Each problem with its file and line.</param>
public sealed record HistoryScenarioLoadOutcome(IReadOnlyList<HistoryScenario> Scenarios, IReadOnlyList<UatProblem> Problems);

/// <summary>
/// Parses <c>enroll-history.toml</c> (C19) with Tomlyn. Unknown keys are refused, every problem is listed with its line, and
/// the loader never throws for bad input.
/// </summary>
public static class HistoryScenarioLoader
{
    private const int SupportedSchema = 1;
    private const int MinClients = 1;
    private const int MaxClients = 8;
    private const string FaultStart = "before_steps";

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "schema", "scenario" };
    private static readonly HashSet<string> ScenarioKeys = new(StringComparer.Ordinal)
    {
        "id", "clients", "retry", "first_op_action", "fault",
    };

    private static readonly HashSet<string> FaultKeys = new(StringComparer.Ordinal) { "id", "start", "heal", "inject" };
    private static readonly HashSet<string> InjectKeys = new(StringComparer.Ordinal) { "tool", "args" };

    private static readonly Dictionary<string, HistoryRetry> Retries = new(StringComparer.Ordinal)
    {
        ["none"] = HistoryRetry.None,
        ["same_key"] = HistoryRetry.SameKey,
        ["new_key"] = HistoryRetry.NewKey,
    };

    private static readonly Dictionary<string, HistoryFirstOpAction> FirstOpActions = new(StringComparer.Ordinal)
    {
        ["hold_request"] = HistoryFirstOpAction.HoldRequest,
        ["drop_response"] = HistoryFirstOpAction.DropResponse,
    };

    /// <summary>
    /// Parses scenario text. <paramref name="file"/> is the name used in problems.
    /// </summary>
    public static HistoryScenarioLoadOutcome LoadText(string file, string text)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(text);

        var problems = new List<UatProblem>();
        var scenarios = new List<HistoryScenario>();

        DocumentSyntax document = SyntaxParser.Parse(text, file, validate: true);
        if (document.HasErrors)
        {
            foreach (DiagnosticMessage diagnostic in document.Diagnostics)
            {
                problems.Add(new UatProblem(file, diagnostic.Span.Start.Line + 1, "invalid TOML: " + diagnostic.Message));
            }

            return new HistoryScenarioLoadOutcome(scenarios, problems);
        }

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            problems.Add(new UatProblem(file, 1, "invalid TOML: " + ex.Message));
            return new HistoryScenarioLoadOutcome(scenarios, problems);
        }

        var lines = text.Split('\n');
        foreach (string key in root.Keys)
        {
            if (!RootKeys.Contains(key))
            {
                problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] unknown key " + key));
            }
        }

        if (!root.TryGetValue("schema", out object? schemaRaw) || schemaRaw is not long schema || schema != SupportedSchema)
        {
            problems.Add(new UatProblem(file, LineOf(lines, "schema"), "[Lab.InvalidArgument] schema must be 1"));
        }

        if (!root.TryGetValue("scenario", out object? raw) || raw is not TomlTableArray array || array.Count == 0)
        {
            problems.Add(new UatProblem(file, 1, "[Lab.InvalidArgument] at least one [[scenario]] table is required"));
            return new HistoryScenarioLoadOutcome(scenarios, problems);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TomlTable table in array)
        {
            HistoryScenario? scenario = ReadScenario(file, lines, table, problems);
            if (scenario is null)
            {
                continue;
            }

            if (!seen.Add(scenario.Id))
            {
                problems.Add(new UatProblem(file, LineOf(lines, "id"), "[Lab.InvalidArgument] duplicate scenario id " + scenario.Id));
                continue;
            }

            scenarios.Add(scenario);
        }

        return problems.Count > 0
            ? new HistoryScenarioLoadOutcome(new List<HistoryScenario>(), problems)
            : new HistoryScenarioLoadOutcome(scenarios, problems);
    }

    private static HistoryScenario? ReadScenario(string file, string[] lines, TomlTable table, List<UatProblem> problems)
    {
        int before = problems.Count;
        foreach (string key in table.Keys)
        {
            if (!ScenarioKeys.Contains(key))
            {
                problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] unknown key " + key));
            }
        }

        string? id = ReadString(file, lines, table, "id", problems);
        if (string.IsNullOrWhiteSpace(id))
        {
            problems.Add(new UatProblem(file, LineOf(lines, "id"), "[Lab.InvalidArgument] scenario id is required"));
        }

        long clients = ReadLong(file, lines, table, "clients", problems);
        if (clients is < MinClients or > MaxClients)
        {
            problems.Add(new UatProblem(file, LineOf(lines, "clients"), "[Lab.InvalidArgument] clients must be 1 to 8"));
        }

        HistoryRetry retry = HistoryRetry.None;
        string? retryText = ReadString(file, lines, table, "retry", problems);
        if (retryText is null || !Retries.TryGetValue(retryText, out retry))
        {
            problems.Add(new UatProblem(file, LineOf(lines, "retry"), "[Lab.InvalidArgument] retry must be none, same_key or new_key"));
        }

        HistoryFirstOpAction action = HistoryFirstOpAction.HoldRequest;
        string? actionText = ReadString(file, lines, table, "first_op_action", problems);
        if (actionText is null || !FirstOpActions.TryGetValue(actionText, out action))
        {
            problems.Add(new UatProblem(file, LineOf(lines, "first_op_action"), "[Lab.InvalidArgument] first_op_action must be hold_request or drop_response"));
        }

        var faults = ReadFaults(file, lines, table, problems);
        if (problems.Count > before || id is null)
        {
            return null;
        }

        return new HistoryScenario(id, (int)clients, retry, action, faults);
    }

    private static List<UatFault> ReadFaults(string file, string[] lines, TomlTable table, List<UatProblem> problems)
    {
        var faults = new List<UatFault>();
        if (!table.TryGetValue("fault", out object? raw))
        {
            return faults;
        }

        if (raw is not TomlTableArray array)
        {
            problems.Add(new UatProblem(file, LineOf(lines, "fault"), "[Lab.InvalidArgument] fault must be an array of tables"));
            return faults;
        }

        foreach (TomlTable fault in array)
        {
            foreach (string key in fault.Keys)
            {
                if (!FaultKeys.Contains(key))
                {
                    problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] unknown key " + key));
                }
            }

            string id = ReadString(file, lines, fault, "id", problems) ?? string.Empty;
            string start = ReadString(file, lines, fault, "start", problems) ?? string.Empty;
            string heal = ReadString(file, lines, fault, "heal", problems) ?? string.Empty;
            if (start != FaultStart)
            {
                problems.Add(new UatProblem(file, LineOf(lines, "start"), "[Lab.InvalidArgument] fault start must be before_steps"));
            }

            string tool = string.Empty;
            System.Text.Json.JsonElement args = SpecLoader.ToJson(new Dictionary<string, object?>(StringComparer.Ordinal));
            if (fault.TryGetValue("inject", out object? injectRaw) && injectRaw is TomlTable inject)
            {
                foreach (string key in inject.Keys)
                {
                    if (!InjectKeys.Contains(key))
                    {
                        problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] unknown key " + key));
                    }
                }

                tool = ReadString(file, lines, inject, "tool", problems) ?? string.Empty;
                if (inject.TryGetValue("args", out object? argsRaw))
                {
                    args = SpecLoader.ToJson(argsRaw);
                }
            }
            else
            {
                problems.Add(new UatProblem(file, LineOf(lines, "inject"), "[Lab.InvalidArgument] a fault needs an inject table"));
            }

            faults.Add(new UatFault(id, tool, args, start, heal));
        }

        return faults;
    }

    private static string? ReadString(string file, string[] lines, TomlTable table, string key, List<UatProblem> problems)
    {
        if (!table.TryGetValue(key, out object? value))
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] " + key + " must be a string"));
        return null;
    }

    private static long ReadLong(string file, string[] lines, TomlTable table, string key, List<UatProblem> problems)
    {
        if (!table.TryGetValue(key, out object? value))
        {
            return 0;
        }

        if (value is long number)
        {
            return number;
        }

        problems.Add(new UatProblem(file, LineOf(lines, key), "[Lab.InvalidArgument] " + key + " must be an integer"));
        return 0;
    }

    private static int LineOf(string[] lines, string key)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (line.StartsWith(key + " ", StringComparison.Ordinal) || line.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return 1;
    }
}
