namespace Dhadgar.ServiceDefaults.Readiness;

public sealed record ReadinessResult(bool IsReady, object? Details)
{
    public static ReadinessResult Ready(object? details = null) => new(true, details);
    public static ReadinessResult NotReady(object? details = null) => new(false, details);
}
