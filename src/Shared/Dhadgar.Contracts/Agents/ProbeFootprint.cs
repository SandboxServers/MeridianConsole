namespace Dhadgar.Contracts.Agents;

/// <summary>
/// The JSON the lab probe prints inside a Windows member (<c>footprint --json</c>) and the lab reads. Metadata only:
/// no certificate material, no registry value contents.
/// </summary>
public sealed record ProbeFootprint(
    ServiceInfo? Service,
    RegistryInfo Registry,
    IReadOnlyList<CertInfo> MachineMy,
    IReadOnlyList<CertInfo> MachineRoot,
    IReadOnlyList<CertInfo> AgentCa,
    StateFileInfo StateFile,
    bool EventSourceExists);

/// <summary>The agent service as the Service Control Manager reports it.</summary>
public sealed record ServiceInfo(string State, string StartType, string Account, string BinaryPath);

/// <summary>Whether the agent registry key exists and which values it holds (presence and length only).</summary>
public sealed record RegistryInfo(bool Exists, IReadOnlyList<RegistryValueInfo> Values);

/// <summary>One registry value: its name, kind and length; never its contents.</summary>
public sealed record RegistryValueInfo(string Name, string Kind, bool Present, int Length);

/// <summary>Certificate metadata. A private key is never exported; <c>HasPrivateKey</c> only reports presence.</summary>
public sealed record CertInfo(
    string Thumbprint,
    string Subject,
    string Issuer,
    string Serial,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool HasPrivateKey);

/// <summary>The agent state file: whether it exists, where it is and when it was last written.</summary>
public sealed record StateFileInfo(bool Exists, string Path, DateTimeOffset? LastWrite);
