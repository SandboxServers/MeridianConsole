using System.Security.Cryptography;
using Dhadgar.Agent.Windows.Uat.Infra;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>Generated secrets for the local stack. Every value is registered with the redactor when it is made.</summary>
internal static class Secrets
{
    /// <summary>Returns 24 random bytes as lowercase hex and registers the value as a password before returning it.</summary>
    public static string GeneratePassword(Redactor redactor)
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        try
        {
            var password = Convert.ToHexString(bytes).ToLowerInvariant();
            redactor.AddSecret(password, SecretKind.Password);
            return password;
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
