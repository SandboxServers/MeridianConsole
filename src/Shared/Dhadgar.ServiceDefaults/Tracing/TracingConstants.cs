namespace Dhadgar.ServiceDefaults.Tracing;

/// <summary>
/// Constants for distributed tracing configuration and span naming.
/// </summary>
/// <remarks>
/// <para>
/// These constants follow OpenTelemetry semantic conventions for database and cache systems.
/// See: https://opentelemetry.io/docs/concepts/semantic-conventions/
/// </para>
/// <para>
/// Custom span naming conventions for Dhadgar services:
/// </para>
/// <list type="bullet">
///   <item>Service activity sources: "{ActivitySourcePrefix}{ServiceName}" (e.g., "Dhadgar.Servers")</item>
///   <item>Database spans: Automatically named by EF Core instrumentation (e.g., "SELECT dhadgar.servers")</item>
///   <item>Cache spans: Automatically named by Redis instrumentation (e.g., "GET", "SET")</item>
///   <item>Custom spans: Use descriptive verb-noun format (e.g., "ValidateToken", "ProcessTask")</item>
/// </list>
/// </remarks>
public static class TracingConstants
{
    /// <summary>
    /// Prefix for all Dhadgar activity sources.
    /// </summary>
    /// <remarks>
    /// Custom ActivitySources should be named "{ActivitySourcePrefix}{Component}",
    /// e.g., "Dhadgar.Auth" or "Dhadgar.TaskScheduler".
    /// </remarks>
    public const string ActivitySourcePrefix = "Dhadgar.";

    /// <summary>
    /// Database system identifier for PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Follows OpenTelemetry semantic conventions for db.system attribute.
    /// See: https://opentelemetry.io/docs/concepts/semantic-conventions/database/
    /// </remarks>
    public const string DatabaseSystem = "postgresql";

    /// <summary>
    /// Cache system identifier for Redis.
    /// </summary>
    /// <remarks>
    /// Follows OpenTelemetry semantic conventions for db.system attribute.
    /// See: https://opentelemetry.io/docs/concepts/semantic-conventions/database/
    /// </remarks>
    public const string CacheSystem = "redis";

    /// <summary>
    /// Semantic attribute names following OpenTelemetry conventions.
    /// </summary>
    public static class Attributes
    {
        /// <summary>
        /// The database management system identifier (e.g., "postgresql", "redis").
        /// </summary>
        public const string DbSystem = "db.system";

        /// <summary>
        /// The database name being accessed.
        /// </summary>
        public const string DbName = "db.name";

        /// <summary>
        /// The database statement being executed.
        /// </summary>
        public const string DbStatement = "db.statement";

        /// <summary>
        /// Tenant identifier for multi-tenant operations.
        /// </summary>
        public const string TenantId = "tenant.id";

        /// <summary>
        /// Correlation ID for distributed tracing across services.
        /// </summary>
        public const string CorrelationId = "correlation.id";
    }
}
