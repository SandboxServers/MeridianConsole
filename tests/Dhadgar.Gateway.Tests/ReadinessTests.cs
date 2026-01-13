using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Gateway.Options;
using Dhadgar.Gateway.Readiness;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Model;

namespace Dhadgar.Gateway.Tests;

public class ReadinessTests
{
    [Fact]
    public async Task ReadinessFailsWhenRequiredClusterMissing()
    {
        var proxyStateLookup = new TestProxyStateLookup(new Dictionary<string, ClusterState>());
        var options = Microsoft.Extensions.Options.Options.Create(new ReadyzOptions
        {
            RequiredClusters = ["identity"],
            MinimumAvailableDestinations = 1,
            FailOnMissingCluster = true
        });

        var check = new YarpReadinessCheck(proxyStateLookup, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task ReadinessSucceedsWhenMissingClustersAreIgnored()
    {
        var proxyStateLookup = new TestProxyStateLookup(new Dictionary<string, ClusterState>());
        var options = Microsoft.Extensions.Options.Options.Create(new ReadyzOptions
        {
            RequiredClusters = ["identity"],
            MinimumAvailableDestinations = 1,
            FailOnMissingCluster = false
        });

        var check = new YarpReadinessCheck(proxyStateLookup, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReadinessFailsWhenAvailableDestinationsBelowMinimum()
    {
        var cluster = BuildCluster("identity", all: 1, available: 0);
        var proxyStateLookup = new TestProxyStateLookup(new Dictionary<string, ClusterState>
        {
            ["identity"] = cluster
        });
        var options = Microsoft.Extensions.Options.Options.Create(new ReadyzOptions
        {
            RequiredClusters = ["identity"],
            MinimumAvailableDestinations = 1,
            FailOnMissingCluster = true
        });

        var check = new YarpReadinessCheck(proxyStateLookup, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ReadinessSucceedsWhenAvailableDestinationsMeetMinimum()
    {
        var cluster = BuildCluster("identity", all: 2, available: 1);
        var proxyStateLookup = new TestProxyStateLookup(new Dictionary<string, ClusterState>
        {
            ["identity"] = cluster
        });
        var options = Microsoft.Extensions.Options.Options.Create(new ReadyzOptions
        {
            RequiredClusters = ["identity"],
            MinimumAvailableDestinations = 1,
            FailOnMissingCluster = true
        });

        var check = new YarpReadinessCheck(proxyStateLookup, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static ClusterState BuildCluster(string clusterId, int all, int available)
    {
        var allDestinations = Enumerable.Range(0, all)
            .Select(index => new DestinationState($"d{index}"))
            .ToArray();
        var availableDestinations = allDestinations.Take(available).ToArray();

        var cluster = new ClusterState(clusterId)
        {
            DestinationsState = new ClusterDestinationsState(allDestinations, availableDestinations)
        };

        return cluster;
    }

    private sealed class TestProxyStateLookup : IProxyStateLookup
    {
        private readonly Dictionary<string, ClusterState> _clusters;

        public TestProxyStateLookup(Dictionary<string, ClusterState> clusters)
        {
            _clusters = clusters;
        }

        public bool TryGetRoute(string routeId, out RouteModel route)
        {
            route = null!;
            return false;
        }

        public IEnumerable<RouteModel> GetRoutes() => [];

        public bool TryGetCluster(string clusterId, out ClusterState cluster)
        {
            return _clusters.TryGetValue(clusterId, out cluster!);
        }

        public IEnumerable<ClusterState> GetClusters() => _clusters.Values;
    }
}
