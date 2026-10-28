using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Twins;

/// <summary>
/// One fault a twin variant injects: the C4 fault tool and its arguments, as written in the experiment file.
/// </summary>
public sealed record FaultSpec(string Tool, JsonElement Args);

/// <summary>
/// The relation an experiment asserts (C19). <c>equal_outcome</c>: every compared pointer matches in both worlds.
/// <c>isolated</c>: the variant world adds a second tenant, and tenant A's observations match while tenant B never sees A's ids.
/// </summary>
public enum TwinRelation
{
    EqualOutcome,
    Isolated,
}

/// <summary>
/// One world of an experiment: the catalog spec, the rows that run in it, and (variant only) the faults it injects.
/// </summary>
public sealed record TwinWorld(string Spec, IReadOnlyList<string> Rows, IReadOnlyList<FaultSpec> Faults);

/// <summary>
/// Optional variant-only rows that run on the reset stack before the compared rows (C19 <c>pre</c>).
/// </summary>
public sealed record TwinPre(string Spec, IReadOnlyList<string> Rows);

/// <summary>
/// One comparison: either a pointer that must match in both worlds, or two variant pointers whose lists share no element (<c>op = "disjoint"</c>).
/// </summary>
public sealed record TwinCompare(string? Locator, IReadOnlyList<string>? VariantLocators, string? Op);

/// <summary>
/// A parsed twin experiment (C19 "Twin experiments"). Loaded by <see cref="TwinSpecLoader"/>; never executed by the loader.
/// </summary>
public sealed record TwinSpec(int Schema, string Id, TwinRelation Relation, ulong? Seed, TwinWorld Base, TwinWorld Variant, TwinPre? Pre, IReadOnlyList<TwinCompare> Compares);

/// <summary>
/// Parses and validates a twin experiment file (C19). Unknown keys are refused, and so is any fault tool that is not a C4
/// fault-injecting tool (R-CONTRACT). The base world is never faulted: the variant's faults are the experiment.
/// </summary>
public static class TwinSpecLoader
{
    /// <summary>The experiment id and the file name both match this (R-PATH).</summary>
    public static readonly Regex IdPattern = new("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The C4 fault tools that inject a fault. <c>fault_heal</c>, <c>fault_reset</c> and <c>fault_list_targets</c> do not.</summary>
    public static readonly IReadOnlySet<string> FaultTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "fault_toxic_add", "fault_proxy_disable", "fault_partition", "fault_kill", "fault_pause",
        "fault_disk_fill", "fault_corrupt_file", "fault_corrupt_registry", "fault_db",
    };

    private static readonly HashSet<string> TopKeys = new(StringComparer.Ordinal) { "schema", "id", "relation", "seed", "base", "variant", "pre", "compare" };
    private static readonly HashSet<string> WorldKeys = new(StringComparer.Ordinal) { "spec", "rows", "faults" };
    private static readonly HashSet<string> FaultKeys = new(StringComparer.Ordinal) { "tool", "args" };
    private static readonly HashSet<string> PreKeys = new(StringComparer.Ordinal) { "spec", "rows" };
    private static readonly HashSet<string> CompareKeys = new(StringComparer.Ordinal) { "pointer", "variant_pointers", "op" };

