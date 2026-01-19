using Microsoft.IdentityModel.Protocols;

namespace Dhadgar.Secrets.Infrastructure;

/// <summary>
/// A document retriever that rewrites external URLs to internal URLs for Docker/K8s environments.
/// This allows the JWT Bearer middleware to fetch JWKS from internal service URLs while still
/// validating tokens that have external issuer URLs.
/// </summary>
public sealed class UrlRewritingDocumentRetriever : IDocumentRetriever, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _externalBaseUrl;
    private readonly string _internalBaseUrl;

    /// <summary>
    /// Gets or sets whether HTTPS is required for document retrieval.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Creates a new instance of UrlRewritingDocumentRetriever.
    /// </summary>
    /// <param name="externalBaseUrl">The external base URL (e.g., https://dev.meridianconsole.com/api/v1/identity/)</param>
    /// <param name="internalBaseUrl">The internal base URL (e.g., http://identity:8080)</param>
    // CA1054: URL string parameters are standard for configuration-driven scenarios.
    // Callers typically receive these from IConfiguration which provides strings.
#pragma warning disable CA1054
    public UrlRewritingDocumentRetriever(string externalBaseUrl, string internalBaseUrl)
#pragma warning restore CA1054
    {
        _httpClient = new HttpClient();
        _externalBaseUrl = externalBaseUrl.TrimEnd('/');
        _internalBaseUrl = internalBaseUrl.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        // Rewrite external URLs to internal URLs
        var rewrittenAddress = RewriteUrl(address);

        if (RequireHttps && !rewrittenAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Allow HTTP for internal URLs in development
            if (!rewrittenAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The address '{address}' is not valid. HTTPS is required.");
            }
        }

        var response = await _httpClient.GetAsync(rewrittenAddress, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancel);
    }

    private string RewriteUrl(string url)
    {
        // If the URL starts with the external base URL, replace it with the internal base URL
        if (url.StartsWith(_externalBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            var path = url.Substring(_externalBaseUrl.Length);
            return _internalBaseUrl + path;
        }

        // Return as-is if it's already an internal URL or a different URL
        return url;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
