using System.Reflection;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Reports health status and builds heartbeat payloads.
/// </summary>
public sealed class HealthReporter : IHealthReporter
{
    private readonly AgentOptions _options;
    private readonly ISystemMetricsCollector _metricsCollector;
    private readonly IProcessManager? _processManager;
    private readonly ILogger<HealthReporter> _logger;

    private NodeStatus _status = NodeStatus.Starting;
    private string? _statusReason;
    private readonly List<string> _warnings = [];
    private readonly object _lock = new();

    private static readonly string AgentVersion = Assembly
        .GetExecutingAssembly()
        .GetName()
        .Version?
        .ToString() ?? "0.0.0";

    public NodeStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public HealthReporter(
        IOptions<AgentOptions> options,
        ISystemMetricsCollector metricsCollector,
        IProcessManager? processManager,
        ILogger<HealthReporter> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _processManager = processManager; // Can be null during startup
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void SetStatus(NodeStatus status, string? reason = null)
    {
        lock (_lock)
        {
            if (_status != status)
            {
                _logger.LogInformation(
                    "Node status changed from {OldStatus} to {NewStatus}. Reason: {Reason}",
                    _status,
                    status,
                    reason ?? "unspecified");
                _status = status;
                _statusReason = reason;
            }
        }
    }

    public void AddWarning(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);

        lock (_lock)
        {
            if (!_warnings.Contains(warning))
            {
                _warnings.Add(warning);
                _logger.LogWarning("Health warning added: {Warning}", warning);
            }
        }
    }

    public void ClearWarnings()
    {
        lock (_lock)
        {
            _warnings.Clear();
        }
    }

    public async Task<Result<HeartbeatPayload>> GetHeartbeatPayloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.NodeId.HasValue)
        {
            return Result<HeartbeatPayload>.Failure("Cannot create heartbeat: agent is not enrolled");
        }

        var metrics = await _metricsCollector.CollectAsync(cancellationToken);

        List<ProcessStatus> activeProcesses;
        if (_processManager is not null)
        {
            activeProcesses = _processManager
                .GetAllProcesses()
                .Where(p => p.State == ProcessState.Running || p.State == ProcessState.Starting)
                .Select(p => p.ToStatus())
                .ToList();
        }
        else
        {
            activeProcesses = [];
        }

        List<string> warnings;
        NodeStatus status;
        lock (_lock)
        {
            status = _status;
            warnings = [.. _warnings];
        }

        return Result<HeartbeatPayload>.Success(new HeartbeatPayload
        {
            NodeId = _options.NodeId.Value,
            AgentVersion = AgentVersion,
            Timestamp = DateTimeOffset.UtcNow,
            Status = status,
            Metrics = metrics,
            ActiveProcesses = activeProcesses,
            Warnings = warnings
        });
    }
}
