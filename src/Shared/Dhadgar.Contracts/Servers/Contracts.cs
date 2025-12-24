namespace Dhadgar.Contracts.Servers;

public record ServerId(Guid Value);

public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);

public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
