using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Security;

/// <summary>
/// Provides structured logging for security-relevant events.
/// All events are logged with consistent fields for SIEM integration and alerting.
/// Shared across all services for consistent security observability.
/// </summary>
public interface ISecurityEventLogger
{
    /// <summary>Log successful authentication</summary>
    void LogAuthenticationSuccess(Guid userId, string? email, string? clientIp, string? userAgent, string? orgId = null);

    /// <summary>Log failed authentication attempt</summary>
    void LogAuthenticationFailure(string? email, string reason, string? clientIp, string? userAgent);

    /// <summary>Log privilege escalation attempt (blocked)</summary>
    void LogPrivilegeEscalationAttempt(Guid actorUserId, Guid targetUserId, string attemptedAction, string? orgId);

    /// <summary>Log role assignment</summary>
    void LogRoleAssignment(Guid actorUserId, Guid targetUserId, string role, Guid orgId);

    /// <summary>Log role revocation</summary>
    void LogRoleRevocation(Guid actorUserId, Guid targetUserId, string role, Guid orgId);

    /// <summary>Log custom role creation</summary>
    void LogCustomRoleCreated(Guid actorUserId, string roleName, IEnumerable<string> permissions, Guid orgId);

    /// <summary>Log OAuth account linking</summary>
    void LogOAuthAccountLinked(Guid userId, string provider, string? clientIp);

    /// <summary>Log OAuth account unlinking</summary>
    void LogOAuthAccountUnlinked(Guid userId, string provider, string? clientIp);

    /// <summary>Log token refresh</summary>
    void LogTokenRefresh(Guid userId, Guid orgId, string? clientIp);

    /// <summary>Log token revocation</summary>
    void LogTokenRevocation(Guid userId, string reason, string? clientIp);

    /// <summary>Log organization membership change</summary>
    void LogOrgMembershipChange(Guid userId, Guid orgId, string changeType, Guid? actorUserId);

    /// <summary>Log email verification status change</summary>
    void LogEmailVerificationChange(Guid userId, string? email, bool verified);

    /// <summary>Log suspicious activity</summary>
    void LogSuspiciousActivity(string activityType, string details, string? clientIp, Guid? userId = null);

    /// <summary>Log authorization denied</summary>
    void LogAuthorizationDenied(Guid? userId, string resource, string requiredPermission, string? clientIp);

    /// <summary>Log resource access (for audit trail)</summary>
    void LogResourceAccess(Guid userId, string resourceType, string resourceId, string action, Guid? orgId);

    /// <summary>Log API key usage</summary>
    void LogApiKeyUsage(string keyId, string endpoint, string? clientIp);

    /// <summary>Log rate limit exceeded</summary>
    void LogRateLimitExceeded(string? userId, string endpoint, string? clientIp);
}

public sealed partial class SecurityEventLogger : ISecurityEventLogger
{
    private readonly ILogger<SecurityEventLogger> _logger;

    public SecurityEventLogger(ILogger<SecurityEventLogger> logger)
    {
        _logger = logger;
    }

    public void LogAuthenticationSuccess(Guid userId, string? email, string? clientIp, string? userAgent, string? orgId = null)
    {
        AuthenticationSucceeded(userId, email ?? "unknown", clientIp ?? "unknown", userAgent ?? "unknown", orgId ?? "none");
    }

    public void LogAuthenticationFailure(string? email, string reason, string? clientIp, string? userAgent)
    {
        AuthenticationFailed(email ?? "unknown", reason, clientIp ?? "unknown", userAgent ?? "unknown");
    }

    public void LogPrivilegeEscalationAttempt(Guid actorUserId, Guid targetUserId, string attemptedAction, string? orgId)
    {
        PrivilegeEscalationAttempted(actorUserId, targetUserId, attemptedAction, orgId ?? "unknown");
    }

    public void LogRoleAssignment(Guid actorUserId, Guid targetUserId, string role, Guid orgId)
    {
        RoleAssigned(actorUserId, targetUserId, role, orgId);
    }

    public void LogRoleRevocation(Guid actorUserId, Guid targetUserId, string role, Guid orgId)
    {
        RoleRevoked(actorUserId, targetUserId, role, orgId);
    }

