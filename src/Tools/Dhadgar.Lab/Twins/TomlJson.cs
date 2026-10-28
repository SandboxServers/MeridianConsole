using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn.Model;

namespace Dhadgar.Lab.Twins;

/// <summary>
/// Converts the TOML values of an experiment file (a fault's <c>args</c>) to JSON, the form every C4 tool takes.
/// </summary>
internal static class TomlJson
{
    /// <summary>Converts one TOML table to a JSON object element.</summary>
    /// <param name="table">The table to convert.</param>
    public static JsonElement ToElement(TomlTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return JsonSerializer.SerializeToElement(ToNode(table));
    }

    private static JsonNode? ToNode(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return JsonValue.Create(text);
            case long number:
                return JsonValue.Create(number);
            case double real:
                return JsonValue.Create(real);
            case bool flag:
                return JsonValue.Create(flag);
            case TomlTable table:
                var obj = new JsonObject();
                foreach (KeyValuePair<string, object> pair in table)
                {
                    obj[pair.Key] = ToNode(pair.Value);
                }

                return obj;
            case IEnumerable items when value is not string:
                var array = new JsonArray();
                foreach (object? item in items)
                {
                    array.Add(ToNode(item));
                }

                return array;
            default:
                return JsonValue.Create(value.ToString() ?? string.Empty);
        }
    }
}
