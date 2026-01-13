using Dhadgar.Gateway.Options;
using Dhadgar.ServiceDefaults.Readiness;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;

namespace Dhadgar.Gateway.Readiness;

public sealed class YarpReadinessCheck : IReadinessCheck
{
    private readonly IProxyStateLookup _proxyStateLookup;
    private readonly ReadyzOptions _options;

    public YarpReadinessCheck(IProxyStateLookup proxyStateLookup, IOptions<ReadyzOptions> options)
    {
        _proxyStateLookup = proxyStateLookup;
        _options = options.Value ?? new ReadyzOptions();
    }

    public Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        var requiredClusters = _options.RequiredClusters;
        var minimumAvailable = Math.Max(0, _options.MinimumAvailableDestinations);
        var failOnMissing = _options.FailOnMissingCluster;

        var failures = new List<object>();
        var clusters = new List<object>();

        foreach (var clusterId in requiredClusters.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_proxyStateLookup.TryGetCluster(clusterId, out var cluster))
            {
                var missingEntry = new
                {
                    cluster = clusterId,
                    status = "missing"
                };
                clusters.Add(missingEntry);

                if (failOnMissing)
                {
                    failures.Add(new { cluster = clusterId, reason = "cluster_not_found" });
                }

                continue;
            }

            var destinationState = cluster.DestinationsState;
            var availableCount = destinationState?.AvailableDestinations?.Count ?? 0;
            var totalCount = destinationState?.AllDestinations?.Count ?? 0;
            var healthy = availableCount >= minimumAvailable;

            clusters.Add(new
            {
                cluster = clusterId,
                status = healthy ? "ready" : "unhealthy",
                available = availableCount,
                required = minimumAvailable,
                total = totalCount
            });

            if (!healthy)
            {
                failures.Add(new
                {
                    cluster = clusterId,
                    reason = "insufficient_available_destinations",
                    available = availableCount,
                    required = minimumAvailable,
                    total = totalCount
                });
            }
        }

        var details = new
        {
            requiredClusters,
            minimumAvailable,
            failOnMissingCluster = failOnMissing,
            clusters,
            failures
        };

        return Task.FromResult(failures.Count == 0
            ? ReadinessResult.Ready(details)
            : ReadinessResult.NotReady(details));
    }
}
