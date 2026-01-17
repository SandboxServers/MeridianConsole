namespace Dhadgar.Secrets.Audit;

/// <summary>
/// Audit logger for secrets operations.
/// All access (success and failure) is logged for security compliance.
/// </summary>
public interface ISecretsAuditLogger
{
    /// <summary>
    /// Log a secret access event.
    /// </summary>
    void LogAccess(SecretAuditEvent evt);

    /// <summary>
    /// Log an access denial event.
    /// </summary>
    void LogAccessDenied(SecretAccessDeniedEvent evt);

    /// <summary>
    /// Log a secret modification event.
    /// </summary>
    void LogModification(SecretModificationEvent evt);

    /// <summary>
    /// Log a secret rotation event.
    /// </summary>
    void LogRotation(SecretRotationEvent evt);

    /// <summary>
    /// Log a batch access event.
    /// </summary>
    void LogBatchAccess(SecretBatchAccessEvent evt);
}

public sealed record SecretAuditEvent(
    string SecretName,
    string Action,
    string? UserId,
    string? PrincipalType,
    bool Success,
    string CorrelationId,
    bool IsBreakGlass = false,
    bool IsServiceAccount = false);

public sealed record SecretAccessDeniedEvent(
    string SecretName,
    string Action,
    string? UserId,
    string Reason,
    string CorrelationId);

public sealed record SecretModificationEvent(
    string SecretName,
    string Action,
    string? UserId,
    string? PrincipalType,
    bool Success,
    string CorrelationId,
    string? ErrorMessage = null);

public sealed record SecretRotationEvent(
    string SecretName,
    string? UserId,
    string? PrincipalType,
    string? NewVersion,
    bool Success,
    string CorrelationId,
    string? ErrorMessage = null);

public sealed record SecretBatchAccessEvent(
    IReadOnlyList<string> RequestedSecrets,
    int AccessedCount,
    int DeniedCount,
    string? UserId,
    string CorrelationId);
