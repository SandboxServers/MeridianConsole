using System.Text;
using System.Text.Json;

using Dhadgar.Agent.Windows.IPC;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.IPC;

public sealed class PipeProtocolTests
{
    #region Serialization Tests

    [Fact]
    public void Serialize_OutputMessage_ProducesValidJson()
    {
        // Arrange
        var message = new OutputMessage
        {
            ServerId = "test-server",
            Data = "Server started successfully",
            IsError = false,
            CorrelationId = "corr-123"
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains("\"type\":\"output\"", json);
        Assert.Contains("\"serverId\":\"test-server\"", json);
        Assert.Contains("\"data\":\"Server started successfully\"", json);
        Assert.Contains("\"isError\":false", json);
    }

    [Fact]
    public void Serialize_StatusMessage_ProducesValidJson()
    {
        // Arrange
        var message = new StatusMessage
        {
            ServerId = "test-server",
            State = GameServerState.Running,
            OsPid = 12345,
            CpuPercent = 25.5,
            MemoryBytes = 1024 * 1024 * 512
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains("\"type\":\"status\"", json);
        Assert.Contains("\"state\":\"Running\"", json);
        Assert.Contains("\"osPid\":12345", json);
    }

    [Fact]
    public void Serialize_CommandMessage_ProducesValidJson()
    {
        // Arrange
        var message = new CommandMessage
        {
            ServerId = "test-server",
            Command = GameServerCommand.Stop,
            Payload = "graceful",
            TimeoutSeconds = 60
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains("\"type\":\"command\"", json);
        Assert.Contains("\"command\":\"Stop\"", json);
        Assert.Contains("\"payload\":\"graceful\"", json);
        Assert.Contains("\"timeoutSeconds\":60", json);
    }

    [Fact]
    public void Serialize_HeartbeatMessage_ProducesValidJson()
    {
        // Arrange
        var message = new HeartbeatMessage
        {
            ServerId = "test-server",
            Sequence = 42
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains("\"type\":\"heartbeat\"", json);
        Assert.Contains("\"sequence\":42", json);
    }

    [Fact]
    public void Serialize_ShutdownMessage_ProducesValidJson()
    {
        // Arrange
        var message = new ShutdownMessage
        {
            ServerId = "test-server",
            GracefulTimeoutSeconds = 45,
            Reason = "Manual shutdown requested"
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains("\"type\":\"shutdown\"", json);
        Assert.Contains("\"gracefulTimeoutSeconds\":45", json);
        Assert.Contains("\"reason\":\"Manual shutdown requested\"", json);
    }

    #endregion

    #region Deserialization Tests

    [Fact]
    public void Deserialize_OutputMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """
            {
                "type": "output",
                "serverId": "test-server",
                "data": "Hello World",
                "isError": true,
                "timestamp": "2024-01-15T10:30:00Z"
            }
            """;

        // Act
        var message = PipeProtocolSerializer.Deserialize(json);

        // Assert
        var output = Assert.IsType<OutputMessage>(message);
        Assert.Equal("test-server", output.ServerId);
        Assert.Equal("Hello World", output.Data);
        Assert.True(output.IsError);
    }

    [Fact]
    public void Deserialize_StatusMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """
            {
                "type": "status",
                "serverId": "test-server",
                "state": "Running",
                "osPid": 9999,
                "cpuPercent": 50.0,
                "memoryBytes": 268435456
            }
            """;

        // Act
        var message = PipeProtocolSerializer.Deserialize(json);

        // Assert
        var status = Assert.IsType<StatusMessage>(message);
        Assert.Equal("test-server", status.ServerId);
        Assert.Equal(GameServerState.Running, status.State);
        Assert.Equal(9999, status.OsPid);
        Assert.Equal(50.0, status.CpuPercent);
        Assert.Equal(268435456, status.MemoryBytes);
    }

    [Fact]
    public void Deserialize_CommandMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """
            {
                "type": "command",
                "serverId": "test-server",
                "command": "Start",
                "payload": null,
                "timeoutSeconds": 30
            }
            """;

        // Act
        var message = PipeProtocolSerializer.Deserialize(json);

        // Assert
        var command = Assert.IsType<CommandMessage>(message);
        Assert.Equal("test-server", command.ServerId);
        Assert.Equal(GameServerCommand.Start, command.Command);
        Assert.Null(command.Payload);
        Assert.Equal(30, command.TimeoutSeconds);
    }

    [Fact]
    public void Deserialize_AcknowledgeMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """
            {
                "type": "ack",
                "serverId": "test-server",
                "acknowledgedId": "cmd-456",
                "success": true,
                "errorMessage": null
            }
            """;

        // Act
        var message = PipeProtocolSerializer.Deserialize(json);

        // Assert
        var ack = Assert.IsType<AcknowledgeMessage>(message);
        Assert.Equal("test-server", ack.ServerId);
        Assert.Equal("cmd-456", ack.AcknowledgedId);
        Assert.True(ack.Success);
        Assert.Null(ack.ErrorMessage);
    }

    [Fact]
    public void Deserialize_ErrorMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """
            {
                "type": "error",
                "serverId": "test-server",
                "errorCode": "PROCESS_FAILED",
                "message": "Game server crashed",
                "isFatal": true
            }
            """;

        // Act
        var message = PipeProtocolSerializer.Deserialize(json);

        // Assert
        var error = Assert.IsType<ErrorMessage>(message);
        Assert.Equal("test-server", error.ServerId);
        Assert.Equal("PROCESS_FAILED", error.ErrorCode);
        Assert.Equal("Game server crashed", error.Message);
        Assert.True(error.IsFatal);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Deserialize_EmptyData_ReturnsNull()
    {
        // Act
        var result = PipeProtocolSerializer.Deserialize(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        // Act
        var result = PipeProtocolSerializer.Deserialize(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act
        var result = PipeProtocolSerializer.Deserialize(invalidJson);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_OversizedData_ReturnsNull()
    {
        // Arrange - Create data larger than MaxMessageSize
        var oversizedData = new byte[PipeMessage.MaxMessageSize + 1];

        // Act
        var result = PipeProtocolSerializer.Deserialize(oversizedData);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_UnknownType_ReturnsNull()
    {
        // Arrange
        var json = """
            {
                "type": "unknown_type",
                "serverId": "test-server"
            }
            """;

        // Act
        var result = PipeProtocolSerializer.Deserialize(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsCorrectly()
    {
        // Arrange
        var original = new StatusMessage
        {
            ServerId = "my-server",
            State = GameServerState.Running,
            OsPid = 54321,
            ExitCode = null,
            Message = "All systems operational",
            CpuPercent = 33.33,
            MemoryBytes = 1073741824,
            CorrelationId = "test-corr-id"
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(original);
        var deserialized = PipeProtocolSerializer.Deserialize(bytes);

        // Assert
        var roundTripped = Assert.IsType<StatusMessage>(deserialized);
        Assert.Equal(original.ServerId, roundTripped.ServerId);
        Assert.Equal(original.State, roundTripped.State);
        Assert.Equal(original.OsPid, roundTripped.OsPid);
        Assert.Equal(original.ExitCode, roundTripped.ExitCode);
        Assert.Equal(original.Message, roundTripped.Message);
        Assert.Equal(original.CpuPercent, roundTripped.CpuPercent);
        Assert.Equal(original.MemoryBytes, roundTripped.MemoryBytes);
        Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
    }

    [Theory]
    [InlineData(GameServerState.Initializing)]
    [InlineData(GameServerState.Starting)]
    [InlineData(GameServerState.Running)]
    [InlineData(GameServerState.Stopping)]
    [InlineData(GameServerState.Stopped)]
    [InlineData(GameServerState.Failed)]
    [InlineData(GameServerState.Restarting)]
    public void GameServerState_SerializesAsString(GameServerState state)
    {
        // Arrange
        var message = new StatusMessage
        {
            ServerId = "test",
            State = state
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains($"\"{state}\"", json);
    }

    [Theory]
    [InlineData(GameServerCommand.GetStatus)]
    [InlineData(GameServerCommand.Start)]
    [InlineData(GameServerCommand.Stop)]
    [InlineData(GameServerCommand.Kill)]
    [InlineData(GameServerCommand.Restart)]
    [InlineData(GameServerCommand.UpdateLimits)]
    public void GameServerCommand_SerializesAsString(GameServerCommand command)
    {
        // Arrange
        var message = new CommandMessage
        {
            ServerId = "test",
            Command = command
        };

        // Act
        var bytes = PipeProtocolSerializer.Serialize(message);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains($"\"{command}\"", json);
    }

    #endregion
}
