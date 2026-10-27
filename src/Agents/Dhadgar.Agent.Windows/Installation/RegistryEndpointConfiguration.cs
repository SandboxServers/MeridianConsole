using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Feeds the control plane endpoint the installer wrote to HKLM into configuration (D-S230).
/// </summary>
/// <remarks>
/// SECURITY: The endpoint is not a secret and is never logged by this type. Only the registry read is performed here;
/// the value still passes the existing <c>ControlPlaneOptions</c> validation (absolute HTTPS URL).
/// </remarks>
internal static class RegistryEndpointConfiguration
{
    /// <summary>Configuration key the endpoint is bound to.</summary>
    internal const string ConfigurationKey = "Agent:ControlPlane:Endpoint";

    /// <summary>
    /// Inserts the installer's endpoint as a configuration source right after the last JSON source (file or stream), so it
    /// overrides appsettings*.json while environment variables and the command line still win. A missing or
    /// blank value adds nothing.
    /// </summary>
    /// <param name="configuration">The host configuration builder to extend.</param>
    /// <param name="readEndpoint">Supplies the endpoint value; null or blank means no source is added.</param>
    internal static void Apply(IConfigurationBuilder configuration, Func<string?> readEndpoint)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(readEndpoint);

        var endpoint = readEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var source = new MemoryConfigurationSource
        {
            InitialData = [new KeyValuePair<string, string?>(ConfigurationKey, endpoint.Trim())]
        };

        var lastJson = -1;
        for (var i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is JsonConfigurationSource or JsonStreamConfigurationSource)
            {
                lastJson = i;
            }
        }

        configuration.Sources.Insert(lastJson + 1, source);
    }

    /// <summary>
    /// Reads the endpoint from HKLM (64-bit view), where the installer writes it. A missing key or value, a non-string
    /// value, or an access failure gives null; the agent then falls back to appsettings.json.
    /// </summary>
    /// <returns>The endpoint string, or null.</returns>
    [SupportedOSPlatform("windows")]
    internal static string? ReadFromRegistry()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(AgentRegistry.KeyPath, writable: false);
            return key?.GetValue(AgentRegistry.ControlPlaneEndpointValueName) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
