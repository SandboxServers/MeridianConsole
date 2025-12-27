using System.Text.Json.Serialization;

namespace Dhadgar.Scope.Services;

public sealed class CommMatrixData
{
    [JsonPropertyName("headers")]
    public List<string> Headers { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<CommMatrixRow> Rows { get; set; } = new();
}

public sealed class CommMatrixRow
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("cells")]
    public List<CommMatrixCell> Cells { get; set; } = new();
}

public sealed class CommMatrixCell
{
    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = "-"; // HTTP|WSS|AMQP|DNS|...
}