    public void LogCustomRoleCreated(Guid actorUserId, string roleName, IEnumerable<string> permissions, Guid orgId)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            CustomRoleCreated(actorUserId, roleName, string.Join(", ", permissions), orgId);
        }
    }

    public void LogOAuthAccountLinked(Guid userId, string provider, string? clientIp)
    {
        OAuthAccountLinked(userId, provider, clientIp ?? "unknown");
    }

    public void LogOAuthAccountUnlinked(Guid userId, string provider, string? clientIp)
    {
        OAuthAccountUnlinked(userId, provider, clientIp ?? "unknown");
    }

    public void LogTokenRefresh(Guid userId, Guid orgId, string? clientIp)
    {
        TokenRefreshed(userId, orgId, clientIp ?? "unknown");
    }

    public void LogTokenRevocation(Guid userId, string reason, string? clientIp)
    {
        TokenRevoked(userId, reason, clientIp ?? "unknown");
    }

    public void LogOrgMembershipChange(Guid userId, Guid orgId, string changeType, Guid? actorUserId)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            OrgMembershipChanged(userId, orgId, changeType, actorUserId?.ToString() ?? "system");
        }
    }

    public void LogEmailVerificationChange(Guid userId, string? email, bool verified)
    {
        EmailVerificationChanged(userId, email ?? "unknown", verified);
    }

    public void LogSuspiciousActivity(string activityType, string details, string? clientIp, Guid? userId = null)
    {
        SuspiciousActivityDetected(activityType, details, clientIp ?? "unknown", userId?.ToString() ?? "anonymous");
    }

    public void LogAuthorizationDenied(Guid? userId, string resource, string requiredPermission, string? clientIp)
    {
        AuthorizationDenied(userId?.ToString() ?? "anonymous", resource, requiredPermission, clientIp ?? "unknown");
    }

    public void LogResourceAccess(Guid userId, string resourceType, string resourceId, string action, Guid? orgId)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            ResourceAccessed(userId, resourceType, resourceId, action, orgId?.ToString() ?? "none");
        }
    }

    public void LogApiKeyUsage(string keyId, string endpoint, string? clientIp)
    {
        ApiKeyUsed(keyId, endpoint, clientIp ?? "unknown");
    }

    public void LogRateLimitExceeded(string? userId, string endpoint, string? clientIp)
    {
        RateLimitExceeded(userId ?? "anonymous", endpoint, clientIp ?? "unknown");
    }

    // Source-generated logging methods for optimal performance
    // All security events use EventId range 5000-5999

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Security: Authentication succeeded for user {UserId} ({Email}) from {ClientIp} using {UserAgent}, org={OrgId}")]
    private partial void AuthenticationSucceeded(Guid userId, string email, string clientIp, string userAgent, string orgId);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Warning,
        Message = "Security: Authentication failed for {Email}, reason={Reason}, from {ClientIp} using {UserAgent}")]
    private partial void AuthenticationFailed(string email, string reason, string clientIp, string userAgent);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Warning,
        Message = "Security: Privilege escalation attempt blocked. Actor={ActorUserId} tried {AttemptedAction} on user {TargetUserId} in org {OrgId}")]
    private partial void PrivilegeEscalationAttempted(Guid actorUserId, Guid targetUserId, string attemptedAction, string orgId);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Information,
        Message = "Security: Role assigned. Actor={ActorUserId} assigned role={Role} to user {TargetUserId} in org {OrgId}")]
    private partial void RoleAssigned(Guid actorUserId, Guid targetUserId, string role, Guid orgId);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Information,
        Message = "Security: Role revoked. Actor={ActorUserId} revoked role={Role} from user {TargetUserId} in org {OrgId}")]
    private partial void RoleRevoked(Guid actorUserId, Guid targetUserId, string role, Guid orgId);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Information,
        Message = "Security: Custom role created. Actor={ActorUserId} created role={RoleName} with permissions=[{Permissions}] in org {OrgId}")]
    private partial void CustomRoleCreated(Guid actorUserId, string roleName, string permissions, Guid orgId);

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Information,
        Message = "Security: OAuth account linked. User={UserId} linked provider={Provider} from {ClientIp}")]
    private partial void OAuthAccountLinked(Guid userId, string provider, string clientIp);

    [LoggerMessage(
        EventId = 5008,
        Level = LogLevel.Information,
        Message = "Security: OAuth account unlinked. User={UserId} unlinked provider={Provider} from {ClientIp}")]
    private partial void OAuthAccountUnlinked(Guid userId, string provider, string clientIp);

    [LoggerMessage(
        EventId = 5009,
        Level = LogLevel.Debug,
        Message = "Security: Token refreshed for user {UserId} in org {OrgId} from {ClientIp}")]
    private partial void TokenRefreshed(Guid userId, Guid orgId, string clientIp);

    [LoggerMessage(
        EventId = 5010,
        Level = LogLevel.Information,
        Message = "Security: Token revoked for user {UserId}, reason={Reason} from {ClientIp}")]
    private partial void TokenRevoked(Guid userId, string reason, string clientIp);

    [LoggerMessage(
        EventId = 5011,
        Level = LogLevel.Information,
        Message = "Security: Organization membership changed. User={UserId} in org {OrgId}, change={ChangeType}, actor={ActorUserId}")]
    private partial void OrgMembershipChanged(Guid userId, Guid orgId, string changeType, string actorUserId);

    [LoggerMessage(
        EventId = 5012,
        Level = LogLevel.Information,
        Message = "Security: Email verification changed. User={UserId} ({Email}) verified={Verified}")]
    private partial void EmailVerificationChanged(Guid userId, string email, bool verified);

    [LoggerMessage(
        EventId = 5013,
        Level = LogLevel.Warning,
        Message = "Security: Suspicious activity detected. Type={ActivityType}, details={Details}, from {ClientIp}, user={UserId}")]
    private partial void SuspiciousActivityDetected(string activityType, string details, string clientIp, string userId);

    [LoggerMessage(
        EventId = 5014,
        Level = LogLevel.Warning,
        Message = "Security: Authorization denied. User={UserId} denied access to {Resource}, required={RequiredPermission}, from {ClientIp}")]
    private partial void AuthorizationDenied(string userId, string resource, string requiredPermission, string clientIp);

    [LoggerMessage(
        EventId = 5015,
        Level = LogLevel.Debug,
        Message = "Security: Resource accessed. User={UserId} performed {Action} on {ResourceType}/{ResourceId} in org {OrgId}")]
    private partial void ResourceAccessed(Guid userId, string resourceType, string resourceId, string action, string orgId);

    [LoggerMessage(
        EventId = 5016,
        Level = LogLevel.Information,
        Message = "Security: API key used. KeyId={KeyId} accessed {Endpoint} from {ClientIp}")]
    private partial void ApiKeyUsed(string keyId, string endpoint, string clientIp);

    [LoggerMessage(
        EventId = 5017,
        Level = LogLevel.Warning,
        Message = "Security: Rate limit exceeded. User={UserId} exceeded limit on {Endpoint} from {ClientIp}")]
    private partial void RateLimitExceeded(string userId, string endpoint, string clientIp);
}
