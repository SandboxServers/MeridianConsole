using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Testing.Authentication;

/// <summary>
/// Authentication handler for testing that reads user identity from HTTP headers.
/// Supports X-Test-User-Id, X-Test-Org-Id, and X-Test-Role headers.
/// </summary>
public class FakeClaimsAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Header name for the test user ID.
    /// </summary>
    public const string UserIdHeader = "X-Test-User-Id";

    /// <summary>
    /// Header name for the test organization ID.
    /// </summary>
    public const string OrgIdHeader = "X-Test-Org-Id";

    /// <summary>
    /// Header name for the test user role.
    /// </summary>
    public const string RoleHeader = "X-Test-Role";

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeClaimsAuthenticationHandler"/> class.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete - required by base class constructor signature
    public FakeClaimsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }
#pragma warning restore CS0618

    /// <summary>
    /// Handles authentication by reading test headers and creating a claims principal.
    /// </summary>
    /// <returns>Authentication result with success if X-Test-User-Id header is present</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if user ID header is present
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = userIdValues.FirstOrDefault();
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId)
        };

        // Add organization ID claim if present
        if (Request.Headers.TryGetValue(OrgIdHeader, out var orgIdValues))
        {
            var orgId = orgIdValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(orgId))
            {
                claims.Add(new Claim("org_id", orgId));
            }
        }

        // Add role claim if present
        if (Request.Headers.TryGetValue(RoleHeader, out var roleValues))
        {
            var role = roleValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("role", role));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
