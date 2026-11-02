using System.Globalization;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// Checks a Toxiproxy listener from inside the lab network (D-ML149). Catalog listen ports are not published to the host,
/// so a host TCP connect proves nothing. pg_isready runs in the lab Postgres container against the proxy.
/// </summary>
internal static class ProxyListenerProbe
{
    internal static IReadOnlyList<string> Args(string proxyName)
    {
        ProxyDefinition proxy = ProxyCatalog.Find(proxyName)
            ?? throw new InvalidOperationException($"Proxy {proxyName} is not in the C10 catalog.");
        if (!proxy.Upstream.StartsWith("meridian-lab-postgres:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Proxy {proxyName} does not front Postgres; ProxyListenerProbe only checks Postgres listeners.");
        }

        return
        [
            "exec", $"{LabDefaults.ContainerPrefix}postgres",
            "pg_isready", "-h", $"{LabDefaults.ContainerPrefix}toxiproxy", "-p", proxy.ListenPort.ToString(CultureInfo.InvariantCulture),
            "-U", "postgres", "-t", "2",
        ];
    }

    /// <summary>True when pg_isready exits 0 through the proxy. A Docker call failure is a test failure, not "closed".</summary>
    internal static async Task<bool> AcceptsAsync(IDockerCli docker, string proxyName, CancellationToken ct)
    {
        Result<DockerOutput> run = await docker.RunAsync(DockerEngine.Linux, Args(proxyName), ct);
        Assert.True(run.IsSuccess, run.IsFailure ? run.Error : string.Empty);
        return run.Value!.ExitCode == 0;
    }
}
