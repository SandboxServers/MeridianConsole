using OpenIddict.Validation.AspNetCore;

namespace Dhadgar.Identity.Authentication;

public static class AuthSchemes
{
    /// <summary>
    /// External cookie scheme for OAuth provider callbacks.
    /// </summary>
    public const string External = "External";

    /// <summary>
    /// OpenIddict validation scheme for JWT bearer tokens.
    /// This is the default scheme for API authentication.
    /// </summary>
    public const string Bearer = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
}
