using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Mcp;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;

namespace Dhadgar.Lab.Tests.Mcp;

/// <summary>
/// The MCP projection of a tool's schema: <c>lease_id</c> is advertised as a required envelope field for Leased tools (C3, D-ML66).
/// </summary>
public sealed class LabMcpSchemaTests
{
    [Fact]
    public void AdvertisedSchema_Leased_AddsRequiredLeaseId()
    {
        JsonElement schema = LabJson.SchemaFor<EchoLikeArgs>();

        JsonObject root = Advertised(schema, LabGate.Leased);

        Assert.Equal("string", root["properties"]!["lease_id"]!["type"]!.GetValue<string>());
        Assert.Single(Required(root), "lease_id");
    }

    [Fact]
    public void AdvertisedSchema_Leased_KeepsExistingRequiredAndAddsLeaseIdOnce()
    {
        JsonElement schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"],\"additionalProperties\":false}").RootElement;

        JsonObject root = Advertised(schema, LabGate.Leased);

        Assert.Equal(new[] { "value", "lease_id" }, Required(root));
        Assert.NotNull(root["properties"]!["value"]);
        Assert.NotNull(root["properties"]!["lease_id"]);
        Assert.False(root["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void AdvertisedSchema_OpenAndOwnLease_AreUnchanged()
    {
        JsonElement schema = LabJson.SchemaFor<EchoLikeArgs>();

        Assert.Equal(schema.GetRawText(), LabMcpServer.AdvertisedSchema(schema, LabGate.Open).GetRawText());
        Assert.Equal(schema.GetRawText(), LabMcpServer.AdvertisedSchema(schema, LabGate.OwnLease).GetRawText());
        Assert.Equal(schema.GetRawText(), LabMcpServer.AdvertisedSchema(schema, null).GetRawText());
    }

    [Fact]
    public void AdvertisedSchema_IsIdempotent()
    {
        JsonElement schema = LabJson.SchemaFor<EchoLikeArgs>();

        JsonElement once = LabMcpServer.AdvertisedSchema(schema, LabGate.Leased);
        JsonElement twice = LabMcpServer.AdvertisedSchema(once, LabGate.Leased);

        Assert.Equal(once.GetRawText(), twice.GetRawText());
        Assert.Single(Required(JsonNode.Parse(twice.GetRawText())!.AsObject()), "lease_id");
    }

    private static JsonObject Advertised(JsonElement schema, LabGate gate) =>
        JsonNode.Parse(LabMcpServer.AdvertisedSchema(schema, gate).GetRawText())!.AsObject();

    private static string[] Required(JsonObject root) =>
        root["required"] is JsonArray array
            ? array.Select(item => item!.GetValue<string>()).ToArray()
            : [];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EchoLikeArgs only supplies a schema; no test constructs it.")]
    private sealed record EchoLikeArgs(string? Value = null);
}
