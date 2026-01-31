using Dhadgar.Testing.Authentication;

namespace Dhadgar.Testing.Http;

/// <summary>
/// Extension methods for HttpClient to simplify setting test authentication headers.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Sets the X-Test-User-Id header for fake authentication in tests.
    /// </summary>
    /// <param name="client">The HTTP client to configure</param>
    /// <param name="userId">The test user ID to use</param>
    /// <returns>The HTTP client for method chaining</returns>
    public static HttpClient WithTestUser(this HttpClient client, string userId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        client.DefaultRequestHeaders.Remove(FakeClaimsAuthenticationHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(FakeClaimsAuthenticationHandler.UserIdHeader, userId);
        return client;
    }

    /// <summary>
    /// Sets the X-Test-Org-Id header for fake authentication in tests.
    /// </summary>
    /// <param name="client">The HTTP client to configure</param>
    /// <param name="orgId">The test organization ID to use</param>
    /// <returns>The HTTP client for method chaining</returns>
    public static HttpClient WithTestOrg(this HttpClient client, string orgId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgId);

        client.DefaultRequestHeaders.Remove(FakeClaimsAuthenticationHandler.OrgIdHeader);
        client.DefaultRequestHeaders.Add(FakeClaimsAuthenticationHandler.OrgIdHeader, orgId);
        return client;
    }

    /// <summary>
    /// Sets the X-Test-Role header for fake authentication in tests.
    /// </summary>
    /// <param name="client">The HTTP client to configure</param>
    /// <param name="role">The test role to use</param>
    /// <returns>The HTTP client for method chaining</returns>
    public static HttpClient WithTestRole(this HttpClient client, string role)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        client.DefaultRequestHeaders.Remove(FakeClaimsAuthenticationHandler.RoleHeader);
        client.DefaultRequestHeaders.Add(FakeClaimsAuthenticationHandler.RoleHeader, role);
        return client;
    }
}
