namespace Dhadgar.ServiceDefaults.Health;

/// <summary>
/// Flags indicating which external dependencies a service uses.
/// Used to register appropriate health checks for Kubernetes readiness probes.
/// </summary>
[Flags]
public enum HealthCheckDependencies
{
    /// <summary>No external dependencies (basic liveness only).</summary>
    None = 0,

    /// <summary>Service uses PostgreSQL database.</summary>
    Postgres = 1,

    /// <summary>Service uses Redis cache.</summary>
    Redis = 2,

    /// <summary>Service uses RabbitMQ messaging.</summary>
    RabbitMq = 4
}
