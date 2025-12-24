using System.Text.Json.Serialization;

namespace Dhadgar.Scope.Services;

public sealed class DbSchemaCatalog
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();

    [JsonPropertyName("services")]
    public List<DbServiceSchema> Services { get; set; } = new();
}

public sealed class DbServiceSchema
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<DbSchemaItem> Items { get; set; } = new();

    [JsonPropertyName("relationships")]
    public List<DbRelationship> Relationships { get; set; } = new();
}

public sealed class DbSchemaItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // e.g. identity.users

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "table"; // table|view|function|enum|type

    [JsonPropertyName("details")]
    public List<string> Details { get; set; } = new(); // columns, enum values, signature, notes
}

public sealed class DbRelationship
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty; // identity.users

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;   // identity.user_roles

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty; // FK label, etc.
}
