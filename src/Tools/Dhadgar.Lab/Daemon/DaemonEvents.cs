using Dhadgar.Lab.Audit;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// The daemon's event sink: every event goes to the audit log, and a forced lease takeover is also logged at Warning.
/// The Warning carries no values; the audit entry holds the detail.
/// </summary>
public sealed class DaemonEvents : ILabEvents
{
    private const string LeaseEventName = "lab.lease";
    private const string ActionField = "action";
    private const string TakenOverAction = "taken_over";

    private readonly AuditLog _audit;
    private readonly ILogger<DaemonEvents> _logger;

    /// <summary>
    /// Creates the sink over the audit log.
    /// </summary>
    public DaemonEvents(AuditLog audit, ILogger<DaemonEvents> logger)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Emit(LabEvent labEvent)
    {
        ArgumentNullException.ThrowIfNull(labEvent);

        _audit.Emit(labEvent);

        if (labEvent.Name == LeaseEventName
            && labEvent.Fields.TryGetValue(ActionField, out string? action)
            && action == TakenOverAction)
        {
            _logger.LogWarning("lab.lease taken_over: a forced takeover ended the previous lease");
        }
    }
}
