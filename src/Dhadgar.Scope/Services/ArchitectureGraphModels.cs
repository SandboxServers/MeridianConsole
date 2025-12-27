using System.Text.Json.Serialization;

namespace Dhadgar.Scope.Services;

public sealed class ArchitectureGraphData
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    [JsonPropertyName("districts")]
    public List<ArchitectureDistrict> Districts { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<ArchitectureNode> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<ArchitectureEdge> Edges { get; set; } = new();

    [JsonPropertyName("tours")]
    public List<ArchitectureTour> Tours { get; set; } = new();
}

public sealed class ArchitectureDistrict
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("center")]
    public ArchitecturePoint Center { get; set; } = new();
}

public sealed class ArchitecturePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ArchitectureNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "service"; // service|agent|db|foundation|external|client

    [JsonPropertyName("emoji")]
    public string Emoji { get; set; } = "⬚";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public ArchitecturePoint Position { get; set; } = new();

    [JsonPropertyName("ports")]
    public List<string> Ports { get; set; } = new();

    [JsonPropertyName("endpoints")]
    public List<string> Endpoints { get; set; } = new();

    [JsonPropertyName("responsibilities")]
    public List<string> Responsibilities { get; set; } = new();
}

public sealed class ArchitectureEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "http"; // http|ws|amqp|db|dns|other

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

public sealed class ArchitectureTour
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<ArchitectureTourStep> Steps { get; set; } = new();
}

public sealed class ArchitectureTourStep
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("focusNodes")]
    public List<string> FocusNodes { get; set; } = new();

    [JsonPropertyName("focusEdges")]
    public List<string> FocusEdges { get; set; } = new();
}
