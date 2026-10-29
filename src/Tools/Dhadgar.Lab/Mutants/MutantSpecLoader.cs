using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Mutants;

/// <summary>
/// Loads <c>mutant.toml</c> files (C19, schema 1) and checks each one against the rules of D-ML51: the id is the folder
/// name, the patch's <c>files</c> stay where the kind allows, every path goes through <see cref="IPathValidator"/>, and
/// the detector's <c>expect_pattern</c> is a bounded regex that never starts with <c>.*</c>.
/// </summary>
public static class MutantSpecLoader
{
    /// <summary>The only schema version this loader reads.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The file name every mutant folder carries.</summary>
    public const string SpecFileName = "mutant.toml";

    /// <summary>The file name of the patch next to the spec.</summary>
    public const string PatchFileName = "change.patch";

    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ShaPattern = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Loads every mutant folder under <paramref name="mutantsRoot"/>, in name order. A folder without a spec is an error.
    /// </summary>
    /// <param name="mutantsRoot">The <c>tools/lab/mutants</c> folder.</param>
    /// <param name="repoRoot">The repository root that <c>files</c> are relative to.</param>
    /// <param name="validator">The path validator every path goes through.</param>
    /// <param name="existsAtBase">Says whether a repo-relative file exists at the base. Null checks the repo root's working tree.</param>
    public static Result<IReadOnlyList<MutantSpec>> LoadAll(string mutantsRoot, string repoRoot, IPathValidator validator, Func<string, bool>? existsAtBase = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (!Directory.Exists(mutantsRoot))
        {
            return Result<IReadOnlyList<MutantSpec>>.Failure("[Lab.InvalidArgument] mutants folder does not exist");
        }

        var specs = new List<MutantSpec>();
        foreach (string folder in Directory.EnumerateDirectories(mutantsRoot).OrderBy(name => name, StringComparer.Ordinal))
        {
            string specPath = Path.Combine(folder, SpecFileName);
            if (!File.Exists(specPath))
            {
                return Result<IReadOnlyList<MutantSpec>>.Failure($"[Lab.InvalidArgument] {Path.GetFileName(folder)} has no {SpecFileName}");
            }

            Result<MutantSpec> spec = LoadText(Path.GetFileName(folder), File.ReadAllText(specPath), folder, repoRoot, validator, existsAtBase);
            if (spec.IsFailure)
            {
                return Result<IReadOnlyList<MutantSpec>>.Failure(spec.Error);
            }

            specs.Add(spec.Value!);
        }

        return Result<IReadOnlyList<MutantSpec>>.Success(specs);
    }

    /// <summary>
    /// Parses and checks one spec. <paramref name="folderName"/> must equal the spec's <c>id</c>.
    /// </summary>
    public static Result<MutantSpec> LoadText(string folderName, string text, string folder, string repoRoot, IPathValidator validator, Func<string, bool>? existsAtBase = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            return Failure(folderName, "invalid TOML: " + ex.Message);
        }

        if (!root.TryGetValue("schema", out object? schema) || schema is not long schemaValue || schemaValue != SchemaVersion)
        {
            return Failure(folderName, "schema must be 1");
        }

        string? id = ReadString(root, "id");
        if (id is null || !IdPattern.IsMatch(id) || !string.Equals(id, folderName, StringComparison.Ordinal))
        {
            return Failure(folderName, "id must match the folder name");
        }

        string? kindText = ReadString(root, "kind");
        MutantKind kind;
        if (kindText == "product")
        {
            kind = MutantKind.Product;
        }
        else if (kindText == "lab")
        {
            kind = MutantKind.Lab;
        }
        else
        {
            return Failure(id, "kind must be product or lab");
        }

        string? baseSha = ReadString(root, "base");
        if (baseSha is null || !ShaPattern.IsMatch(baseSha))
        {
            return Failure(id, "base must be a full lowercase commit sha");
        }

        string? title = ReadString(root, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return Failure(id, "title is required");
        }

        Result<IReadOnlyList<string>> files = ReadPaths(id, root, "files", kind, repoRoot, validator, existsAtBase ?? (rel => File.Exists(Path.Combine(repoRoot, rel))));
        if (files.IsFailure)
        {
            return Result<MutantSpec>.Failure(files.Error);
        }

        Result<IReadOnlyList<string>> services = ReadStrings(id, root, "services");
        if (services.IsFailure)
        {
            return Result<MutantSpec>.Failure(services.Error);
        }

        if (kind == MutantKind.Product && services.Value!.Count == 0)
        {
            return Failure(id, "a product mutant names at least one service");
        }

        Result<MutantDetector> detector = ReadDetector(id, root, kind);
        if (detector.IsFailure)
        {
            return Result<MutantSpec>.Failure(detector.Error);
        }

