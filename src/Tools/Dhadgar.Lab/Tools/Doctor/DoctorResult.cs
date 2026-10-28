namespace Dhadgar.Lab.Tools;

/// <summary>
/// The four severities of a doctor line. <c>info</c> is a fact, not a verdict; a check the tool does not run is not emitted.
/// </summary>
public static class DoctorStatus
{
    /// <summary>Checked, fine.</summary>
    public const string Ok = "ok";

    /// <summary>Checked; the lab works, but something is degraded or unknown.</summary>
    public const string Warn = "warn";

    /// <summary>Checked; a lab action will fail.</summary>
    public const string Fail = "fail";

    /// <summary>A fact, not a verdict (OS name, dev ports in use, "not Windows").</summary>
    public const string Info = "info";
}

/// <summary>
/// One doctor line: a name, a status (see <see cref="DoctorStatus"/>) and a short detail.
/// </summary>
public sealed record DoctorCheck(string Name, string Status, string Detail);

/// <summary>
/// The result of <c>lab_doctor</c>. <see cref="Ok"/> is true when no check has status <c>fail</c>.
/// </summary>
public sealed record LabDoctorResult(bool Ok, IReadOnlyList<DoctorCheck> Checks)
{
    /// <summary>
    /// Builds the result from the checks, deriving <see cref="Ok"/>.
    /// </summary>
    public static LabDoctorResult From(IReadOnlyList<DoctorCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        return new LabDoctorResult(checks.All(c => c.Status != DoctorStatus.Fail), checks);
    }
}
