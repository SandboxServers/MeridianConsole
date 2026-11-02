using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Middleware for validating mTLS client certificates on agent endpoints.
/// Extracts the node ID from the certificate's SPIFFE ID and stores it in HttpContext.Items.
/// </summary>
/// <remarks>
/// This middleware:
/// - Only processes requests to agent endpoints (configurable via MtlsOptions.AgentEndpointPrefix)
/// - Allows unauthenticated access to exempt paths (enroll, ca-certificate)
/// - Validates certificates were issued by our CA
/// - Extracts node ID from SPIFFE ID in certificate SAN
/// - Stores validated node ID in HttpContext.Items["NodeId"]
/// - Creates a ClaimsPrincipal with node_id claim for authorization
/// </remarks>
public sealed class MtlsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MtlsOptions _options;
    private readonly ILogger<MtlsMiddleware> _logger;

    /// <summary>
    /// Key used to store the validated node ID in HttpContext.Items.
    /// </summary>
    public const string NodeIdItemKey = "NodeId";

    /// <summary>
    /// Key used to store the certificate thumbprint in HttpContext.Items.
    /// </summary>
    public const string CertificateThumbprintItemKey = "CertificateThumbprint";

    /// <summary>
    /// Key used to store the SPIFFE ID in HttpContext.Items.
    /// </summary>
    public const string SpiffeIdItemKey = "SpiffeId";

    /// <summary>
    /// Authentication type of the identity this middleware creates. <see cref="AgentIdentityHandler"/> checks it.
    /// </summary>
    public const string AuthenticationType = "mTLS";

    public MtlsMiddleware(
        RequestDelegate next,
        IOptions<MtlsOptions> options,
        ILogger<MtlsMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICertificateValidationService validationService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Check if this is an agent endpoint or the agent hub
        var isAgentEndpoint = IsAgentEndpoint(path);
        if (!isAgentEndpoint && !IsHubEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Check if this path is exempt from mTLS (exempt paths are relative to AgentEndpointPrefix only)
        if (isAgentEndpoint && IsExemptPath(path))
        {
            _logger.LogDebug("Path {Path} is exempt from mTLS validation", path);
            await _next(context);
            return;
        }

        // If mTLS is disabled, skip validation (development mode)
        if (!_options.Enabled)
        {
            _logger.LogDebug("mTLS is disabled, skipping certificate validation for {Path}", path);
            await _next(context);
            return;
        }

        // Get the client certificate
        var clientCertificate = await context.Connection.GetClientCertificateAsync();

        if (clientCertificate is null)
        {
            if (_options.RequireClientCertificate)
            {
                _logger.LogWarning(
                    "No client certificate provided for protected endpoint {Path}",
                    path);

                await MtlsProblemWriter.WriteUnauthorizedAsync(context, "certificate_required",
                    "A client certificate is required for this endpoint");
                return;
            }

            _logger.LogDebug(
                "No client certificate provided but RequireClientCertificate=false, allowing request to {Path}",
                path);

            await _next(context);
            return;
        }

        // Validate the certificate
        var validationResult = await validationService.ValidateClientCertificateAsync(clientCertificate);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Certificate validation failed for {Path}. Error: {ErrorCode} - {ErrorMessage}",
                path, validationResult.ErrorCode, validationResult.ErrorMessage);

            if (string.Equals(validationResult.ErrorCode, CertificateValidationResult.UnavailableErrorCode, StringComparison.Ordinal))
            {
                await MtlsProblemWriter.WriteServiceUnavailableAsync(context, validationResult.ErrorCode!,
                    validationResult.ErrorMessage!);
                return;
            }

            await MtlsProblemWriter.WriteUnauthorizedAsync(context, validationResult.ErrorCode!,
                validationResult.ErrorMessage!);
            return;
        }

        // Store validated information in HttpContext.Items
        context.Items[NodeIdItemKey] = validationResult.NodeId!.Value;
        context.Items[CertificateThumbprintItemKey] = validationResult.Thumbprint;
        context.Items[SpiffeIdItemKey] = validationResult.SpiffeId;

        // Create a ClaimsPrincipal with the node identity
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validationResult.NodeId!.Value.ToString()),
            new("node_id", validationResult.NodeId!.Value.ToString()),
            new("spiffe_id", validationResult.SpiffeId!),
            new("certificate_thumbprint", validationResult.Thumbprint!)
        };

        // Expiry travels with the principal so long-lived connections (the agent hub) can re-check it
        if (validationResult.NotAfter is { } notAfter)
        {
            claims.Add(new Claim(
                "certificate_not_after",
                new DateTimeOffset(notAfter).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var identity = new ClaimsIdentity(claims, AuthenticationType);
        var principal = new ClaimsPrincipal(identity);

        // Set the user on the context (this allows [Authorize] to work)
        context.User = principal;

        _logger.LogDebug(
            "mTLS authentication successful for node {NodeId} on {Path}",
            validationResult.NodeId, path);

        await _next(context);
    }

    private bool IsAgentEndpoint(string path)
    {
        var normalizedPrefix = NormalizePrefix(_options.AgentEndpointPrefix);
        return path.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the path is the agent hub or one of its sub-paths. Matches whole segments only,
    /// so "/hubs/agentx" is not protected and "/hubs/agent/negotiate" is.
    /// </summary>
    private bool IsHubEndpoint(PathString path)
    {
        var hubPrefix = new PathString(NormalizePrefix(_options.HubPathPrefix));
        return path.StartsWithSegments(hubPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExemptPath(string path)
    {
        var normalizedPrefix = NormalizePrefix(_options.AgentEndpointPrefix);

        // Remove the prefix to get the relative path
        var relativePath = path[normalizedPrefix.Length..];

        // Ensure relative path starts with '/' for consistent comparison
        if (relativePath.Length > 0 && !relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }

        foreach (var exemptPath in _options.ExemptPaths)
        {
            // Normalize exempt path to start with '/'
            var normalizedExemptPath = exemptPath.StartsWith('/')
                ? exemptPath
                : "/" + exemptPath;

            // Exact match
            if (string.Equals(relativePath, normalizedExemptPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Match with or without trailing slash
            if (relativePath.StartsWith(normalizedExemptPath, StringComparison.OrdinalIgnoreCase))
            {
                // Must be exact match or followed by '/' or query string
                var remaining = relativePath[normalizedExemptPath.Length..];
                if (remaining.Length == 0 ||
                    remaining.StartsWith('/') ||
                    remaining.StartsWith('?'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes the prefix to ensure it starts with '/' and does not end with '/'.
    /// </summary>
    internal static string NormalizePrefix(string prefix)
    {
        var normalized = prefix.TrimEnd('/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }
        return normalized;
    }

}

/// <summary>
/// Extension methods for adding mTLS middleware and services.
/// </summary>
public static class MtlsMiddlewareExtensions
{
    /// <summary>
    /// Adds mTLS services to the service collection.
    /// </summary>
    /// <remarks>
    /// In non-Development environments, this method enforces that MtlsOptions.Enabled is true.
    /// If mTLS is disabled in production, the service will fail to start.
    /// </remarks>
    public static IServiceCollection AddMtlsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MtlsOptions>()
            .Bind(configuration.GetSection(MtlsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Enforce mTLS is enabled in non-Development environments
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (!environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            var mtlsSection = configuration.GetSection(MtlsOptions.SectionName);
            var enabledValue = mtlsSection.GetValue<bool?>("Enabled");

            // If explicitly set to false in config, fail startup
            if (enabledValue == false)
            {
                throw new InvalidOperationException(
                    $"mTLS cannot be disabled in {environment} environment. " +
                    $"Set '{MtlsOptions.SectionName}:Enabled' to true or remove the setting to use the default (true).");
            }
        }

        // Refuse to start when forwarded certificates are enabled without a usable trust list (D-S25, C6).
        var forwardedOptions = configuration
            .GetSection($"{MtlsOptions.SectionName}:ForwardedCertificate")
            .Get<ForwardedCertificateOptions>() ?? new ForwardedCertificateOptions();

        if (forwardedOptions.Enabled)
        {
            if (forwardedOptions.TrustedProxies.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{MtlsOptions.SectionName}:ForwardedCertificate:TrustedProxies' must contain at least one CIDR " +
                    "when 'ForwardedCertificate:Enabled' is true. See D-S210.");
            }

            foreach (var proxy in forwardedOptions.TrustedProxies)
            {
                // A /0 prefix matches every peer, so any client could present a forwarded certificate.
                if (!IPNetwork.TryParse(proxy, out var network) || network.PrefixLength == 0)
                {
                    throw new InvalidOperationException(
                        $"'{MtlsOptions.SectionName}:ForwardedCertificate:TrustedProxies' contains an invalid CIDR. " +
                        "Each entry must be an IP address with a prefix length, for example 10.42.0.0/16, " +
                        "and a prefix length of 0 is not allowed. See D-S210.");
                }
            }
        }

        // Renewal grace (D-S212): 0 revokes immediately; anything longer than the maximum is refused at start.
        var graceMinutes = configuration
            .GetSection(MtlsOptions.SectionName)
            .GetValue<int?>("RenewalGraceMinutes") ?? AgentCertificateRenewalPolicy.DefaultGraceMinutes;
        if (graceMinutes < 0 || graceMinutes > AgentCertificateRenewalPolicy.MaxGraceMinutes)
        {
            throw new InvalidOperationException(
                $"'{MtlsOptions.SectionName}:RenewalGraceMinutes' must be between 0 and " +
                $"{AgentCertificateRenewalPolicy.MaxGraceMinutes}. See D-S212.");
        }

        services.AddScoped<ICertificateValidationService, CertificateValidationService>();

        // Named scheme for agent policies. Policies without a scheme evaluate HttpContext.User,
        // which MtlsMiddleware sets; the scheme writes the C5 challenge and forbid responses.
        services.AddAuthentication(AgentMtlsAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AgentMtlsAuthenticationHandler>(
                AgentMtlsAuthenticationHandler.SchemeName,
                null);

        return services;
    }

    /// <summary>
    /// Adds the mTLS middleware to the application pipeline.
    /// This should be called after UseRouting() but before UseAuthorization().
    /// </summary>
    /// <remarks>
    /// <see cref="ForwardedClientCertificateMiddleware"/> runs first, so a certificate forwarded by a
    /// trusted proxy is on the connection before <see cref="MtlsMiddleware"/> validates it.
    /// </remarks>
    public static IApplicationBuilder UseMtlsAuthentication(this IApplicationBuilder app)
    {
        return app
            .UseMiddleware<ForwardedClientCertificateMiddleware>()
            .UseMiddleware<MtlsMiddleware>();
    }
}
