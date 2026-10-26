using System.Globalization;
using System.Text.Json;
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
            Id, Id, AgentCommandStatus.Failed, Now, Now.AddSeconds(1), null, "boom", "E1", null);

        var json = AssertRoundTrip(result);

        Assert.Contains("\"status\":1", json);
    }

    [Fact]
    public void AgentServerProcess_RoundTrips()
    {
        AssertRoundTrip(new AgentServerProcess(Id, AgentServerState.Running, 1234, 12.5, 1024, 60));
    }

    [Fact]
    public void AgentHeartbeat_RoundTrips()
    {
        var heartbeat = new AgentHeartbeat(
            Id, "1.0.0", Now, 10.0, 20.0, 30.0,
            [new AgentServerProcess(Id, AgentServerState.Stopped, null, 0, 0, 0)],
            ["disk low"]);

        AssertRoundTrip(heartbeat);
    }

    [Fact]
    public void AgentServerStatus_RoundTrips()
    {
        AssertRoundTrip(new AgentServerStatus(Id, Id, AgentServerState.Crashed, 137, "killed", Now));
    }

    [Fact]
    public void AgentConsoleLine_RoundTrips()
    {
        AssertRoundTrip(new AgentConsoleLine(Now, true, "error line"));
    }

    [Fact]
    public void AgentConsoleBatch_RoundTrips()
    {
        AssertRoundTrip(new AgentConsoleBatch(Id, Id, [new AgentConsoleLine(Now, false, "hello")]));
    }

    [Fact]
    public void AgentTelemetry_RoundTrips()
    {
        AssertRoundTrip(new AgentTelemetry(Id, Now, new Dictionary<string, double> { ["cpu"] = 1.5 }));
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
        AssertRoundTrip(new StartServerPayload(Id, "1.21.1", "https://example.test/server.jar", "abcdef", 2048, 25565, true));
    }

    [Fact]
    public void StartServerPayload_MissingRequiredProperty_Throws()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
    }

    [Fact]
    public void StartServerPayload_UnknownProperty_Throws()
    {
        var json = $"{{\"serverId\":\"{Id:D}\",\"minecraftVersion\":\"1.21.1\",\"serverJarUrl\":\"https://example.test/server.jar\",\"serverJarSha1\":\"abcdef\",\"memoryMb\":2048,\"port\":25565,\"eulaAccepted\":true,\"extra\":1}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartServerPayload>(json, AgentJson.Options));
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
