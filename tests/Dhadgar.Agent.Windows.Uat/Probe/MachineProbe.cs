using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Uat.Msi;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Uat.Probe;

/// <summary>What the machine still holds of the agent, as <c>kind:identity</c> strings.</summary>
internal sealed record Footprint(IReadOnlyList<string> Items)
{
    /// <summary>True when nothing of the agent is left.</summary>
    public bool IsEmpty => Items.Count == 0;
}

/// <summary>Reads the machine footprint (D-S225). Behind an interface so tests use fakes.</summary>
internal interface IMachineProbe
{
    /// <summary>Scans the machine for any agent footprint.</summary>
    Footprint Scan();
}

/// <summary>
/// The real probe. Read-only: it opens LocalMachine stores for reading and never writes.
/// The event source is not a footprint item (a developer machine may have it).
/// </summary>
internal sealed class MachineProbe : IMachineProbe
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public Footprint Scan()
    {
        var items = new List<string>();

        if (ServiceExists(AgentWindowsFacts.ServiceName))
        {
            items.Add($"service:{AgentWindowsFacts.ServiceName}");
        }

        using (var key = Registry.LocalMachine.OpenSubKey(AgentWindowsFacts.RegistryKeyPath))
        {
            if (key is not null)
            {
                items.Add($"registryKey:HKLM\\{AgentWindowsFacts.RegistryKeyPath}");
            }
        }

        if (StoreExists(AgentWindowsFacts.CaStoreName))
        {
            items.Add($"certStore:{AgentWindowsFacts.CaStoreName}");
        }

        ForEachCertificate(StoreName.My, cert =>
        {
            if (AgentCertificateMatcher.IsAgentClientCertificate(cert, new DateTimeOffset(cert.NotBefore))
                || AgentCertificateMatcher.IsAgentCa(cert))
            {
                items.Add($"certificate:My:{cert.Thumbprint}");
            }
        });

        ForEachCertificate(StoreName.Root, cert =>
        {
            if (AgentCertificateMatcher.IsAgentCa(cert))
            {
                items.Add($"certificate:Root:{cert.Thumbprint}");
            }
        });

        if (Directory.Exists(AgentDirectories.MachineAgentDirectory))
        {
            items.Add($"directory:{AgentDirectories.MachineAgentDirectory}");
        }

        if (Directory.Exists(AgentDirectories.MachineProgramFilesAgentDirectory))
        {
            items.Add($"directory:{AgentDirectories.MachineProgramFilesAgentDirectory}");
        }

        // An installer that cannot be asked leaves the product unproven, so the footprint says so rather than "none".
        var related = new MsiProducts().RelatedProducts(AgentWindowsFacts.MsiUpgradeCode);
        if (related.IsFailure)
        {
            items.Add("msiProduct:unreadable");
        }
        else if (related.Value.Count > 0)
        {
            items.Add($"msiProduct:{AgentWindowsFacts.MsiUpgradeCode}");
        }

        items.AddRange(ListUatContainers().Select(name => $"container:{name}"));
        return new Footprint(items);
    }

    private static bool ServiceExists(string name)
    {
        var result = CommandRunner.RunAsync("sc.exe", ["query", name], ToolTimeout).GetAwaiter().GetResult();
        return result.IsSuccess && result.Value.ExitCode == 0;
    }

    private static IReadOnlyList<string> ListUatContainers()
    {
        var result = CommandRunner.RunAsync(
            "docker",
            ["ps", "-a", "--filter", "name=meridian-uat-", "--format", "{{.Names}}"],
            ToolTimeout).GetAwaiter().GetResult();

        // Docker down means no containers can be listed; preflight refuses to run without Docker.
        if (result.IsFailure || result.Value.ExitCode != 0)
        {
            return [];
        }

        return result.Value.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.StartsWith("meridian-uat-", StringComparison.Ordinal))
            .ToList();
    }

    private static bool StoreExists(string name)
    {
        try
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static void ForEachCertificate(StoreName name, Action<X509Certificate2> visit)
    {
        using var store = new X509Store(name, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var certs = store.Certificates;
        try
        {
            foreach (var cert in certs)
            {
                visit(cert);
            }
        }
        finally
        {
            foreach (var cert in certs)
            {
                cert.Dispose();
            }
        }
    }
}