    /// <summary>
    /// Parses one experiment. A failure names the first problem; the message never echoes a value.
    /// </summary>
    /// <param name="text">The TOML text of the experiment file.</param>
    public static Result<TwinSpec> Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Result<TwinSpec>.Failure("[Lab.InvalidArgument] the experiment is not valid TOML");
        }

        Result unknown = UnknownKey(root, TopKeys);
        if (unknown.IsFailure)
        {
            return Result<TwinSpec>.Failure(unknown.Error);
        }

        if (!root.TryGetValue("schema", out object? schema) || schema is not long schemaValue || schemaValue != 1)
        {
            return Result<TwinSpec>.Failure("[Lab.InvalidArgument] schema must be 1");
        }

        if (!root.TryGetValue("id", out object? idRaw) || idRaw is not string id || !IdPattern.IsMatch(id))
        {
            return Result<TwinSpec>.Failure("[Lab.InvalidArgument] id must match ^[a-z0-9-]{1,64}$");
        }

        TwinRelation relation;
        if (root.TryGetValue("relation", out object? relRaw) && relRaw is "equal_outcome")
        {
            relation = TwinRelation.EqualOutcome;
        }
        else if (root.TryGetValue("relation", out relRaw) && relRaw is "isolated")
        {
            relation = TwinRelation.Isolated;
        }
        else
        {
            return Result<TwinSpec>.Failure("[Lab.InvalidArgument] relation must be equal_outcome or isolated");
        }

        ulong? seed = null;
        if (root.TryGetValue("seed", out object? seedRaw))
        {
            if (seedRaw is not long seedValue || seedValue < 0)
            {
                return Result<TwinSpec>.Failure("[Lab.InvalidArgument] seed must be a non-negative integer");
            }

            seed = (ulong)seedValue;
        }

        Result<TwinWorld> baseWorld = ReadWorld(root, "base", allowFaults: false);
        if (baseWorld.IsFailure)
        {
            return Result<TwinSpec>.Failure(baseWorld.Error);
        }

        Result<TwinWorld> variantWorld = ReadWorld(root, "variant", allowFaults: true);
        if (variantWorld.IsFailure)
        {
            return Result<TwinSpec>.Failure(variantWorld.Error);
        }

        TwinPre? pre = null;
        if (root.TryGetValue("pre", out object? preRaw))
        {
            if (preRaw is not TomlTable preTable)
            {
                return Result<TwinSpec>.Failure("[Lab.InvalidArgument] pre must be a table");
            }

            Result preUnknown = UnknownKey(preTable, PreKeys);
            if (preUnknown.IsFailure)
            {
                return Result<TwinSpec>.Failure(preUnknown.Error);
            }

            if (!preTable.TryGetValue("spec", out object? preSpec) || preSpec is not string preSpecText || preSpecText.Length == 0
                || !TryStrings(preTable, "rows", out IReadOnlyList<string>? preRows))
            {
                return Result<TwinSpec>.Failure("[Lab.InvalidArgument] pre needs spec and rows");
            }

            pre = new TwinPre(preSpecText, preRows!);
        }

        var compares = new List<TwinCompare>();
        if (root.TryGetValue("compare", out object? compareRaw))
        {
            if (!TryTables(compareRaw, out List<TomlTable> compareArray))
            {
                return Result<TwinSpec>.Failure("[Lab.InvalidArgument] compare must be an array of tables");
            }

            foreach (TomlTable table in compareArray)
            {
                Result<TwinCompare> compare = ReadCompare(table, relation);
                if (compare.IsFailure)
                {
                    return Result<TwinSpec>.Failure(compare.Error);
                }

                compares.Add(compare.Value!);
            }
        }

        if (compares.Count == 0)
        {
            return Result<TwinSpec>.Failure("[Lab.InvalidArgument] an experiment needs at least one compare");
        }

        return Result<TwinSpec>.Success(new TwinSpec(1, id, relation, seed, baseWorld.Value!, variantWorld.Value!, pre, compares));
    }

    /// <summary>
    /// Checks that every row named in <c>[base]</c> exists in the base spec (C19). Returns the first missing row as a failure.
    /// </summary>
    /// <param name="spec">The parsed experiment.</param>
    /// <param name="baseRowIds">The row ids of the base catalog spec.</param>
    public static Result CheckBaseRows(TwinSpec spec, IReadOnlyCollection<string> baseRowIds)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(baseRowIds);
        foreach (string row in spec.Base.Rows)
        {
            if (!baseRowIds.Contains(row, StringComparer.Ordinal))
            {
                return Result.Failure("[Lab.NotFound] the base row is not in the spec");
            }
        }

        return Result.Success();
    }

    private static Result<TwinWorld> ReadWorld(TomlTable root, string key, bool allowFaults)
    {
        if (!root.TryGetValue(key, out object? raw) || raw is not TomlTable table)
        {
            return Result<TwinWorld>.Failure("[Lab.InvalidArgument] " + key + " must be a table");
        }

        Result unknown = UnknownKey(table, WorldKeys);
        if (unknown.IsFailure)
        {
            return Result<TwinWorld>.Failure(unknown.Error);
        }

        if (!table.TryGetValue("spec", out object? specRaw) || specRaw is not string spec || spec.Length == 0
            || !TryStrings(table, "rows", out IReadOnlyList<string>? rows))
        {
            return Result<TwinWorld>.Failure("[Lab.InvalidArgument] " + key + " needs spec and rows");
        }

        var faults = new List<FaultSpec>();
        if (table.TryGetValue("faults", out object? faultsRaw))
        {
            if (!allowFaults)
            {
                return Result<TwinWorld>.Failure("[Lab.InvalidArgument] the base world is never faulted");
            }

            if (!TryTables(faultsRaw, out List<TomlTable> faultArray))
            {
                return Result<TwinWorld>.Failure("[Lab.InvalidArgument] faults must be an array of tables");
            }

            foreach (TomlTable faultTable in faultArray)
            {
                Result<FaultSpec> fault = ReadFault(faultTable);
                if (fault.IsFailure)
                {
                    return Result<TwinWorld>.Failure(fault.Error);
                }

                faults.Add(fault.Value!);
            }
        }

        return Result<TwinWorld>.Success(new TwinWorld(spec, rows!, faults));
    }

    private static Result<FaultSpec> ReadFault(TomlTable table)
    {
        Result unknown = UnknownKey(table, FaultKeys);
        if (unknown.IsFailure)
        {
            return Result<FaultSpec>.Failure(unknown.Error);
        }

        if (!table.TryGetValue("tool", out object? toolRaw) || toolRaw is not string tool || !FaultTools.Contains(tool))
        {
            return Result<FaultSpec>.Failure("[Lab.InvalidArgument] a twin fault must use a C4 fault tool");
        }

        if (!table.TryGetValue("args", out object? argsRaw) || argsRaw is not TomlTable argsTable)
        {
            return Result<FaultSpec>.Failure("[Lab.InvalidArgument] a twin fault needs args");
        }

        return Result<FaultSpec>.Success(new FaultSpec(tool, TomlJson.ToElement(argsTable)));
    }

    private static Result<TwinCompare> ReadCompare(TomlTable table, TwinRelation relation)
    {
        Result unknown = UnknownKey(table, CompareKeys);
        if (unknown.IsFailure)
        {
            return Result<TwinCompare>.Failure(unknown.Error);
        }

        bool hasPointer = table.TryGetValue("pointer", out object? pointerRaw);
        bool hasVariant = table.TryGetValue("variant_pointers", out object? variantRaw);
        if (hasPointer == hasVariant)
        {
            return Result<TwinCompare>.Failure("[Lab.InvalidArgument] a compare has either pointer or variant_pointers");
        }

        if (hasPointer)
        {
            if (pointerRaw is not string pointer || !IsValidPointer(pointer))
            {
                return Result<TwinCompare>.Failure("[Lab.InvalidArgument] a compare pointer is not an RFC 6901 pointer");
            }

            return Result<TwinCompare>.Success(new TwinCompare(pointer, null, null));
        }

        if (!table.TryGetValue("op", out object? opRaw) || opRaw is not "disjoint")
        {
            return Result<TwinCompare>.Failure("[Lab.InvalidArgument] variant_pointers needs op = \"disjoint\"");
        }

        if (relation != TwinRelation.Isolated)
        {
            return Result<TwinCompare>.Failure("[Lab.InvalidArgument] disjoint is allowed only under isolated");
        }

        if (variantRaw is not TomlArray array || array.Count != 2 || array[0] is not string first || array[1] is not string second
            || !IsValidPointer(first) || !IsValidPointer(second))
        {
            return Result<TwinCompare>.Failure("[Lab.InvalidArgument] disjoint needs two RFC 6901 pointers");
        }

        return Result<TwinCompare>.Success(new TwinCompare(null, [first, second], "disjoint"));
    }

    /// <summary>
    /// RFC 6901: empty, or starts with <c>/</c> and every <c>~</c> is followed by <c>0</c> or <c>1</c>.
    /// </summary>
    internal static bool IsValidPointer(string pointer)
    {
        if (pointer.Length == 0)
        {
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        for (int i = 0; i < pointer.Length; i++)
        {
            if (pointer[i] != '~')
            {
                continue;
            }

            if (i + 1 >= pointer.Length || pointer[i + 1] is not ('0' or '1'))
            {
                return false;
            }
        }

        return true;
    }

    private static Result UnknownKey(TomlTable table, HashSet<string> allowed)
    {
        foreach (string key in table.Keys)
        {
            if (!allowed.Contains(key))
            {
                // The key name is not echoed: it is author text, and the message goes to the operator.
                return Result.Failure("[Lab.InvalidArgument] the experiment has an unknown key");
            }
        }

        return Result.Success();
    }

    /// <summary>Accepts both TOML forms of an array of tables: <c>[[name]]</c> and an inline array of inline tables.</summary>
    private static bool TryTables(object raw, out List<TomlTable> tables)
    {
        tables = [];
        if (raw is TomlTableArray array)
        {
            tables.AddRange(array);
            return true;
        }

        if (raw is TomlArray items)
        {
            foreach (object? item in items)
            {
                if (item is not TomlTable table)
                {
                    return false;
                }

                tables.Add(table);
            }

            return true;
        }

        return false;
    }

    private static bool TryStrings(TomlTable table, string key, out IReadOnlyList<string>? values)
    {
        values = null;
        if (!table.TryGetValue(key, out object? raw) || raw is not TomlArray array || array.Count == 0)
        {
            return false;
        }

        var list = new List<string>(array.Count);
        foreach (object? item in array)
        {
            if (item is not string text || text.Length == 0)
            {
                return false;
            }

            list.Add(text);
        }

        values = list;
        return true;
    }
}
