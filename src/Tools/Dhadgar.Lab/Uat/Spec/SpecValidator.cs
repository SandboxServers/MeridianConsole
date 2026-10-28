using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;

namespace Dhadgar.Lab.Uat.Spec;

/// <summary>
/// Plan-time checks of a loaded UAT spec (C7, L13). Runs no tool and touches nothing. Every rule adds a
/// <see cref="UatProblem"/> with the file and line of the offending text (R-RESULT); the validator never throws.
/// </summary>
public static class SpecValidator
{
    private const int MaxTimeoutMs = 300_000;
    private const int MaxWaitMs = 60_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex IdPattern = new(@"^[a-z0-9-]+\z", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex KnownIssuePattern = new(@"^KI-[A-Z0-9-]+\z", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ExpectErrorPattern = new(@"^(Lab\.[A-Za-z]+|http:\d{3})\z", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex VarPattern = new(@"\$\{([^}]*)\}", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly string[] ForbiddenStepPrefixes = ["lab_lease_", "uat_", "fault_", "job_", "hunt_", "results_", "triage_"];

    private static readonly HashSet<string> ForbiddenStepTools = new(StringComparer.Ordinal)
    {
        "faults_active", "fuzz_run", "chaos_run", "soak_run", "fleet_run", "pool_acquire", "pool_release",
    };

    private static readonly HashSet<string> FaultTools = new(StringComparer.Ordinal)
    {
        "fault_toxic_add", "fault_proxy_disable", "fault_partition", "fault_kill", "fault_pause",
        "fault_disk_fill", "fault_corrupt_file", "fault_corrupt_registry", "fault_db",
    };

    private static readonly HashSet<string> KnownRequires = new(StringComparer.Ordinal)
    {
        "docker", "windows", "windows_containers", "network",
    };

    private static readonly HashSet<string> PoolKeys = new(StringComparer.Ordinal)
    {
        "os", "min_os_build", "sku", "isolation", "kind", "arch", "reboot", "count",
    };

    private static readonly HashSet<string> KnownInvariants = new(StringComparer.Ordinal)
    {
        "I1", "I2", "I3", "I4", "I5", "I6", "I7",
    };

    private static readonly HashSet<string> BuiltinVars = new(StringComparer.Ordinal)
    {
        "run_id", "row_id", "section_id", "seed",
    };

    /// <summary>Required source fields per C7 source (the clause's top-level keys are not listed here).</summary>
    private static readonly Dictionary<string, string[]> SourceRequired = new(StringComparer.Ordinal)
    {
        ["db"] = ["sql"],
        ["log"] = ["process"],
        ["http"] = ["via", "method", "path"],
        ["tool"] = ["tool"],
        ["certstore"] = ["store"],
        ["registry"] = ["key"],
        ["state_file"] = ["file"],
        ["step"] = ["step"],
        ["invariant"] = ["id"],
        ["hub"] = ["event", "node_id"],
        ["agent_log"] = ["member"],
        ["game"] = ["member", "server_id"],
        ["human"] = [],
    };

    /// <summary>Optional source fields per C7 source.</summary>
    private static readonly Dictionary<string, string[]> SourceOptional = new(StringComparer.Ordinal)
    {
        ["db"] = ["params"],
        ["log"] = ["contains", "matches", "category", "level", "event_id"],
        ["http"] = ["headers", "body"],
        ["tool"] = ["args"],
        ["certstore"] = ["subject_contains", "issuer_contains", "issuer_thumbprint"],
        ["agent_log"] = ["contains", "matches"],
    };

    private static readonly HashSet<string> ValuelessOps = new(StringComparer.Ordinal)
    {
        "exists", "absent", "truthy", "falsy",
    };

    private static readonly HashSet<string> NumberOps = new(StringComparer.Ordinal)
    {
        "gt", "gte", "lt", "lte", "len_gte", "min_rows", "max_rows", "delta",
    };

    private static readonly HashSet<string> ScalarOps = new(StringComparer.Ordinal)
    {
        "eq", "ne", "contains", "not_contains",
    };

    /// <summary>
    /// Checks a loaded spec. <paramref name="text"/> is the file text, used only to find line numbers. An empty list means valid.
    /// Cross-file rules (section ids unique across specs) are checked by the plan tool, not here.
    /// </summary>
    public static IReadOnlyList<UatProblem> Validate(UatSpec spec, string text, LabToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(registry);

        var checker = new Checker(spec.File, text, registry);
        checker.Run(spec);
        return checker.Problems;
    }

    /// <summary>
    /// Checks the arguments of a tool call against its input schema: an object with known and required properties.
    /// Returns null when the arguments fit.
    /// </summary>
    internal static string? SchemaError(JsonElement args, JsonElement schema)
    {
        if (args.ValueKind == JsonValueKind.Undefined || schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!schema.TryGetProperty("properties", out JsonElement props) || props.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            return "args must be a table";
        }

        foreach (JsonProperty arg in args.EnumerateObject())
        {
            if (!props.TryGetProperty(arg.Name, out _))
            {
                return "unknown arg " + arg.Name;
            }
        }

        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in required.EnumerateArray())
            {
                string? name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (name is not null && name != "lease_id" && !args.TryGetProperty(name, out _))
                {
                    return "missing arg " + name;
                }
            }
        }

        return null;
    }

    private static string? StringProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool IsTargetId(string value) =>
        value == "${member}" || value.StartsWith("c:", StringComparison.Ordinal) || value.StartsWith("p:", StringComparison.Ordinal)
        || value.StartsWith("d:", StringComparison.Ordinal) || value.StartsWith("f:", StringComparison.Ordinal)
        || value.StartsWith("m:", StringComparison.Ordinal);

    private static bool IsAfterStep(string value, HashSet<string> stepIds) =>
        value.StartsWith("after_step:", StringComparison.Ordinal) && stepIds.Contains(value["after_step:".Length..]);

    private static bool ValidPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record VarScope(bool HasPool, IReadOnlySet<string> Captured, bool AllowSecret = false);

    private sealed class Checker
    {
        private readonly string _file;
        private readonly string[] _lines;
        private readonly LabToolRegistry _registry;

        public Checker(string file, string text, LabToolRegistry registry)
        {
            _file = file;
            _lines = text.Split('\n');
            _registry = registry;
        }

        public List<UatProblem> Problems { get; } = new();

        public void Run(UatSpec spec)
        {
            bool hasPool = spec.Section.RequiresPool is not null;
            if (!IdPattern.IsMatch(spec.Section.Id))
            {
                Add(spec.Section.Id, "section id must match [a-z0-9-]+");
            }

            foreach (string req in spec.Section.Requires)
            {
                if (!KnownRequires.Contains(req))
                {
                    Add(req, "unknown requires entry " + req);
                }
            }

            foreach (string inv in spec.Section.AllowInvariants)
            {
                if (!KnownInvariants.Contains(inv))
                {
                    Add(inv, "unknown invariant id " + inv);
                }
            }

            if (spec.Section.RequiresPool is { } pool)
            {
                foreach (string key in pool.Keys)
                {
                    if (!PoolKeys.Contains(key))
                    {
                        Add(key, "requires_pool key " + key + " is not a pool constraint");
                    }
                }
            }

            var rowIndex = new HashSet<string>(StringComparer.Ordinal);
            foreach (UatRow row in spec.Rows)
            {
                CheckRow(row, hasPool, rowIndex);
                if (IdPattern.IsMatch(row.Id))
                {
                    rowIndex.Add(row.Id);
                }
            }
        }

        private void CheckRow(UatRow row, bool hasPool, HashSet<string> earlierRows)
        {
            if (!IdPattern.IsMatch(row.Id))
            {
                Add(row.Id, "row id must match [a-z0-9-]+");
            }
            else if (earlierRows.Contains(row.Id))
            {
                Add(row.Id, "duplicate row id " + row.Id);
            }

            foreach (string dep in row.DependsOn)
            {
                if (!earlierRows.Contains(dep))
                {
                    Add(dep, "depends_on " + dep + " must name an earlier row of this section");
                }
            }

            if (!string.IsNullOrEmpty(row.KnownIssue) && !KnownIssuePattern.IsMatch(row.KnownIssue))
            {
                Add(row.KnownIssue, "known_issue must match ^KI-[A-Z0-9-]+$");
            }

            foreach (string inv in row.AllowInvariants)
            {
                if (!KnownInvariants.Contains(inv))
                {
                    Add(inv, "unknown invariant id " + inv);
                }
            }

            if (row.Steps.Count == 0)
            {
                Add(row.Id, "row " + row.Id + " needs at least one step");
            }

            if (!row.Expect.Any(c => c.Required))
            {
                Add(row.Id, "row " + row.Id + " needs at least one required clause");
            }

            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (UatStep step in row.Steps)
            {
                if (!string.IsNullOrEmpty(step.Id) && !stepIds.Add(step.Id))
                {
                    Add(step.Id, "duplicate step id " + step.Id);
                }
            }

            var faultIds = CheckFaults(row, hasPool, stepIds);
            HashSet<string> captured = CheckSteps(row, hasPool, stepIds);
            CheckClauses(row, hasPool, captured, stepIds, faultIds);
        }

        private HashSet<string> CheckFaults(UatRow row, bool hasPool, HashSet<string> stepIds)
        {
            var faultIds = new HashSet<string>(StringComparer.Ordinal);
            var scope = new VarScope(hasPool, new HashSet<string>(StringComparer.Ordinal));
            foreach (UatFault fault in row.Faults)
            {
                if (!IdPattern.IsMatch(fault.Id))
                {
                    Add(fault.Id, "fault id must match [a-z0-9-]+");
                }
                else if (!faultIds.Add(fault.Id))
                {
                    Add(fault.Id, "duplicate fault id " + fault.Id);
                }

                if (!FaultTools.Contains(fault.Tool))
                {
                    Add(fault.Id, "fault tool " + fault.Tool + " is not a C4 fault tool");
                }
                else if (_registry.Find(fault.Tool)?.InputSchema is { } schema && SchemaError(fault.Args, schema) is { } error)
                {
                    Add(fault.Id, "fault " + fault.Id + ": " + error);
                }

                CheckJson(fault.Args, fault.Id, scope);
                if (StringProperty(fault.Args, "target") is { } target && !IsTargetId(target))
                {
                    Add(target, "target " + target + " has no C10 id form");
                }

                if (fault.Start != "before_steps" && !IsAfterStep(fault.Start, stepIds))
                {
                    Add(fault.Id, "fault start must be before_steps or after_step:<id>");
                }

                if (fault.Heal is not ("after_steps" or "never") && !IsAfterStep(fault.Heal, stepIds))
                {
                    Add(fault.Id, "fault heal must be after_steps, never or after_step:<id>");
                }
            }

            return faultIds;
        }

        private HashSet<string> CheckSteps(UatRow row, bool hasPool, HashSet<string> stepIds)
        {
            var captured = new HashSet<string>(StringComparer.Ordinal);
            foreach (UatStep step in row.Steps)
            {
                string needle = string.IsNullOrEmpty(step.Id) ? (step.Tool ?? row.Id) : step.Id;
                bool hasTool = !string.IsNullOrEmpty(step.Tool);
                if (hasTool == (step.WaitMs is not null))
                {
                    Add(needle, "a step is either a tool step or a pause step");
                }

                if (hasTool)
                {
                    CheckStepTool(step.Tool!, needle);
                    if (step.Args is { ValueKind: not JsonValueKind.Undefined } args)
                    {
                        CheckJson(args, needle, new VarScope(hasPool, captured));
                    }
                }

                if (step.WaitMs is { } wait && (wait < 0 || wait > MaxWaitMs))
                {
                    Add(needle, "wait_ms must be between 0 and 60000");
                }

                if (!string.IsNullOrEmpty(step.ExpectError) && !ExpectErrorPattern.IsMatch(step.ExpectError))
                {
                    Add(step.ExpectError, "expect_error must be Lab.<Code> or http:<status>");
                }

                foreach (KeyValuePair<string, string> capture in step.Capture)
                {
                    if (capture.Value.Length > 0 && !capture.Value.StartsWith('/'))
                    {
                        Add(capture.Value, "capture pointer must start with /");
                    }

                    captured.Add(capture.Key);
                }
            }

            return captured;
        }

        private void CheckClauses(UatRow row, bool hasPool, HashSet<string> captured, HashSet<string> stepIds, HashSet<string> faultIds)
        {
            var clauseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (UatClause clause in row.Expect)
            {
                if (!IdPattern.IsMatch(clause.Id))
                {
                    Add(clause.Id, "clause id must match [a-z0-9-]+");
                }
                else if (!clauseIds.Add(clause.Id))
                {
                    Add(clause.Id, "duplicate clause id " + clause.Id);
                }

                CheckClause(clause, new VarScope(hasPool, captured), stepIds, faultIds);
            }
        }

        private void CheckClause(UatClause clause, VarScope scope, HashSet<string> stepIds, HashSet<string> faultIds)
        {
            string needle = clause.Id;
            CheckVars(clause.Text, needle, scope);
            if (clause.Value is { ValueKind: not JsonValueKind.Undefined } value)
            {
                CheckJson(value, needle, scope);
            }

            if (!string.IsNullOrEmpty(clause.Pointer) && !clause.Pointer.StartsWith('/'))
            {
                Add(clause.Pointer, "pointer must start with /");
            }

            if (clause.TimeoutMs < 0 || clause.TimeoutMs > MaxTimeoutMs)
            {
                Add(needle, "timeout_ms must be between 0 and 300000");
            }

            if (clause.PollMs <= 0)
            {
                Add(needle, "poll_ms must be positive");
            }

            CheckSince(clause.Since, needle, stepIds, faultIds);
            CheckSource(clause, scope, stepIds);
            CheckOp(clause, scope);
        }

        private void CheckSource(UatClause clause, VarScope scope, HashSet<string> stepIds)
        {
            string needle = clause.Id;
            if (!SourceRequired.TryGetValue(clause.Source, out string[]? required))
            {
                Add(needle, "unknown source " + clause.Source);
                return;
            }

            string[] optional = SourceOptional.TryGetValue(clause.Source, out string[]? opt) ? opt : [];
            foreach (string key in required)
            {
                if (!clause.SourceFields.ContainsKey(key))
                {
                    Add(needle, "source " + clause.Source + " needs field " + key);
                }
            }

            foreach (KeyValuePair<string, JsonElement> field in clause.SourceFields)
            {
                if (!required.Contains(field.Key) && !optional.Contains(field.Key))
                {
                    Add(needle, "source " + clause.Source + " has no field " + field.Key);
                }

                bool secretOk = clause.Source == "http" && field.Key == "body";
                CheckJson(field.Value, needle, scope with { AllowSecret = secretOk });
            }

            if (clause.Source is "log" or "agent_log"
                && !clause.SourceFields.ContainsKey("contains") && !clause.SourceFields.ContainsKey("matches"))
            {
                Add(needle, "source " + clause.Source + " needs contains or matches");
            }

            if (clause.Source == "tool" && clause.SourceFields.TryGetValue("tool", out JsonElement toolField))
            {
                string tool = toolField.ValueKind == JsonValueKind.String ? toolField.GetString() ?? string.Empty : string.Empty;
                if (ToolPolicy.GateFor(tool) != LabGate.Open)
                {
                    Add(needle, "tool source only reads OPEN tools; " + tool + " is not OPEN");
                }
                else if (_registry.Find(tool) is null)
                {
                    Add(needle, "unknown tool " + tool);
                }
            }

            if (clause.Source == "invariant"
                && clause.SourceFields.TryGetValue("id", out JsonElement invId)
                && invId.ValueKind == JsonValueKind.String
                && !KnownInvariants.Contains(invId.GetString() ?? string.Empty))
            {
                Add(needle, "unknown invariant id " + invId.GetString());
            }

            if (clause.Source == "step"
                && clause.SourceFields.TryGetValue("step", out JsonElement stepRef)
                && (stepRef.ValueKind != JsonValueKind.String || !stepIds.Contains(stepRef.GetString() ?? string.Empty)))
            {
                Add(needle, "step source names no step of this row");
            }
        }

        private void CheckOp(UatClause clause, VarScope scope)
        {
            string needle = clause.Id;
            if (ValuelessOps.Contains(clause.Op))
            {
                return;
            }

            bool known = NumberOps.Contains(clause.Op) || ScalarOps.Contains(clause.Op)
                || clause.Op is "matches" or "approx" or "any";
            if (!known)
            {
                Add(needle, "unknown op " + clause.Op);
                return;
            }

            if (clause.Value is not { ValueKind: not JsonValueKind.Undefined } value)
            {
                Add(needle, "op " + clause.Op + " needs a value");
                return;
            }

            bool shapeOk = clause.Op switch
            {
                "approx" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2 && AllNumbers(value),
                "any" => value.ValueKind == JsonValueKind.Array,
                "matches" => value.ValueKind == JsonValueKind.String && ValidPattern(value.GetString() ?? string.Empty),
                _ when NumberOps.Contains(clause.Op) => value.ValueKind == JsonValueKind.Number,
                _ => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False,
            };

            if (!shapeOk)
            {
                Add(needle, "value has the wrong shape for op " + clause.Op);
            }

            CheckJson(value, needle, scope);
        }

        private static bool AllNumbers(JsonElement array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }
            }

            return true;
        }

        private void CheckSince(string since, string needle, HashSet<string> stepIds, HashSet<string> faultIds)
        {
            // row_start is the loader's default (the clause is measured from the row's first step).
            if (string.IsNullOrEmpty(since) || since == "row_start")
            {
                return;
            }

            if (since.StartsWith("step:", StringComparison.Ordinal))
            {
                if (!stepIds.Contains(since["step:".Length..]))
                {
                    Add(needle, "since names no step of this row");
                }

                return;
            }

            if (since.StartsWith("fault_heal:", StringComparison.Ordinal))
            {
                if (!faultIds.Contains(since["fault_heal:".Length..]))
                {
                    Add(needle, "since names no fault of this row");
                }

                return;
            }

            Add(needle, "since must be step:<id> or fault_heal:<id>");
        }

        private void CheckStepTool(string tool, string needle)
        {
            if (ForbiddenStepTools.Contains(tool) || ForbiddenStepPrefixes.Any(p => tool.StartsWith(p, StringComparison.Ordinal)))
            {
                Add(needle, "tool " + tool + " cannot be a step");
                return;
            }

            if (_registry.Find(tool) is null)
            {
                Add(needle, "unknown tool " + tool);
            }

            LabGate? gate = ToolPolicy.GateFor(tool);
            if (gate is null)
            {
                Add(needle, "tool " + tool + " is not classified");
            }
            else if (gate == LabGate.OwnLease)
            {
                Add(needle, "tool " + tool + " takes its own lease and cannot be a step");
            }
        }

        private void CheckJson(JsonElement element, string needle, VarScope scope)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    CheckVars(element.GetString(), needle, scope);
                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.Name == "lease_id")
                        {
                            Add(needle, "lease_id never appears in a spec");
                        }

