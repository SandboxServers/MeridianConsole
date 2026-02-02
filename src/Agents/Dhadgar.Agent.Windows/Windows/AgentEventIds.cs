namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows Event Log event IDs for the Meridian Console Agent.
/// </summary>
/// <remarks>
/// Event ID categories (thousands digit):
/// <list type="bullet">
/// <item><description>1xxx - Service lifecycle events</description></item>
/// <item><description>2xxx - Connection events</description></item>
/// <item><description>3xxx - Enrollment events</description></item>
/// <item><description>4xxx - Process management events</description></item>
/// <item><description>5xxx - Command execution events</description></item>
/// <item><description>9xxx - Security events</description></item>
/// </list>
/// </remarks>
public static class AgentEventIds
{
    #region Service Lifecycle (1xxx)

    /// <summary>
    /// The agent service has started successfully.
    /// </summary>
    public const int ServiceStarted = 1000;

    /// <summary>
    /// The agent service has stopped gracefully.
    /// </summary>
    public const int ServiceStopped = 1001;

    /// <summary>
    /// The agent service failed to start or encountered a fatal error.
    /// </summary>
    public const int ServiceFailed = 1002;

    /// <summary>
    /// The agent service is starting up.
    /// </summary>
    public const int ServiceStarting = 1003;

    /// <summary>
    /// The agent service is shutting down.
    /// </summary>
    public const int ServiceStopping = 1004;

    /// <summary>
    /// The agent service configuration has been reloaded.
    /// </summary>
    public const int ServiceConfigReloaded = 1005;

    #endregion

    #region Connection (2xxx)

    /// <summary>
    /// Successfully connected to the control plane.
    /// </summary>
    public const int Connected = 2000;

    /// <summary>
    /// Disconnected from the control plane.
    /// </summary>
    public const int Disconnected = 2001;

    /// <summary>
    /// Attempting to reconnect to the control plane.
    /// </summary>
    public const int ReconnectAttempt = 2002;

    /// <summary>
    /// Failed to reconnect to the control plane after maximum retries.
    /// </summary>
    public const int ReconnectFailed = 2003;

    /// <summary>
    /// Heartbeat sent to the control plane.
    /// </summary>
    public const int HeartbeatSent = 2004;

    /// <summary>
    /// Heartbeat failed to send.
    /// </summary>
    public const int HeartbeatFailed = 2005;

    /// <summary>
    /// Connection timeout occurred.
    /// </summary>
    public const int ConnectionTimeout = 2006;

    #endregion

    #region Enrollment (3xxx)

    /// <summary>
    /// Agent enrollment process has started.
    /// </summary>
    public const int EnrollmentStarted = 3000;

    /// <summary>
    /// Agent enrollment completed successfully.
    /// </summary>
    public const int EnrollmentSucceeded = 3001;

    /// <summary>
    /// Agent enrollment failed.
    /// </summary>
    public const int EnrollmentFailed = 3002;

    /// <summary>
    /// Agent certificate has been renewed.
    /// </summary>
    public const int CertificateRenewed = 3003;

    /// <summary>
    /// Agent certificate renewal failed.
    /// </summary>
    public const int CertificateRenewalFailed = 3004;

    /// <summary>
    /// Agent certificate is expiring soon.
    /// </summary>
    public const int CertificateExpiringSoon = 3005;

    /// <summary>
    /// Agent certificate has expired.
    /// </summary>
    public const int CertificateExpired = 3006;

    #endregion

    #region Process Management (4xxx)

    /// <summary>
    /// A managed process has been started.
    /// </summary>
    public const int ProcessStarted = 4000;

    /// <summary>
    /// A managed process has stopped normally.
    /// </summary>
    public const int ProcessStopped = 4001;

    /// <summary>
    /// A managed process has crashed unexpectedly.
    /// </summary>
    public const int ProcessCrashed = 4002;

    /// <summary>
    /// A managed process has exceeded resource limits (CPU, memory, etc.).
    /// </summary>
    public const int ResourceExceeded = 4003;

    /// <summary>
    /// A managed process is being forcefully terminated.
    /// </summary>
    public const int ProcessKilled = 4004;

    /// <summary>
    /// A managed process is being restarted.
    /// </summary>
    public const int ProcessRestarting = 4005;

    /// <summary>
    /// Failed to start a managed process.
    /// </summary>
    public const int ProcessStartFailed = 4006;

    /// <summary>
    /// A managed process output a warning or error.
    /// </summary>
    public const int ProcessOutputError = 4007;

    #endregion

    #region Command Execution (5xxx)

    /// <summary>
    /// A command has been received from the control plane.
    /// </summary>
    public const int CommandReceived = 5000;

    /// <summary>
    /// A command has been executed successfully.
    /// </summary>
    public const int CommandSucceeded = 5001;

    /// <summary>
    /// A command has failed to execute.
    /// </summary>
    public const int CommandFailed = 5002;

    /// <summary>
    /// A command has been rejected (invalid, unauthorized, or unsupported).
    /// </summary>
    public const int CommandRejected = 5003;

    /// <summary>
    /// A command has timed out during execution.
    /// </summary>
    public const int CommandTimeout = 5004;

    /// <summary>
    /// A command is being queued for execution.
    /// </summary>
    public const int CommandQueued = 5005;

    #endregion

    #region Security (9xxx)

    /// <summary>
    /// A security violation has been detected.
    /// </summary>
    public const int SecurityViolation = 9000;

    /// <summary>
    /// An unauthorized access attempt was detected.
    /// </summary>
    public const int UnauthorizedAccess = 9001;

    /// <summary>
    /// A path traversal attack attempt was detected.
    /// </summary>
    public const int PathTraversalAttempt = 9002;

    /// <summary>
    /// An invalid or untrusted certificate was presented.
    /// </summary>
    public const int InvalidCertificate = 9003;

    /// <summary>
    /// A command was rejected due to signature validation failure.
    /// </summary>
    public const int SignatureValidationFailed = 9004;

    /// <summary>
    /// A suspicious process execution pattern was detected.
    /// </summary>
    public const int SuspiciousActivity = 9005;

    /// <summary>
    /// Certificate trust chain validation failed.
    /// </summary>
    public const int CertificateTrustFailed = 9006;

    /// <summary>
    /// Rate limiting triggered due to excessive requests.
    /// </summary>
    public const int RateLimitExceeded = 9007;

    #endregion
}
