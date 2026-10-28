using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The variables of one row: the built-ins (<c>run_id</c>, <c>row_id</c>, <c>section_id</c>, <c>seed</c>, <c>member</c>) and the
/// values captured from earlier steps. <c>${name}</c> is substituted in strings; <c>${secret:&lt;ref&gt;}</c> is left as written
/// for <see cref="Http.LabHttp"/> to resolve inside the vault. An unknown name is a failure, never an empty string.
/// </summary>
public sealed class UatVars
{
    private const string UnknownVar = "[Lab.InvalidArgument] unknown var";
    private const string SecretPrefix = "secret:";

    private static readonly Regex Placeholder = new(@"\$\{([^}]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex VarName = new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The run id these variables belong to (the <c>uat:&lt;runId&gt;</c> caller of tool calls).</summary>
    public string RunId => _values["run_id"];

    /// <summary>
    /// Creates the built-in variables for one row. <paramref name="member"/> is defined only for sections with <c>requires_pool</c>.
    /// </summary>
    public UatVars(string runId, string rowId, string sectionId, ulong seed, string? member)
    {
        _values["run_id"] = runId ?? throw new ArgumentNullException(nameof(runId));
        _values["row_id"] = rowId ?? throw new ArgumentNullException(nameof(rowId));
        _values["section_id"] = sectionId ?? throw new ArgumentNullException(nameof(sectionId));
        _values["seed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (member is not null)
        {
            _values["member"] = member;
        }
    }

    /// <summary>
    /// Captures <paramref name="name"/> from a step result: the value at <paramref name="path"/>. A string is stored as its text,
    /// any other value as its JSON text. A missing pointer or a bad name is a failure.
    /// </summary>
    public Result Capture(string name, JsonElement stepResult, string path)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);

        if (!VarName.IsMatch(name))
        {
            return Result.Failure("[Lab.InvalidArgument] invalid var name");
        }

        Result<PointerLookup> found = JsonPointer.Resolve(stepResult, path);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (!found.Value.Found)
        {
            return Result.Failure("[Lab.InvalidArgument] capture pointer not found");
        }

        JsonElement value = found.Value.Value;
        _values[name] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        return Result.Success();
    }

    /// <summary>
    /// Substitutes every <c>${name}</c> in <paramref name="text"/>. <c>${secret:...}</c> is kept verbatim.
    /// </summary>
    public Result<string> Expand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.IndexOf("${", StringComparison.Ordinal) < 0)
        {
            return Result<string>.Success(text);
        }

        var builder = new StringBuilder(text.Length);
        int cursor = 0;
        foreach (Match match in Placeholder.Matches(text))
        {
            builder.Append(text, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;

            string name = match.Groups[1].Value;
            if (name.StartsWith(SecretPrefix, StringComparison.Ordinal))
            {
                builder.Append(match.Value);
            }
            else if (_values.TryGetValue(name, out string? value))
            {
                builder.Append(value);
            }
            else
            {
                return Result<string>.Failure(UnknownVar);
            }
        }

        builder.Append(text, cursor, text.Length - cursor);
        return Result<string>.Success(builder.ToString());
    }

    /// <summary>
    /// Substitutes inside every string of a JSON value (property names and numbers are left alone).
    /// </summary>
    public Result<JsonElement> Expand(JsonElement json)
    {
        Result<JsonNode?> node = ExpandNode(json);
        if (node.IsFailure)
        {
            return Result<JsonElement>.Failure(node.Error);
        }

        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement<JsonNode?>(node.Value));
    }

    /// <summary>
    /// Substitutes inside every value of a field map (a clause's source fields).
    /// </summary>
    public Result<Dictionary<string, JsonElement>> ExpandFields(IReadOnlyDictionary<string, JsonElement> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var expanded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonElement> field in fields)
        {
            Result<JsonElement> value = Expand(field.Value);
            if (value.IsFailure)
            {
                return Result<Dictionary<string, JsonElement>>.Failure(value.Error);
            }

            expanded[field.Key] = value.Value;
        }

        return Result<Dictionary<string, JsonElement>>.Success(expanded);
    }

    private Result<JsonNode?> ExpandNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Result<string> text = Expand(element.GetString()!);
                return text.IsFailure
                    ? Result<JsonNode?>.Failure(text.Error)
                    : Result<JsonNode?>.Success(JsonValue.Create(text.Value));
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Result<JsonNode?> child = ExpandNode(property.Value);
                    if (child.IsFailure)
                    {
                        return child;
                    }

                    obj[property.Name] = child.Value;
                }

                return Result<JsonNode?>.Success(obj);
            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Result<JsonNode?> child = ExpandNode(item);
                    if (child.IsFailure)
                    {
                        return child;
                    }

                    array.Add(child.Value);
                }

                return Result<JsonNode?>.Success(array);
            default:
                return Result<JsonNode?>.Success(JsonNode.Parse(element.GetRawText()));
        }
    }
}
