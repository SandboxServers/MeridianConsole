using System.Text.Json.Serialization;

namespace Dhadgar.Scope.Services;

public sealed class DependencyGraphData
{
    [JsonPropertyName("layers")]
    public List<string> Layers { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<DependencyNode> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<DependencyEdge> Edges { get; set; } = new();
}

public sealed class DependencyNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("layer")]
    public string Layer { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("responsibilities")]
    public List<string> Responsibilities { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("dependents")]
    public List<string> Dependents { get; set; } = new();

    [JsonPropertyName("endpoints")]
    public List<string> Endpoints { get; set; } = new();
}

public sealed class DependencyEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "depends_on";
}
