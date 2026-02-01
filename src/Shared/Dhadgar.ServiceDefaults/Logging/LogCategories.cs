using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Logging;

/// <summary>
/// Defines EventId conventions and ranges for structured logging across all Dhadgar services.
/// Each service domain is allocated a specific range to prevent collisions and enable
/// efficient log filtering by domain.
/// </summary>
/// <remarks>
/// <para>
/// EventId ranges are allocated as follows:
/// <list type="bullet">
///   <item>1000-1999: Server lifecycle events</item>
///   <item>2000-2999: Authentication and Identity events</item>
///   <item>3000-3999: Node management events</item>
///   <item>4000-4999: Task orchestration events</item>
///   <item>5000-5999: Security events (used by SecurityEventLogger)</item>
///   <item>6000-6999: File operations events</item>
///   <item>7000-7999: Mod management events</item>
///   <item>8000-8999: Billing events</item>
///   <item>9000-9999: Infrastructure and system events</item>
/// </list>
/// </para>
/// <para>
/// Usage example:
/// <code>
/// [LoggerMessage(
///     EventId = LogCategories.ServerEvents.Starting,
///     Level = LogLevel.Information,
///     Message = "Server {ServerId} starting for tenant {TenantId}")]
/// public partial void ServerStarting(Guid serverId, Guid tenantId);
/// </code>
/// </para>
/// </remarks>
public static class LogCategories
{
    /// <summary>
    /// Server lifecycle events (1000-1999).
    /// Used by the Servers service for game server management.
    /// </summary>
    public static class ServerEvents
    {
        /// <summary>Base offset for server events.</summary>
        public const int Base = 1000;

        /// <summary>Server is starting up.</summary>
        public const int Starting = Base + 1;

        /// <summary>Server started successfully.</summary>
        public const int Started = Base + 2;

        /// <summary>Server is stopping.</summary>
        public const int Stopping = Base + 3;

        /// <summary>Server stopped successfully.</summary>
        public const int Stopped = Base + 4;

        /// <summary>Server start failed.</summary>
        public const int StartFailed = Base + 5;

        /// <summary>Server stop failed.</summary>
        public const int StopFailed = Base + 6;

        /// <summary>Server crashed unexpectedly.</summary>
        public const int Crashed = Base + 7;

        /// <summary>Server restart initiated.</summary>
        public const int Restarting = Base + 8;

        /// <summary>Server configuration changed.</summary>
        public const int ConfigurationChanged = Base + 10;

        /// <summary>Server update started.</summary>
        public const int UpdateStarted = Base + 20;

        /// <summary>Server update completed.</summary>
        public const int UpdateCompleted = Base + 21;

        /// <summary>Server update failed.</summary>
        public const int UpdateFailed = Base + 22;

        /// <summary>Server backup started.</summary>
        public const int BackupStarted = Base + 30;

        /// <summary>Server backup completed.</summary>
        public const int BackupCompleted = Base + 31;

        /// <summary>Server backup failed.</summary>
        public const int BackupFailed = Base + 32;

        /// <summary>Server created.</summary>
        public const int Created = Base + 100;

        /// <summary>Server deleted.</summary>
        public const int Deleted = Base + 101;
    }

    /// <summary>
    /// Authentication and Identity events (2000-2999).
    /// Used by the Identity service for user authentication and authorization.
    /// </summary>
    public static class AuthEvents
    {
        /// <summary>Base offset for authentication events.</summary>
        public const int Base = 2000;

        /// <summary>User login attempt started.</summary>
        public const int LoginAttempted = Base + 1;

        /// <summary>User login succeeded.</summary>
        public const int LoginSucceeded = Base + 2;

        /// <summary>User login failed.</summary>
        public const int LoginFailed = Base + 3;

        /// <summary>User logged out.</summary>
        public const int LoggedOut = Base + 4;

        /// <summary>Token refresh requested.</summary>
        public const int TokenRefreshRequested = Base + 10;

        /// <summary>Token refresh succeeded.</summary>
        public const int TokenRefreshSucceeded = Base + 11;

        /// <summary>Token refresh failed.</summary>
        public const int TokenRefreshFailed = Base + 12;

        /// <summary>Token revoked.</summary>
        public const int TokenRevoked = Base + 13;

        /// <summary>OAuth provider linked.</summary>
        public const int OAuthLinked = Base + 20;

        /// <summary>OAuth provider unlinked.</summary>
        public const int OAuthUnlinked = Base + 21;

        /// <summary>Password changed.</summary>
        public const int PasswordChanged = Base + 30;

        /// <summary>Password reset requested.</summary>
        public const int PasswordResetRequested = Base + 31;

        /// <summary>Password reset completed.</summary>
        public const int PasswordResetCompleted = Base + 32;

        /// <summary>Email verification sent.</summary>
        public const int EmailVerificationSent = Base + 40;

        /// <summary>Email verified.</summary>
        public const int EmailVerified = Base + 41;

        /// <summary>User registered.</summary>
        public const int UserRegistered = Base + 100;

        /// <summary>User account disabled.</summary>
        public const int UserDisabled = Base + 101;

