using System.Diagnostics;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>
/// The MSI leg (P23, D-S223). Its manifest entries are recorded before U11a's install, so the cleanup engine can
/// undo everything the installer leaves, even when the install fails part way.
/// </summary>
internal static class MsiLegs
{
    private const string HklmHive = "HKLM";
    private const string ParentKeyPath = AgentWindowsFacts.RegistryParentKeyPath;
    private const string AgentKeyPath = AgentWindowsFacts.RegistryKeyPath;

    /// <summary>
    /// The entries the MSI leg may cause, in the spec's order: the product, the service, the two registry keys, the
    /// event source, the CA store, and the two directories. Each records whether the item existed before.
    /// </summary>
    internal static IReadOnlyList<CleanupEntry> PreInstallEntries(
        string msiPath,
        bool registryParentExisted,
        bool registryKeyExisted,
        bool eventSourceExisted,
        bool programDataExisted,
        bool programFilesExisted) =>
    [
        new MsiProductEntry(AgentWindowsFacts.MsiUpgradeCode, msiPath),
        new ServiceEntry(AgentWindowsFacts.ServiceName),
        new RegistryKeyEntry(HklmHive, ParentKeyPath, registryParentExisted),
        new RegistryKeyEntry(HklmHive, AgentKeyPath, registryKeyExisted),
        new EventSourceEntry(AgentWindowsFacts.EventLogSourceName, eventSourceExisted),
        new CertStoreEntry(AgentWindowsFacts.CaStoreName, ExistedBefore: false),
        new DirectoryEntry(AgentDirectories.MachineAgentDirectory, programDataExisted),
        new DirectoryEntry(AgentDirectories.MachineProgramFilesDirectory, programFilesExisted),
    ];

    /// <summary>Reads the machine and appends <see cref="PreInstallEntries"/> to <paramref name="manifest"/> in order.</summary>
    internal static Result AppendPreInstallEntries(CleanupManifest manifest, string msiPath)
    {
        var entries = PreInstallEntries(
            msiPath,
            registryParentExisted: RegistryKeyExists(ParentKeyPath),
            registryKeyExisted: RegistryKeyExists(AgentKeyPath),
            eventSourceExisted: EventSourceExists(AgentWindowsFacts.EventLogSourceName),
            programDataExisted: Directory.Exists(AgentDirectories.MachineAgentDirectory),
            programFilesExisted: Directory.Exists(AgentDirectories.MachineProgramFilesDirectory));

        foreach (var entry in entries)
        {
            var appended = manifest.Append(entry);
            if (appended.IsFailure)
            {
                return appended;
            }
        }

        return Result.Success();
    }

    private static bool RegistryKeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    /// <summary>
    /// True when the event source is already registered. When the check itself fails the source counts as existing,
    /// so the undo never deletes a source the run cannot prove it created.
    /// </summary>
    private static bool EventSourceExists(string source)
    {
#pragma warning disable CA1031 // A failed check must not stop the leg; see the comment above.
        try
        {
            return EventLog.SourceExists(source);
        }
        catch (Exception)
        {
            return true;
        }
#pragma warning restore CA1031
    }
}
