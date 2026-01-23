namespace Dhadgar.Nodes;

/// <summary>
/// Configuration options for the Nodes service.
/// </summary>
public sealed class NodesOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Nodes";

    /// <summary>
    /// How long (in minutes) without a heartbeat before a node is considered offline.
    /// Default: 5 minutes.
    /// </summary>
    public int HeartbeatThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// How often (in minutes) to check for stale nodes.
    /// Default: 1 minute.
    /// </summary>
    public int StaleNodeCheckIntervalMinutes { get; set; } = 1;

    /// <summary>
    /// CPU usage percentage threshold for marking a node as degraded.
    /// Default: 90%.
    /// </summary>
    public double DegradedCpuThreshold { get; set; } = 90.0;

    /// <summary>
    /// Memory usage percentage threshold for marking a node as degraded.
    /// Default: 90%.
    /// </summary>
    public double DegradedMemoryThreshold { get; set; } = 90.0;

    /// <summary>
    /// Disk usage percentage threshold for marking a node as degraded.
    /// Default: 90%.
    /// </summary>
    public double DegradedDiskThreshold { get; set; } = 90.0;

    /// <summary>
    /// How long (in days) agent certificates are valid.
    /// Default: 90 days.
    /// </summary>
    public int CertificateValidityDays { get; set; } = 90;

    // ===== Certificate Authority Configuration =====

    /// <summary>
    /// CA storage provider type: "local" (development) or "azurekeyvault" (production).
    /// Default: "local".
    /// </summary>
    public string CaStorageType { get; set; } = "local";

    /// <summary>
    /// Path for local CA storage (only used when CaStorageType is "local").
    /// Default: {ApplicationData}/MeridianConsole/CA
    /// </summary>
    public string? CaStoragePath { get; set; }

    /// <summary>
    /// Password for encrypting the CA private key (local storage).
    /// If not set, a random password is generated and stored alongside the key.
    /// For production, always set this explicitly.
    /// </summary>
    public string? CaKeyPassword { get; set; }

    /// <summary>
    /// RSA key size for the CA certificate.
    /// Default: 4096 bits.
    /// </summary>
    public int CaKeySize { get; set; } = 4096;

    /// <summary>
    /// How long (in years) the CA certificate is valid.
    /// Default: 10 years.
    /// </summary>
    public int CaValidityYears { get; set; } = 10;

    /// <summary>
    /// RSA key size for client certificates.
    /// Default: 2048 bits.
    /// </summary>
    public int ClientKeySize { get; set; } = 2048;

    /// <summary>
    /// Azure Key Vault URL for storing CA certificate (production).
    /// Only used when CaStorageType is "azurekeyvault".
    /// </summary>
    public string? AzureKeyVaultUrl { get; set; }

    /// <summary>
    /// Name of the CA certificate in Azure Key Vault.
    /// Default: "meridian-agent-ca".
    /// </summary>
    public string AzureKeyVaultCaCertName { get; set; } = "meridian-agent-ca";

    /// <summary>
    /// Default validity period (in minutes) for enrollment tokens when not specified.
    /// Default: 60 minutes (1 hour).
    /// </summary>
    public int DefaultEnrollmentTokenExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum validity period (in minutes) allowed for enrollment tokens.
    /// Default: 10080 minutes (1 week).
    /// </summary>
    public int MaxEnrollmentTokenExpiryMinutes { get; set; } = 10080;

    // ===== Audit Log Configuration =====

    /// <summary>
    /// How long (in days) to retain audit logs before cleanup.
    /// Default: 90 days.
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// How often (in hours) to run the audit log cleanup process.
    /// Default: 24 hours (once per day).
    /// </summary>
    public int AuditLogCleanupIntervalHours { get; set; } = 24;

    /// <summary>
    /// Number of audit log records to delete per batch during cleanup.
    /// Default: 1000 records.
    /// </summary>
    public int AuditLogCleanupBatchSize { get; set; } = 1000;

    /// <summary>
    /// Health scoring algorithm configuration.
    /// </summary>
    public HealthScoringOptions HealthScoring { get; set; } = new();

    // ===== Capacity Reservation Configuration =====

    /// <summary>
    /// How often (in minutes) to check for and expire stale reservations.
    /// Default: 1 minute.
    /// </summary>
    public int ReservationExpiryCheckIntervalMinutes { get; set; } = 1;

    /// <summary>
    /// Default TTL (in minutes) for capacity reservations when not specified.
    /// Default: 15 minutes.
    /// </summary>
    public int DefaultReservationTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum TTL (in minutes) allowed for capacity reservations.
    /// Default: 1440 minutes (24 hours).
    /// </summary>
    public int MaxReservationTtlMinutes { get; set; } = 1440;
}

/// <summary>
/// Configuration options for the health scoring algorithm.
/// </summary>
public sealed class HealthScoringOptions
{
    /// <summary>
    /// Minimum health score to be considered healthy.
    /// Default: 80.
    /// </summary>
    public int HealthyThreshold { get; set; } = 80;

    /// <summary>
    /// Minimum health score to be considered degraded (below this is critical).
    /// Default: 50.
    /// </summary>
    public int DegradedThreshold { get; set; } = 50;

    /// <summary>
    /// Weight of CPU usage in the health score calculation.
    /// Default: 0.25 (25%).
    /// </summary>
    public double CpuWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight of memory usage in the health score calculation.
    /// Default: 0.30 (30%).
    /// </summary>
    public double MemoryWeight { get; set; } = 0.30;

    /// <summary>
    /// Weight of disk usage in the health score calculation.
    /// Default: 0.20 (20%).
    /// </summary>
    public double DiskWeight { get; set; } = 0.20;

    /// <summary>
    /// Weight of active health issues in the health score calculation.
    /// Default: 0.25 (25%).
    /// </summary>
    public double IssueWeight { get; set; } = 0.25;

    /// <summary>
    /// Points deducted from issue score per active health issue.
    /// Default: 20 (5 issues = 0 score from issues).
    /// </summary>
    public int IssueScorePenalty { get; set; } = 20;

    /// <summary>
    /// Minimum score change to consider the trend as changing (not stable).
    /// Default: 5.
    /// </summary>
    public int TrendThreshold { get; set; } = 5;
}
