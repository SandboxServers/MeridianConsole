using Microsoft.Extensions.Compliance.Classification;

namespace Dhadgar.ServiceDefaults.Logging;

/// <summary>
/// Defines data classification taxonomy for PII and sensitive data redaction.
/// These classifications are used with [DataClassification] attributes on log parameters
/// to automatically redact sensitive information before it reaches log sinks.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "User {Email} authenticated")]
/// public partial void UserAuthenticated([DataClassification(DhadgarDataClassifications.Email)] string email);
/// </code>
/// </remarks>
public static class DhadgarDataClassifications
{
    /// <summary>
    /// Taxonomy name used to group all Dhadgar-specific data classifications.
    /// </summary>
    public static string TaxonomyName => "Dhadgar";

    /// <summary>
    /// Classification for email addresses. Redacted to "***@***.***".
    /// Use for any user email, contact email, or notification recipient.
    /// </summary>
    public static DataClassification Email => new(TaxonomyName, nameof(Email));

    /// <summary>
    /// Classification for authentication tokens (JWT, Bearer, refresh tokens).
    /// Redacted to "[REDACTED-TOKEN:len=N]" where N is the original length.
    /// </summary>
    public static DataClassification Token => new(TaxonomyName, nameof(Token));

    /// <summary>
    /// Classification for passwords and password hashes.
    /// Fully erased - no length or pattern information preserved.
    /// </summary>
    public static DataClassification Password => new(TaxonomyName, nameof(Password));

    /// <summary>
    /// Classification for database connection strings.
    /// Preserves Host/Database, redacts credentials.
    /// Output: "Host=xxx;Database=xxx;[CREDENTIALS-REDACTED]"
    /// </summary>
    public static DataClassification ConnectionString => new(TaxonomyName, nameof(ConnectionString));

    /// <summary>
    /// Classification for API keys (Steam, Discord, third-party integrations).
    /// Redacted to "[REDACTED-TOKEN:len=N]" like Token classification.
    /// </summary>
    public static DataClassification ApiKey => new(TaxonomyName, nameof(ApiKey));

    /// <summary>
    /// Classification for IP addresses (client IPs, node IPs).
    /// Note: IPs may be logged for security audit purposes in some contexts.
    /// Use this classification when IP should be redacted (e.g., user-facing logs).
    /// </summary>
    public static DataClassification IpAddress => new(TaxonomyName, nameof(IpAddress));
}

/// <summary>
/// Attribute for marking log parameters with Email data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class EmailDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDataAttribute"/> class.
    /// </summary>
    public EmailDataAttribute() : base(DhadgarDataClassifications.Email) { }
}

/// <summary>
/// Attribute for marking log parameters with Token data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TokenDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenDataAttribute"/> class.
    /// </summary>
    public TokenDataAttribute() : base(DhadgarDataClassifications.Token) { }
}

/// <summary>
/// Attribute for marking log parameters with Password data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PasswordDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordDataAttribute"/> class.
    /// </summary>
    public PasswordDataAttribute() : base(DhadgarDataClassifications.Password) { }
}

/// <summary>
/// Attribute for marking log parameters with ConnectionString data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ConnectionStringDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStringDataAttribute"/> class.
    /// </summary>
    public ConnectionStringDataAttribute() : base(DhadgarDataClassifications.ConnectionString) { }
}

/// <summary>
/// Attribute for marking log parameters with ApiKey data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ApiKeyDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyDataAttribute"/> class.
    /// </summary>
    public ApiKeyDataAttribute() : base(DhadgarDataClassifications.ApiKey) { }
}

/// <summary>
/// Attribute for marking log parameters with IpAddress data classification.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class IpAddressDataAttribute : DataClassificationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpAddressDataAttribute"/> class.
    /// </summary>
    public IpAddressDataAttribute() : base(DhadgarDataClassifications.IpAddress) { }
}