        /// <summary>User account enabled.</summary>
        public const int UserEnabled = Base + 102;
    }

    /// <summary>
    /// Node management events (3000-3999).
    /// Used by the Nodes service for agent and hardware node management.
    /// </summary>
    public static class NodeEvents
    {
        /// <summary>Base offset for node events.</summary>
        public const int Base = 3000;

        /// <summary>Node registered.</summary>
        public const int Registered = Base + 1;

        /// <summary>Node unregistered.</summary>
        public const int Unregistered = Base + 2;

        /// <summary>Node came online.</summary>
        public const int Online = Base + 3;

        /// <summary>Node went offline.</summary>
        public const int Offline = Base + 4;

        /// <summary>Node health check started.</summary>
        public const int HealthCheckStarted = Base + 10;

        /// <summary>Node health check passed.</summary>
        public const int HealthCheckPassed = Base + 11;

        /// <summary>Node health check failed.</summary>
        public const int HealthCheckFailed = Base + 12;

        /// <summary>Node capacity updated.</summary>
        public const int CapacityUpdated = Base + 20;

        /// <summary>Node maintenance started.</summary>
        public const int MaintenanceStarted = Base + 30;

        /// <summary>Node maintenance ended.</summary>
        public const int MaintenanceEnded = Base + 31;

        /// <summary>Node agent updated.</summary>
        public const int AgentUpdated = Base + 40;

        /// <summary>Node certificate rotated.</summary>
        public const int CertificateRotated = Base + 50;
    }

    /// <summary>
    /// Task orchestration events (4000-4999).
    /// Used by the Tasks service for background job management.
    /// </summary>
    public static class TaskEvents
    {
        /// <summary>Base offset for task events.</summary>
        public const int Base = 4000;

        /// <summary>Task created.</summary>
        public const int Created = Base + 1;

        /// <summary>Task started.</summary>
        public const int Started = Base + 2;

        /// <summary>Task completed.</summary>
        public const int Completed = Base + 3;

        /// <summary>Task failed.</summary>
        public const int Failed = Base + 4;

        /// <summary>Task cancelled.</summary>
        public const int Cancelled = Base + 5;

        /// <summary>Task progress updated.</summary>
        public const int ProgressUpdated = Base + 10;

        /// <summary>Task retry scheduled.</summary>
        public const int RetryScheduled = Base + 20;

        /// <summary>Task retry attempted.</summary>
        public const int RetryAttempted = Base + 21;

        /// <summary>Task timeout occurred.</summary>
        public const int TimedOut = Base + 30;

        /// <summary>Task dependency resolved.</summary>
        public const int DependencyResolved = Base + 40;

        /// <summary>Task dependency failed.</summary>
        public const int DependencyFailed = Base + 41;
    }

    /// <summary>
    /// Security events (5000-5999).
    /// NOTE: This range is already used by SecurityEventLogger.cs.
    /// See <see cref="Dhadgar.ServiceDefaults.Security.SecurityEventLogger"/> for existing event definitions.
    /// </summary>
    public static class SecurityEvents
    {
        /// <summary>Base offset for security events.</summary>
        public const int Base = 5000;

        // NOTE: Events 5001-5017 are defined in SecurityEventLogger.cs
        // Do not reuse those EventIds.

        /// <summary>Reserved start of SecurityEventLogger range.</summary>
        public const int ReservedStart = Base + 1;

        /// <summary>Reserved end of SecurityEventLogger range.</summary>
        public const int ReservedEnd = Base + 100;

        /// <summary>Security policy violation detected.</summary>
        public const int PolicyViolation = Base + 101;

        /// <summary>Security scan started.</summary>
        public const int ScanStarted = Base + 110;

        /// <summary>Security scan completed.</summary>
        public const int ScanCompleted = Base + 111;

        /// <summary>Security vulnerability found.</summary>
        public const int VulnerabilityFound = Base + 112;
    }

    /// <summary>
    /// File operations events (6000-6999).
    /// Used by the Files service for file management.
    /// </summary>
    public static class FileEvents
    {
        /// <summary>Base offset for file events.</summary>
        public const int Base = 6000;

        /// <summary>File upload started.</summary>
        public const int UploadStarted = Base + 1;

        /// <summary>File upload completed.</summary>
        public const int UploadCompleted = Base + 2;

        /// <summary>File upload failed.</summary>
        public const int UploadFailed = Base + 3;

        /// <summary>File download started.</summary>
        public const int DownloadStarted = Base + 10;

        /// <summary>File download completed.</summary>
        public const int DownloadCompleted = Base + 11;

        /// <summary>File download failed.</summary>
        public const int DownloadFailed = Base + 12;

        /// <summary>File deleted.</summary>
        public const int Deleted = Base + 20;

        /// <summary>File moved.</summary>
        public const int Moved = Base + 21;

        /// <summary>File copied.</summary>
        public const int Copied = Base + 22;

        /// <summary>File checksum verified.</summary>
        public const int ChecksumVerified = Base + 30;

        /// <summary>File checksum mismatch.</summary>
        public const int ChecksumMismatch = Base + 31;

