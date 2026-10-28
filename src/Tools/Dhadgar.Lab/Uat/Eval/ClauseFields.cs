using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// Reads the typed source fields of a clause (C7 table). Failures name the field, so a bad spec reads as Unverified with a reason.
/// </summary>
internal static class ClauseFields
{
    /// <summary>A required non-empty string field.</summary>
    public static Result<string> Required(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
        {
            return Result<string>.Failure($"[Lab.InvalidArgument] {key} is required");
        }

        return Result<string>.Success(value.GetString()!);
    }

    /// <summary>An optional string field; absent is the empty string.</summary>
    public static Result<string> Optional(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out JsonElement value))
        {
            return Result<string>.Success(string.Empty);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return Result<string>.Failure($"[Lab.InvalidArgument] {key} must be a string");
        }

        return Result<string>.Success(value.GetString()!);
    }

    /// <summary>An optional JSON object field whose values are all strings; absent is an empty map.</summary>
    public static Result<Dictionary<string, string>> StringMap(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!fields.TryGetValue(key, out JsonElement value))
        {
            return Result<Dictionary<string, string>>.Success(map);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return Result<Dictionary<string, string>>.Failure($"[Lab.InvalidArgument] {key} must be a table of strings");
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return Result<Dictionary<string, string>>.Failure($"[Lab.InvalidArgument] {key}.{property.Name} must be a string");
            }

            map[property.Name] = property.Value.GetString()!;
        }

        return Result<Dictionary<string, string>>.Success(map);
    }
}
