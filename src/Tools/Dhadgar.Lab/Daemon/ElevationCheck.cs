using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// Detects whether the daemon runs elevated. Windows reads the token elevation type; other systems check for uid 0.
/// The lab never needs elevation, so the daemon refuses to start when it is.
/// </summary>
public static class ElevationCheck
{
    private const int TokenElevationTypeClass = 18;
    private const int TokenElevationTypeDefault = 1;
    private const int TokenElevationTypeFull = 2;

    /// <summary>
    /// True when the current process runs with administrator rights.
    /// </summary>
    public static bool IsElevated() =>
        OperatingSystem.IsWindows() ? IsWindowsElevated() : EffectiveUid() == 0;

    /// <summary>
    /// The effective user id on Unix-like systems. Only meaningful when the platform is not Windows.
    /// </summary>
    public static uint EffectiveUid() => geteuid();

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

        if (!GetTokenInformation(identity.Token, TokenElevationTypeClass, out int elevationType, sizeof(int), out _))
        {
            // Fail closed: when the token cannot be read, fall back to the group check alone.
            return isAdmin;
        }

        return elevationType == TokenElevationTypeFull
            || (elevationType == TokenElevationTypeDefault && isAdmin);
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();
}
