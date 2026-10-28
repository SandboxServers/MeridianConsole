namespace Dhadgar.Lab.Uat;

/// <summary>
/// Answers whether the host has a capability named in a section's <c>requires</c> list (docker, windows, windows_containers, network).
/// An unknown name answers false.
/// </summary>
public interface ICapabilityProbe
{
    /// <summary>
    /// Checks one capability. Never throws; a failed check answers false.
    /// </summary>
    /// <param name="capability">The capability name from the spec.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<bool> IsAvailableAsync(string capability, CancellationToken ct);
}
