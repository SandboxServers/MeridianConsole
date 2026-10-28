using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// Parses the daemon bearer token. The token is base64url text that decodes to at least <see cref="LabDefaults.MinTokenBytes"/> bytes.
/// </summary>
public static class TokenValidator
{
    /// <summary>
    /// Decodes a base64url token into a fresh buffer. The caller owns the buffer and zeroes it after use.
    /// Refusals are <c>[Lab.InvalidArgument]</c>; nothing from the token is echoed in the message.
    /// </summary>
    public static Result<byte[]> ParseToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result<byte[]>.Failure("[Lab.InvalidArgument] token is missing");
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(raw.AsSpan().Trim());
        }
        catch (FormatException)
        {
            return Result<byte[]>.Failure("[Lab.InvalidArgument] token is not base64url");
        }

        if (bytes.Length < LabDefaults.MinTokenBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            return Result<byte[]>.Failure($"[Lab.InvalidArgument] token must decode to at least {LabDefaults.MinTokenBytes} bytes");
        }

        return Result<byte[]>.Success(bytes);
    }
}
