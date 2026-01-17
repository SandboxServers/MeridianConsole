namespace Dhadgar.Secrets.Audit;

/// <summary>
/// Implementation of secrets audit logging.
/// Uses structured logging with consistent fields for SIEM integration.
/// Retention: 90 days (configured in log aggregation system).
/// </summary>
public sealed class SecretsAuditLogger : ISecretsAuditLogger
{
    private readonly ILogger<SecretsAuditLogger> _logger;

    public SecretsAuditLogger(ILogger<SecretsAuditLogger> logger)
    {
        _logger = logger;
    }

    public void LogAccess(SecretAuditEvent evt)
    {
        if (evt.IsBreakGlass)
        {
            // Break-glass is always logged at Warning level
            _logger.LogWarning(
                "AUDIT:SECRETS:BREAKGLASS Action={Action} Secret={SecretName} User={UserId} PrincipalType={PrincipalType} Success={Success} CorrelationId={CorrelationId}",
                evt.Action,
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.Success,
                evt.CorrelationId);
        }
        else
        {
            _logger.LogInformation(
                "AUDIT:SECRETS:ACCESS Action={Action} Secret={SecretName} User={UserId} PrincipalType={PrincipalType} Success={Success} IsServiceAccount={IsServiceAccount} CorrelationId={CorrelationId}",
                evt.Action,
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.Success,
                evt.IsServiceAccount,
                evt.CorrelationId);
        }
    }

    public void LogAccessDenied(SecretAccessDeniedEvent evt)
    {
        _logger.LogWarning(
            "AUDIT:SECRETS:DENIED Action={Action} Secret={SecretName} User={UserId} Reason={Reason} CorrelationId={CorrelationId}",
            evt.Action,
            evt.SecretName,
            evt.UserId,
            evt.Reason,
            evt.CorrelationId);
    }

    public void LogModification(SecretModificationEvent evt)
    {
        if (evt.Success)
        {
            _logger.LogInformation(
                "AUDIT:SECRETS:MODIFY Action={Action} Secret={SecretName} User={UserId} PrincipalType={PrincipalType} Success={Success} CorrelationId={CorrelationId}",
                evt.Action,
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.Success,
                evt.CorrelationId);
        }
        else
        {
            _logger.LogError(
                "AUDIT:SECRETS:MODIFY:FAILED Action={Action} Secret={SecretName} User={UserId} PrincipalType={PrincipalType} Error={ErrorMessage} CorrelationId={CorrelationId}",
                evt.Action,
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.ErrorMessage,
                evt.CorrelationId);
        }
    }

    public void LogRotation(SecretRotationEvent evt)
    {
        if (evt.Success)
        {
            // Rotation is a significant security event - always Warning level
            _logger.LogWarning(
                "AUDIT:SECRETS:ROTATED Secret={SecretName} User={UserId} PrincipalType={PrincipalType} NewVersion={NewVersion} CorrelationId={CorrelationId}",
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.NewVersion,
                evt.CorrelationId);
        }
        else
        {
            _logger.LogError(
                "AUDIT:SECRETS:ROTATION:FAILED Secret={SecretName} User={UserId} PrincipalType={PrincipalType} Error={ErrorMessage} CorrelationId={CorrelationId}",
                evt.SecretName,
                evt.UserId,
                evt.PrincipalType,
                evt.ErrorMessage,
                evt.CorrelationId);
        }
    }

    public void LogBatchAccess(SecretBatchAccessEvent evt)
    {
        _logger.LogInformation(
            "AUDIT:SECRETS:BATCH RequestedCount={RequestedCount} AccessedCount={AccessedCount} DeniedCount={DeniedCount} User={UserId} CorrelationId={CorrelationId}",
            evt.RequestedSecrets.Count,
            evt.AccessedCount,
            evt.DeniedCount,
            evt.UserId,
            evt.CorrelationId);
    }
}