        return Result<MutantSpec>.Success(new MutantSpec(id, kind, baseSha, title!, files.Value!, services.Value!, detector.Value!, folder));
    }

    private static Result<MutantDetector> ReadDetector(string id, TomlTable root, MutantKind kind)
    {
        if (!root.TryGetValue("detector", out object? raw) || raw is not TomlTable table)
        {
            return Result<MutantDetector>.Failure($"[Lab.InvalidArgument] {id}: detector is required");
        }

        string? expect = ReadString(table, "expect_pattern");
        if (expect is not null && !IsBoundedPattern(expect))
        {
            return Result<MutantDetector>.Failure($"[Lab.InvalidArgument] {id}: expect_pattern must compile, must not start with .*, and is matched with a 1 s timeout");
        }

        string? tool = ReadString(table, "tool");
        string? test = ReadString(table, "test");
        if (kind == MutantKind.Product)
        {
            if (string.IsNullOrWhiteSpace(tool) || test is not null || expect is null)
            {
                return Result<MutantDetector>.Failure($"[Lab.InvalidArgument] {id}: a product detector names tool and expect_pattern, not test");
            }

            JsonObject? args = null;
            if (table.TryGetValue("args", out object? argsRaw))
            {
                if (argsRaw is not TomlTable argsTable || !TryToJsonObject(argsTable, out args))
                {
                    return Result<MutantDetector>.Failure($"[Lab.InvalidArgument] {id}: args must hold strings, integers, booleans or arrays of them");
                }
            }

            return Result<MutantDetector>.Success(new MutantDetector(tool, args, null, expect));
        }

        if (string.IsNullOrWhiteSpace(test) || tool is not null)
        {
            return Result<MutantDetector>.Failure($"[Lab.InvalidArgument] {id}: a lab detector names test, not tool");
        }

        return Result<MutantDetector>.Success(new MutantDetector(null, null, test, expect));
    }

    private static Result<IReadOnlyList<string>> ReadPaths(string id, TomlTable root, string key, MutantKind kind, string repoRoot, IPathValidator validator, Func<string, bool> exists)
    {
        Result<IReadOnlyList<string>> values = ReadStrings(id, root, key);
        if (values.IsFailure)
        {
            return values;
        }

        if (values.Value!.Count == 0)
        {
            return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: files must name at least one file");
        }

        foreach (string rel in values.Value)
        {
            if (Path.IsPathRooted(rel) || rel.Split('/', '\\').Contains(".."))
            {
                return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {rel} must be a relative path without '..'");
            }

            string normal = rel.Replace('\\', '/');
            bool inTools = normal.StartsWith("src/Tools/", StringComparison.Ordinal);
            bool allowed = kind == MutantKind.Product
                ? normal.StartsWith("src/", StringComparison.Ordinal) && !inTools
                : inTools;
            if (!allowed)
            {
                return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {rel} is outside what a {kind.ToString().ToLowerInvariant()} mutant may touch");
            }

            Result<string> checkedPath = validator.ValidatePath(Path.Combine(repoRoot, rel), [repoRoot]);
            if (checkedPath.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {rel} is not inside the repository");
            }

            if (!exists(rel))
            {
                return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {rel} does not exist at the base");
            }
        }

        return values;
    }

    private static Result<IReadOnlyList<string>> ReadStrings(string id, TomlTable root, string key)
    {
        if (!root.TryGetValue(key, out object? raw))
        {
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());
        }

        if (raw is not TomlArray array)
        {
            return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {key} must be an array of strings");
        }

        var values = new List<string>();
        foreach (object? item in array)
        {
            if (item is not string text || string.IsNullOrWhiteSpace(text))
            {
                return Result<IReadOnlyList<string>>.Failure($"[Lab.InvalidArgument] {id}: {key} must hold non-empty strings");
            }

            values.Add(text);
        }

        return Result<IReadOnlyList<string>>.Success(values);
    }

    private static bool TryToJsonObject(TomlTable table, out JsonObject? result)
    {
        var node = new JsonObject();
        foreach (KeyValuePair<string, object> pair in table)
        {
            JsonNode? value = pair.Value switch
            {
                string text => JsonValue.Create(text),
                long number => JsonValue.Create(number),
                bool flag => JsonValue.Create(flag),
                TomlArray array when array.All(item => item is string) => new JsonArray(array.Select(item => (JsonNode?)JsonValue.Create((string)item!)).ToArray()),
                _ => null,
            };
            if (value is null)
            {
                result = null;
                return false;
            }

            node[pair.Key] = value;
        }

        result = node;
        return true;
    }

    private static bool IsBoundedPattern(string pattern)
    {
        if (pattern.Length == 0 || pattern.StartsWith(".*", StringComparison.Ordinal) || pattern.Contains(".*", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? ReadString(TomlTable table, string key) =>
        table.TryGetValue(key, out object? raw) && raw is string text ? text : null;

    private static Result<MutantSpec> Failure(string id, string message) =>
        Result<MutantSpec>.Failure($"[Lab.InvalidArgument] {id}: {message}");
}
