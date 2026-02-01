namespace Dhadgar.ServiceDefaults;

/// <summary>
/// Options for configuring Dhadgar service middleware pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This class allows per-service configuration of the Dhadgar middleware pipeline.
/// Not all services need all middlewares - use these options to enable only what's needed.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// app.UseDhadgarMiddleware(new DhadgarServiceOptions
/// {
///     EnableTenantEnrichment = true,
///     EnableAuditMiddleware = false
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class DhadgarServiceOptions
{
    /// <summary>
    /// Enable tenant enrichment middleware for multi-tenant services.
    /// Adds TenantId, ServiceName, ServiceVersion to logging scope.
    /// Default: true
    /// </summary>
    /// <remarks>
    /// Disable for platform-level services that don't operate in a tenant context
    /// (e.g., Secrets service which uses Azure Key Vault).
    /// </remarks>
    public bool EnableTenantEnrichment { get; set; } = true;

    /// <summary>
    /// Enable request logging middleware.
    /// Logs HTTP requests/responses with full context.
    /// Default: true
    /// </summary>
    public bool EnableRequestLogging { get; set; } = true;

    /// <summary>
    /// Enable audit middleware for tracking changes.
    /// Default: false
    /// </summary>
    /// <remarks>
    /// Enable for services that track user/data changes:
    /// Identity (user/role changes), Billing (payment changes), Servers (lifecycle changes).
    /// </remarks>
    public bool EnableAuditMiddleware { get; set; }
}