        /// <summary>File transfer progress updated.</summary>
        public const int TransferProgress = Base + 40;
    }

    /// <summary>
    /// Mod management events (7000-7999).
    /// Used by the Mods service for game modification management.
    /// </summary>
    public static class ModEvents
    {
        /// <summary>Base offset for mod events.</summary>
        public const int Base = 7000;

        /// <summary>Mod installed.</summary>
        public const int Installed = Base + 1;

        /// <summary>Mod uninstalled.</summary>
        public const int Uninstalled = Base + 2;

        /// <summary>Mod updated.</summary>
        public const int Updated = Base + 3;

        /// <summary>Mod enabled.</summary>
        public const int Enabled = Base + 10;

        /// <summary>Mod disabled.</summary>
        public const int Disabled = Base + 11;

        /// <summary>Mod compatibility check started.</summary>
        public const int CompatibilityCheckStarted = Base + 20;

        /// <summary>Mod compatibility check passed.</summary>
        public const int CompatibilityCheckPassed = Base + 21;

        /// <summary>Mod compatibility check failed.</summary>
        public const int CompatibilityCheckFailed = Base + 22;

        /// <summary>Mod download started.</summary>
        public const int DownloadStarted = Base + 30;

        /// <summary>Mod download completed.</summary>
        public const int DownloadCompleted = Base + 31;

        /// <summary>Mod download failed.</summary>
        public const int DownloadFailed = Base + 32;
    }

    /// <summary>
    /// Billing events (8000-8999).
    /// Used by the Billing service for subscription and payment management.
    /// </summary>
    public static class BillingEvents
    {
        /// <summary>Base offset for billing events.</summary>
        public const int Base = 8000;

        /// <summary>Subscription created.</summary>
        public const int SubscriptionCreated = Base + 1;

        /// <summary>Subscription updated.</summary>
        public const int SubscriptionUpdated = Base + 2;

        /// <summary>Subscription cancelled.</summary>
        public const int SubscriptionCancelled = Base + 3;

        /// <summary>Subscription renewed.</summary>
        public const int SubscriptionRenewed = Base + 4;

        /// <summary>Payment received.</summary>
        public const int PaymentReceived = Base + 10;

        /// <summary>Payment failed.</summary>
        public const int PaymentFailed = Base + 11;

        /// <summary>Payment refunded.</summary>
        public const int PaymentRefunded = Base + 12;

        /// <summary>Invoice generated.</summary>
        public const int InvoiceGenerated = Base + 20;

        /// <summary>Invoice sent.</summary>
        public const int InvoiceSent = Base + 21;

        /// <summary>Usage recorded.</summary>
        public const int UsageRecorded = Base + 30;

        /// <summary>Usage limit approached.</summary>
        public const int UsageLimitApproached = Base + 31;

        /// <summary>Usage limit exceeded.</summary>
        public const int UsageLimitExceeded = Base + 32;
    }

    /// <summary>
    /// Infrastructure and system events (9000-9999).
    /// Used for cross-cutting infrastructure concerns.
    /// </summary>
    public static class InfraEvents
    {
        /// <summary>Base offset for infrastructure events.</summary>
        public const int Base = 9000;

        /// <summary>Service starting.</summary>
        public const int ServiceStarting = Base + 1;

        /// <summary>Service started.</summary>
        public const int ServiceStarted = Base + 2;

        /// <summary>Service stopping.</summary>
        public const int ServiceStopping = Base + 3;

        /// <summary>Service stopped.</summary>
        public const int ServiceStopped = Base + 4;

        /// <summary>Configuration loaded.</summary>
        public const int ConfigurationLoaded = Base + 10;

        /// <summary>Configuration changed.</summary>
        public const int ConfigurationChanged = Base + 11;

        /// <summary>Database connection established.</summary>
        public const int DatabaseConnected = Base + 20;

        /// <summary>Database connection lost.</summary>
        public const int DatabaseDisconnected = Base + 21;

        /// <summary>Database migration started.</summary>
        public const int MigrationStarted = Base + 22;

        /// <summary>Database migration completed.</summary>
        public const int MigrationCompleted = Base + 23;

        /// <summary>Cache connected.</summary>
        public const int CacheConnected = Base + 30;

        /// <summary>Cache disconnected.</summary>
        public const int CacheDisconnected = Base + 31;

        /// <summary>Message broker connected.</summary>
        public const int MessageBrokerConnected = Base + 40;

        /// <summary>Message broker disconnected.</summary>
        public const int MessageBrokerDisconnected = Base + 41;

        /// <summary>Health check performed.</summary>
        public const int HealthCheckPerformed = Base + 50;

        /// <summary>Metrics exported.</summary>
        public const int MetricsExported = Base + 60;

        /// <summary>Request received (for tracing).</summary>
        public const int RequestReceived = Base + 100;

        /// <summary>Request completed.</summary>
        public const int RequestCompleted = Base + 101;

        /// <summary>Request failed.</summary>
        public const int RequestFailed = Base + 102;

        /// <summary>Unhandled exception occurred.</summary>
        public const int UnhandledException = Base + 200;
    }
}
