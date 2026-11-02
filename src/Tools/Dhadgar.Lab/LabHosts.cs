namespace Dhadgar.Lab;

/// <summary>
/// The only places the lab may send HTTP. Anything else is refused as <c>[Lab.Untrusted]</c> by the caller.
/// </summary>
public static class LabHosts
{
    private static readonly string[] LoopbackHosts = ["127.0.0.1", "localhost", "[::1]", "::1"];

    /// <summary>
    /// True only for http/https, a loopback host, and a port in <see cref="LabPorts.ServicePorts"/>.
    /// </summary>
    public static bool IsLabEndpoint(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return IsLoopback(uri) && LabPorts.ServicePorts.Contains(uri.Port);
    }

    /// <summary>
    /// True only for http, a loopback host, and a port in <see cref="LabPorts.InfraPorts"/>.
    /// </summary>
    public static bool IsLabInfraEndpoint(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        return IsLoopback(uri) && LabPorts.InfraPorts.Contains(uri.Port);
    }

    /// <summary>
    /// True only for http, a loopback host, and <see cref="LabPorts.Sink"/>. The webhook sink is the one infra endpoint a lab
    /// service's readiness check and <c>http_request via=sink</c> may reach (D-ML147); the other infra ports stay out.
    /// </summary>
    public static bool IsSinkEndpoint(Uri uri) => IsLabInfraEndpoint(uri) && uri.Port == LabPorts.Sink;

    /// <summary>
    /// True only for http, a loopback host, and <see cref="LabDefaults.DaemonPort"/>. The CLI's loopback calls (<c>status</c>, <c>uat run</c>)
    /// and the self-check recovery's calls to its child daemon use this; <see cref="IsLabEndpoint"/> is unchanged.
    /// </summary>
    public static bool IsDaemonEndpoint(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        return IsLoopback(uri) && uri.Port == LabDefaults.DaemonPort;
    }

    private static bool IsLoopback(Uri uri) =>
        Array.IndexOf(LoopbackHosts, uri.Host) >= 0;
}
