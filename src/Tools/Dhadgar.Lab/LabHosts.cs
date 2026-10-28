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

    private static bool IsLoopback(Uri uri) =>
        Array.IndexOf(LoopbackHosts, uri.Host) >= 0;
}
