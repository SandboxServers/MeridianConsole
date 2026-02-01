using System.Diagnostics;

namespace Dhadgar.Agent.Core.Telemetry;

/// <summary>
/// OpenTelemetry activity source for distributed tracing.
/// </summary>
public sealed class AgentActivitySource : IDisposable
{
    public const string SourceName = "Dhadgar.Agent";

    private readonly ActivitySource _activitySource;

    public AgentActivitySource()
    {
        _activitySource = new ActivitySource(SourceName, "1.0.0");
    }

    /// <summary>
    /// Start an activity for command execution.
    /// </summary>
    public Activity? StartCommandExecution(Guid commandId, string commandType, string? correlationId)
    {
        var activity = _activitySource.StartActivity("ExecuteCommand", ActivityKind.Consumer);
        if (activity is not null)
        {
            activity.SetTag("command.id", commandId.ToString());
            activity.SetTag("command.type", commandType);
            if (correlationId is not null)
            {
                activity.SetTag("correlation.id", correlationId);
            }
        }
        return activity;
    }

    /// <summary>
    /// Start an activity for heartbeat.
    /// </summary>
    public Activity? StartHeartbeat(Guid nodeId)
    {
        var activity = _activitySource.StartActivity("SendHeartbeat", ActivityKind.Client);
        activity?.SetTag("node.id", nodeId.ToString());
        return activity;
    }

    /// <summary>
    /// Start an activity for file transfer.
    /// </summary>
    public Activity? StartFileTransfer(Guid transferId, string direction, bool isP2P)
    {
        var activity = _activitySource.StartActivity("FileTransfer", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("transfer.id", transferId.ToString());
            activity.SetTag("transfer.direction", direction);
            activity.SetTag("transfer.p2p", isP2P);
        }
        return activity;
    }

    /// <summary>
    /// Start an activity for process operation.
    /// </summary>
    public Activity? StartProcessOperation(Guid processId, string operation)
    {
        var activity = _activitySource.StartActivity($"Process.{operation}", ActivityKind.Internal);
        activity?.SetTag("process.id", processId.ToString());
        return activity;
    }

    /// <summary>
    /// Start an activity for connection state change.
    /// </summary>
    public Activity? StartConnectionStateChange(string previousState, string newState)
    {
        var activity = _activitySource.StartActivity("ConnectionStateChange", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("connection.previous_state", previousState);
            activity.SetTag("connection.new_state", newState);
        }
        return activity;
    }

    public void Dispose()
    {
        _activitySource.Dispose();
    }
}
