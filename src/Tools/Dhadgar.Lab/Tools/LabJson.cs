using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// The JSON rules for every tool argument and result: snake_case names and unknown members refused.
/// </summary>
public static class LabJson
{
    /// <summary>
    /// Serializer options shared by the tool base, the dispatcher and the MCP projection.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// The JSON Schema of <typeparamref name="T"/> as the MCP tool's input schema. The root is always an object:
    /// the exporter marks reference roots as nullable, and the MCP SDK refuses an input schema whose root is not <c>"object"</c>.
    /// </summary>
    public static JsonElement SchemaFor<T>()
    {
        JsonNode? schema = JsonSchemaExporter.GetJsonSchemaAsNode(Options, typeof(T));
        if (schema is JsonObject root && root.TryGetPropertyValue("type", out JsonNode? typeNode) && typeNode is JsonArray)
        {
            root["type"] = "object";
        }

        return JsonSerializer.SerializeToElement(schema, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        options.MakeReadOnly();
        return options;
    }
}
