using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Contracts.Tests.Agents;

public class AgentMessagesSerializationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly JsonSerializerOptions Options = AgentJson.Options;

    /// <summary>
    /// Serializes, deserializes and serializes again; the two JSON strings must match.
    /// Compared as JSON because records holding lists or dictionaries do not compare equal by value.
    /// </summary>
    private static string AssertRoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        var back = JsonSerializer.Deserialize<T>(json, Options);
        Assert.NotNull(back);
        Assert.Equal(json, JsonSerializer.Serialize(back, Options));
        return json;
    }

    private static StartServerPayload SampleStartServer() =>
        new(Id, "1.21.1", "https://example.test/server.jar", "abcdef", 2048, 25565, true);

    [Fact]
    public void AgentCommandEnvelope_RoundTrips_WithCamelCaseNames()
    {
        var envelope = new AgentCommandEnvelope(
            Id, "Ping", Id, Id, Id, Now, Now.AddSeconds(60), "{}", "c2ln", "kid", "corr-1");

        var json = AssertRoundTrip(envelope);

        Assert.Contains("\"commandId\"", json);
        Assert.Contains("\"payloadJson\"", json);
        Assert.Contains("\"signingKeyId\"", json);
    }

    [Fact]
    public void AgentCommandResult_RoundTrips_WithEnumAsNumber()
    {
        var result = new AgentCommandResult(
            Id, AgentCommandStatus.Failed, Now, Now.AddSeconds(1), null, "boom", "E1", null);

        var json = AssertRoundTrip(result);

        Assert.Contains("\"status\":1", json);
    }

    [Fact]
    public void AgentServerStatus_RoundTrips()
    {
        AssertRoundTrip(new AgentServerStatus(Id, AgentServerState.Crashed, 137, "killed", Now));
    }

    [Fact]
    public void AgentConsoleLine_RoundTrips()
    {
        AssertRoundTrip(new AgentConsoleLine(Now, true, "error line"));
    }

    [Fact]
    public void AgentConsoleBatch_RoundTrips()
    {
        AssertRoundTrip(new AgentConsoleBatch(Id, [new AgentConsoleLine(Now, false, "hello")]));
    }

    [Fact]
    public void AgentTelemetry_RoundTrips()
    {
        AssertRoundTrip(new AgentTelemetry(Now, new Dictionary<string, double> { ["cpu"] = 1.5 }));
    }

    [Fact]
    public void PingPayload_RoundTrips()
    {
        AssertRoundTrip(new PingPayload("abc"));
    }

    [Fact]
    public void PingResult_RoundTrips()
    {
        AssertRoundTrip(new PingResult("1.0.0", Now, "abc"));
    }

    [Fact]
    public void StartServerPayload_RoundTrips()
    {
        AssertRoundTrip(SampleStartServer());
    }

    [Fact]
    public void StartServerPayload_MissingRequiredProperty_Throws()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void PingPayload_EmptyObject_DeserializesWithNullNonce()
    {
        var payload = JsonSerializer.Deserialize<PingPayload>("{}", AgentJson.Options);

        Assert.NotNull(payload);
        Assert.Null(payload.Nonce);
    }

    [Fact]
    public void StartServerPayload_UnknownProperty_IsIgnored()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":1}}";

        var payload = JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options);

        Assert.Equal(SampleStartServer(), payload);
    }

    [Fact]
    public void StartServerPayload_UnknownNestedObject_IsIgnored()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":{{\"a\":[1,2]}}}}";

        var payload = JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options);

        Assert.Equal(SampleStartServer(), payload);
    }

    [Fact]
    public void AgentCommandEnvelope_UnknownProperty_IsIgnored()
    {
        var envelope = new AgentCommandEnvelope(
            Id, "Ping", Id, Id, Id, Now, Now.AddSeconds(60), "{}", "c2ln", "kid", "corr-1");
        var json = JsonSerializer.Serialize(envelope, Options);
        var withExtra = json[..^1] + ",\"extra\":1}";

        var back = JsonSerializer.Deserialize<AgentCommandEnvelope>(withExtra, Options);

        Assert.Equal(envelope, back);
    }

    [Fact]
    public void StartServerPayload_DuplicateProperty_Throws()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"port\":25565,\"eulaAccepted\":true}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void StartServerPayload_NullForNonNullable_Throws()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":null,\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void StartServerPayload_DepthOverLimit_Throws()
    {
        // The payload object is depth 1; the unknown member nests 9 more objects, so depth 10 exceeds MaxDepth 8.
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 9)) + "1" + new string('}', 9);
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":{nested}}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void StartServerPayload_UnknownMemberAtDepthLimit_IsIgnored()
    {
        // The payload object is depth 1; the unknown member adds 7 objects, so depth 8 equals MaxDepth and is accepted.
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 7)) + "1" + new string('}', 7);
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":{nested}}}";

        var payload = JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options);

        Assert.Equal(SampleStartServer(), payload);
    }

    [Fact]
    public void StartServerPayload_UnknownMemberOneOverDepthLimit_Throws()
    {
        // The payload object is depth 1; the unknown member adds 8 objects, so depth 9 exceeds MaxDepth 8.
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 8)) + "1" + new string('}', 8);
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":{nested}}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void AgentJson_Options_AreReadOnly()
    {
        Assert.True(AgentJson.Options.IsReadOnly);
        Assert.Equal(JsonUnmappedMemberHandling.Skip, AgentJson.Options.UnmappedMemberHandling);
        Assert.False(AgentJson.Options.AllowDuplicateProperties);
        Assert.Equal(8, AgentJson.Options.MaxDepth);
    }

    [Fact]
    public void StopServerPayload_RoundTrips()
    {
        AssertRoundTrip(new StopServerPayload(Id, 30));
    }

    [Fact]
    public void ServerStatusPayload_RoundTrips()
    {
        AssertRoundTrip(new ServerStatusPayload(Id));
    }

    [Fact]
    public void ServerStatusResult_RoundTrips()
    {
        AssertRoundTrip(new ServerStatusResult(Id, AgentServerState.Running, 42, 300));
    }

    [Fact]
    public void AgentCommandCompleted_RoundTrips()
    {
        AssertRoundTrip(new AgentCommandCompleted(
            Id, Id, Id, "Ping", null, AgentCommandStatus.Succeeded, "{}", null, null, "corr-1", Now));
    }

    [Fact]
    public void AgentServerStatusChanged_RoundTrips()
    {
        AssertRoundTrip(new AgentServerStatusChanged(Id, Id, Id, AgentServerState.Stopping, null, null, Now));
    }

    [Fact]
    public void ServerConsoleOutput_RoundTrips()
    {
        AssertRoundTrip(new ServerConsoleOutput(Id, Id, Id, [new AgentConsoleLine(Now, false, "line")]));
    }

    [Fact]
    public void DispatchAgentCommandRequest_RoundTrips()
    {
        AssertRoundTrip(new DispatchAgentCommandRequest("Ping", "{}", null, "corr-1"));
    }

    [Fact]
    public void DispatchAgentCommandResponse_RoundTrips()
    {
        AssertRoundTrip(new DispatchAgentCommandResponse(Id, Now, Now.AddSeconds(60)));
    }
}
