using System.Runtime.Versioning;
using System.Security;
using Dhadgar.Contracts.Agents;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Lab.Windows;

/// <summary>
/// Reads the agent's machine registry key (<c>HKLM\SOFTWARE\Meridian Console\Agent</c>) as presence and length only.
/// No registry value is returned: a value of any kind is reduced to its name, its kind and its length.
/// </summary>
public static class AgentRegistryReader
{
    /// <summary>
    /// Reads the agent key. A missing key reads as <c>Exists = false</c>; a denied read is <c>[Lab.Unavailable]</c>.
    /// </summary>
    public static Result<RegistryInfo> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result<RegistryInfo>.Failure("[Lab.Unavailable] the agent registry key is read on Windows only");
        }

        return ReadKey();
    }

    [SupportedOSPlatform("windows")]
    private static Result<RegistryInfo> ReadKey()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AgentWindowsFootprint.RegistryKeyPath, writable: false);
            if (key is null)
            {
                return Result<RegistryInfo>.Success(new RegistryInfo(false, Array.Empty<RegistryValueInfo>()));
            }

            var values = new List<RegistryValueInfo>();
            var names = key.GetValueNames();
            foreach (var name in names)
            {
                values.Add(Describe(key, name));
            }

            // The enrollment token is always reported, so a missing token shows as absent rather than unlisted.
            if (!names.Contains(AgentWindowsFootprint.EnrollmentTokenValueName, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(new RegistryValueInfo(AgentWindowsFootprint.EnrollmentTokenValueName, string.Empty, false, 0));
            }

            return Result<RegistryInfo>.Success(new RegistryInfo(true, values));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return Result<RegistryInfo>.Failure("[Lab.Unavailable] needs an elevated read; use a member");
        }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryValueInfo Describe(RegistryKey key, string name)
    {
        var kind = key.GetValueKind(name).ToString();

        // The value is held only long enough to measure it; it is never copied into the result.
        object? value = key.GetValue(name);
        int length = value switch
        {
            string text => text.Length,
            byte[] bytes => bytes.Length,
            string[] items => items.Sum(item => item.Length),
            _ => 0,
        };

        return new RegistryValueInfo(name, kind, true, length);
    }
}
