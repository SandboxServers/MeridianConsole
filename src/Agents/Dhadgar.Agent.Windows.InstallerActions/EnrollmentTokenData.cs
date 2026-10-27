namespace Dhadgar.Agent.Windows.InstallerActions;

/// <summary>
/// Where the installer writes the enrollment token, and what it accepts (D-S245). Dependency-free, so
/// Dhadgar.Agent.Windows.Tests links this file and pins it against AgentRegistry (the installer-agent seam).
/// </summary>
internal static class EnrollmentTokenData
{
    /// <summary>The agent key under HKLM (64-bit view). Must equal AgentRegistry.KeyPath.</summary>
    internal const string KeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>The token value name. Must equal AgentRegistry.EnrollmentTokenValueName.</summary>
    internal const string ValueName = "EnrollmentToken";

    /// <summary>Upper bound on the token length the installer writes.</summary>
    internal const int MaxLength = 4096;

    /// <summary>True when the token is non-empty, at most <see cref="MaxLength"/> characters and has no control characters.</summary>
    internal static bool IsAcceptable(string? token)
    {
        if (token is null || token.Length == 0 || token.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in token)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
