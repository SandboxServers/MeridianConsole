using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhadgar.Contracts.Agents;
using Xunit;

namespace Dhadgar.Contracts.Tests.Agents;

public class AgentMessageLimitsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>A control character is one UTF-8 byte and escapes to six bytes (\u0001) in the hub JSON.</summary>
    private const char ControlChar = '\u0001';

    /// <summary>Serializer options matching the SignalR JSON hub protocol default (JsonSerializerDefaults.Web).</summary>
    private static readonly JsonSerializerOptions HubFrameOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CommandLifetimeAndClockSkew_ArePinned()
    {
        Assert.Equal(60, AgentMessageLimits.MaxCommandLifetimeSeconds);
        Assert.Equal(30, AgentMessageLimits.MaxClockSkewSeconds);
    }

    [Fact]
    public void HubContentLimits_ArePinned()
    {
        Assert.Equal(64, AgentMessageLimits.MaxNonceLength);
        Assert.Equal(64, AgentMessageLimits.MaxAgentVersionLength);
        Assert.Equal(256, AgentMessageLimits.MaxWarningLength);
        Assert.Equal(128, AgentMessageLimits.MaxMetricNameLength);
        Assert.Equal(64, AgentMessageLimits.MaxServersPerHeartbeat);
        Assert.Equal(512 * 1024, AgentProtocol.MaxHubMessageBytes);
    }

    [Fact]
    public void StartServerAndStopServerRanges_ArePinned()
    {
        Assert.Equal(32, AgentMessageLimits.MaxMinecraftVersionLength);
        Assert.Equal("^[0-9A-Za-z._-]{1,32}$", AgentMessageLimits.MinecraftVersionPattern);
        Assert.Equal(2048, AgentMessageLimits.MaxServerJarUrlLength);
        Assert.Equal(40, AgentMessageLimits.ServerJarSha1HexLength);
        Assert.Equal(512, AgentMessageLimits.MinServerMemoryMb);
        Assert.Equal(32 * 1024, AgentMessageLimits.MaxServerMemoryMb);
        Assert.Equal(1024, AgentMessageLimits.MinServerPort);
        Assert.Equal(65535, AgentMessageLimits.MaxServerPort);
        Assert.Equal(1, AgentMessageLimits.MinGracefulTimeoutSeconds);
        Assert.Equal(300, AgentMessageLimits.MaxGracefulTimeoutSeconds);
    }

    [Fact]
    public void WorstCaseEnvelope_FrameFitsMaxHubMessageBytes()
    {
        var envelope = new AgentCommandEnvelope(
            Id, AgentCommandTypes.StartServer, Id, Id, Id, Now, Now.AddSeconds(60),
            new string(ControlChar, AgentMessageLimits.MaxPayloadJsonBytes),
            new string('A', 86), "kid", new string('c', AgentMessageLimits.MaxCorrelationIdLength));

        AssertFrameFits("ReceiveCommand", envelope);
    }

    [Fact]
    public void WorstCaseCommandResult_FrameFitsMaxHubMessageBytes()
    {
        var result = new AgentCommandResult(
            Id, Id, AgentCommandStatus.Failed, Now, Now.AddSeconds(1),
            new string(ControlChar, AgentMessageLimits.MaxResultJsonBytes),
            new string(ControlChar, AgentMessageLimits.MaxErrorMessageLength),
            "E1", null);

        AssertFrameFits("CommandResult", result);
    }

    [Fact]
    public void WorstCaseHeartbeat_FrameFitsMaxHubMessageBytes()
    {
        var servers = Enumerable.Range(0, AgentMessageLimits.MaxServersPerHeartbeat)
            .Select(_ => new AgentServerProcess(Id, AgentServerState.Running, int.MaxValue, 100.0, long.MaxValue, long.MaxValue))
            .ToList();
        var warnings = Enumerable.Repeat(new string(ControlChar, AgentMessageLimits.MaxWarningLength), AgentMessageLimits.MaxHeartbeatWarnings)
            .ToList();
        var heartbeat = new AgentHeartbeat(
            Id, new string(ControlChar, AgentMessageLimits.MaxAgentVersionLength), Now, 100.0, 100.0, 100.0, servers, warnings);

        AssertFrameFits("Heartbeat", heartbeat);
    }

    [Fact]
    public void WorstCaseTelemetry_FrameFitsMaxHubMessageBytes()
    {
        var metrics = new Dictionary<string, double>();
        for (var i = 0; i < AgentMessageLimits.MaxTelemetryMetrics; i++)
        {
            var name = new string(ControlChar, AgentMessageLimits.MaxMetricNameLength - 3) + i.ToString("D3", CultureInfo.InvariantCulture);
            metrics[name] = double.MaxValue;
        }

        AssertFrameFits("Telemetry", new AgentTelemetry(Id, Now, metrics));
    }

    [Fact]
    public void WorstCaseConsoleBatch_FrameFitsMaxHubMessageBytes()
    {
        var lineCount = AgentMessageLimits.MaxConsoleBatchChars / AgentMessageLimits.MaxConsoleLineLength;
        var lines = Enumerable.Range(0, lineCount)
            .Select(_ => new AgentConsoleLine(Now, true, new string(ControlChar, AgentMessageLimits.MaxConsoleLineLength)))
            .ToList();

        AssertFrameFits("ConsoleOutput", new AgentConsoleBatch(Id, Id, lines));
    }

    [Fact]
    public void WorstCaseServerStatus_FrameFitsMaxHubMessageBytes()
    {
        AssertFrameFits("ServerStatusChanged", new AgentServerStatus(
            Id, Id, AgentServerState.Crashed, 137, new string(ControlChar, AgentMessageLimits.MaxErrorMessageLength), Now));
    }

    [Fact]
    public void WorstCaseReceivePing_FrameFitsMaxHubMessageBytes()
    {
        AssertFrameFits("ReceivePing", new string(ControlChar, AgentMessageLimits.MaxNonceLength));
    }

    /// <summary>
    /// Builds the hub frame (JSON invocation message plus the 0x1E record separator) and asserts it fits
    /// <see cref="AgentProtocol.MaxHubMessageBytes"/>. Measures the escaped size, which is what the wire carries.
    /// </summary>
    private static void AssertFrameFits<T>(string target, T argument)
    {
        var frame = new Dictionary<string, object?>
        {
            ["type"] = 1,
            ["target"] = target,
            ["arguments"] = new object?[] { argument },
        };
        var json = JsonSerializer.Serialize(frame, HubFrameOptions);
        var frameBytes = Encoding.UTF8.GetByteCount(json) + 1;

        Assert.True(
            frameBytes <= AgentProtocol.MaxHubMessageBytes,
            $"{target} frame is {frameBytes} bytes, above MaxHubMessageBytes {AgentProtocol.MaxHubMessageBytes}.");
    }
}
