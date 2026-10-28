using Dhadgar.Contracts.Nodes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// Builds the product's <c>EnrollNodeRequest</c> for the lab's agent members. The plain token is read from the vault by reference.
/// </summary>
public static class EnrollProbe
{
    /// <summary>
    /// Builds the product contract request; the plain token is read from the vault by ref. Not yet implemented (L01 stub).
    /// </summary>
    public static Result<EnrollNodeRequest> BuildRequest(SecretVault vault, string tokenRef, string platform, out string keyRef)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrEmpty(tokenRef);
        ArgumentException.ThrowIfNullOrEmpty(platform);

        keyRef = string.Empty;
        return Result<EnrollNodeRequest>.Failure("[Lab.NotImplemented] enroll probe");
    }
}
