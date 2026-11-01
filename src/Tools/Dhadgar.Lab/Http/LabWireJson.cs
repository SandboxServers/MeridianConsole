using System.Text.Json;

namespace Dhadgar.Lab.Http;

/// <summary>
/// The JSON shape product endpoints bind (minimal API Web defaults, camelCase). Only for product request and
/// response bodies; tool arguments and results use <c>LabJson.Options</c> (D-ML117).
/// </summary>
public static class LabWireJson
{
    /// <summary>The product wire options: <see cref="JsonSerializerDefaults.Web"/>.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
