namespace Dhadgar.ServiceDefaults.Readiness;

public interface IReadinessCheck
{
    Task<ReadinessResult> CheckAsync(CancellationToken ct);
}