                        CheckJson(property.Value, needle, scope);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        CheckJson(item, needle, scope);
                    }

                    break;
            }
        }

        private void CheckVars(string? value, string needle, VarScope scope)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (Match match in VarPattern.Matches(value))
            {
                string name = match.Groups[1].Value;
                if (name.StartsWith("secret:", StringComparison.Ordinal))
                {
                    if (!scope.AllowSecret)
                    {
                        Add(needle, "${secret:...} is allowed only in http bodies");
                    }

                    continue;
                }

                if (name == "member")
                {
                    if (!scope.HasPool)
                    {
                        Add(needle, "${member} needs a section with requires_pool");
                    }

                    continue;
                }

                if (!BuiltinVars.Contains(name) && !scope.Captured.Contains(name))
                {
                    Add(needle, "unknown var ${" + name + "}");
                }
            }
        }

        private void Add(string needle, string message) =>
            Problems.Add(new UatProblem(_file, LineOf(needle), "[Lab.InvalidArgument] " + message));

        private int LineOf(string needle)
        {
            if (!string.IsNullOrEmpty(needle))
            {
                for (int i = 0; i < _lines.Length; i++)
                {
                    if (_lines[i].Contains(needle, StringComparison.Ordinal))
                    {
                        return i + 1;
                    }
                }
            }

            return 1;
        }
    }
}
