using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// A component that reports into <c>GET /status</c>. Lease, stack, fault, pool and job packets (L05, L07, L08, L19, L27, L30)
/// register one of these; until then the idle defaults stand.
/// </summary>
public interface ILabStatusSource
{
    /// <summary>
    /// Writes this component's fields into the builder. Must not include lease ids, tokens or tool arguments.
    /// </summary>
    void Contribute(StatusBuilder builder);
}

/// <summary>
/// Collects the status fields. Starts at the idle defaults: no lease, stack <c>down</c>, no processes, no faults, no job.
/// </summary>
public sealed class StatusBuilder
{
    public LeaseStatusDto Lease { get; set; } = new(false, null, null, null, null);

    public string StackState { get; set; } = "down";

    public List<ProcessStatusDto> Processes { get; } = [];

    public int ActiveFaults { get; set; }

    public List<string> LeasedMembers { get; } = [];

    public string? RunningJob { get; set; }
}

/// <summary>
/// Maps <c>GET /status</c>. The route sits behind the edge guard, so it only answers a valid bearer token.
/// </summary>
internal static class StatusEndpoint
{
    private static readonly string Version = typeof(StatusEndpoint).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Adds the status route to the application.
    /// </summary>
    public static void MapStatus(this IEndpointRouteBuilder endpoints, DateTimeOffset startedAt)
    {
        var pid = Environment.ProcessId;
        endpoints.MapGet(LabDefaults.StatusPath, (HttpContext context) =>
        {
            var sources = context.RequestServices.GetServices<ILabStatusSource>();
            return Results.Ok(Build(sources, pid, startedAt));
        });
    }

    /// <summary>
    /// Builds the status body from every registered source.
    /// </summary>
    internal static StatusDto Build(IEnumerable<ILabStatusSource> sources, int pid, DateTimeOffset startedAt)
    {
        var builder = new StatusBuilder();
        foreach (var source in sources)
        {
            source.Contribute(builder);
        }

        return new StatusDto(
            Version,
            pid,
            startedAt,
            builder.Lease,
            builder.StackState,
            builder.Processes,
            builder.ActiveFaults,
            builder.LeasedMembers,
            builder.RunningJob);
    }
}
