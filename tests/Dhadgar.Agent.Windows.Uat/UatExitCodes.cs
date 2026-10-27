namespace Dhadgar.Agent.Windows.Uat;

/// <summary>Exit codes of the UAT driver, per the wave 2U contract.</summary>
internal static class UatExitCodes
{
    /// <summary>Every step passed and cleanup is clean.</summary>
    public const int Pass = 0;

    /// <summary>A step failed or was inconclusive (cleanup clean).</summary>
    public const int Fail = 1;

    /// <summary>Refused before any machine change.</summary>
    public const int Refused = 2;

    /// <summary>Footprint left after cleanup (overrides <see cref="Fail"/>).</summary>
    public const int FootprintLeft = 3;

    /// <summary>Internal driver error (cleanup still ran).</summary>
    public const int InternalError = 4;
}
