using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Authentication scheme for policies on agent endpoints that rely on mTLS (F3).
/// </summary>
/// <remarks>
/// This handler never authenticates a request itself. <see cref="MtlsMiddleware"/> validates the
/// certificate and sets <c>HttpContext.User</c> after <c>UseAuthentication</c>, so policies without
/// schemes evaluate that user. The handler exists so that a challenge or a forbid writes the C5
/// problem+json shape instead of throwing for a missing default scheme.
/// </remarks>
public sealed class AgentMtlsAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Name of the scheme, registered by <see cref="MtlsMiddlewareExtensions.AddMtlsAuthentication"/>.
    /// </summary>
    public const string SchemeName = "AgentMtls";

    public AgentMtlsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        return MtlsProblemWriter.WriteUnauthorizedAsync(
            Context,
            "certificate_required",
            "A client certificate is required for this endpoint");
    }

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        return MtlsProblemWriter.WriteForbiddenAsync(
            Context,
            "forbidden",
            "The authenticated client is not allowed to access this endpoint");
    }
}
