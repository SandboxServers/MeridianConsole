using System.Diagnostics.Metrics;

namespace Dhadgar.Agent.Core.Telemetry;

/// <summary>
/// OpenTelemetry metrics for the agent.
/// </summary>
public sealed class AgentMeter : IDisposable
{
    public const string MeterName = "Dhadgar.Agent";

    private readonly Meter _meter;

    // Counters
    private readonly Counter<long> _commandsReceived;
    private readonly Counter<long> _commandsExecuted;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _heartbeatsSent;
    private readonly Counter<long> _reconnectAttempts;
    private readonly Counter<long> _filesTransferred;
    private readonly Counter<long> _bytesTransferred;

    // Gauges (observable)
    private readonly ObservableGauge<int> _activeProcessCount;
    private readonly ObservableGauge<double> _cpuUsage;
    private readonly ObservableGauge<long> _memoryUsage;

    private int _processCount;
    private double _cpuPercent;
    private long _memoryBytes;

    public AgentMeter()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Commands
        _commandsReceived = _meter.CreateCounter<long>(
            "agent.commands.received",
            description: "Number of commands received from control plane");

        _commandsExecuted = _meter.CreateCounter<long>(
            "agent.commands.executed",
            description: "Number of commands executed successfully");

        _commandsFailed = _meter.CreateCounter<long>(
            "agent.commands.failed",
            description: "Number of commands that failed");

        // Connection
        _heartbeatsSent = _meter.CreateCounter<long>(
            "agent.heartbeats.sent",
            description: "Number of heartbeats sent to control plane");

        _reconnectAttempts = _meter.CreateCounter<long>(
            "agent.connection.reconnect_attempts",
            description: "Number of reconnection attempts");

        // File transfers
        _filesTransferred = _meter.CreateCounter<long>(
            "agent.files.transferred",
            description: "Number of files transferred");

        _bytesTransferred = _meter.CreateCounter<long>(
            "agent.files.bytes_transferred",
            unit: "bytes",
            description: "Total bytes transferred");

        // Observable gauges
        _activeProcessCount = _meter.CreateObservableGauge(
            "agent.processes.active",
            () => _processCount,
            description: "Number of active game server processes");

        _cpuUsage = _meter.CreateObservableGauge(
            "agent.system.cpu_percent",
            () => _cpuPercent,
            unit: "percent",
            description: "Agent system CPU usage");

        _memoryUsage = _meter.CreateObservableGauge(
            "agent.system.memory_bytes",
            () => _memoryBytes,
            unit: "bytes",
            description: "Agent system memory usage");
    }

    public void RecordCommandReceived(string commandType)
    {
        _commandsReceived.Add(1, new KeyValuePair<string, object?>("command_type", commandType));
    }

    public void RecordCommandExecuted(string commandType, bool success)
    {
        if (success)
        {
            _commandsExecuted.Add(1, new KeyValuePair<string, object?>("command_type", commandType));
        }
        else
        {
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", commandType));
        }
    }

    public void RecordHeartbeatSent()
    {
        _heartbeatsSent.Add(1);
    }

    public void RecordReconnectAttempt()
    {
        _reconnectAttempts.Add(1);
    }

    public void RecordFileTransfer(long bytes, bool isUpload)
    {
        var direction = isUpload ? "upload" : "download";
        _filesTransferred.Add(1, new KeyValuePair<string, object?>("direction", direction));
        _bytesTransferred.Add(bytes, new KeyValuePair<string, object?>("direction", direction));
    }

    public void UpdateProcessCount(int count)
    {
        _processCount = count;
    }

    public void UpdateSystemMetrics(double cpuPercent, long memoryBytes)
    {
        _cpuPercent = cpuPercent;
        _memoryBytes = memoryBytes;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
