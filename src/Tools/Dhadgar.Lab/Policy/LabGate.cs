namespace Dhadgar.Lab.Policy;

/// <summary>
/// How a tool is allowed to run. Every tool name maps to exactly one gate (C3, C4); an unmapped name is refused.
/// </summary>
public enum LabGate
{
    /// <summary>Read-only or lab-local; no lease needed.</summary>
    Open,

    /// <summary>Requires the caller to hold the current lease.</summary>
    Leased,

    /// <summary>The tool manages the lease itself (for example acquire and release).</summary>
    OwnLease,

    /// <summary>Reserved. No tool may carry it (D-ML43).</summary>
    Machine
}
