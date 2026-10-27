using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>
/// The only URLs the driver sends HTTP to (R-LOCALURL): <c>https://localhost:&lt;port&gt;</c> or
/// <c>http://127.0.0.1:&lt;port&gt;</c>, for a port the driver allocated in this run.
/// </summary>
internal static class LocalUrl
{
    /// <summary>Builds a local URL, or fails for any other scheme, host or unallocated port.</summary>
    public static Result<Uri> Create(string scheme, string host, int port, IReadOnlySet<int> allocatedPorts)
    {
        if (!allocatedPorts.Contains(port))
        {
            return Result<Uri>.Failure($"port {port} was not allocated by this run");
        }

        var allowed = (scheme, host) switch
        {
            ("https", "localhost") => true,
            ("http", "127.0.0.1") => true,
            _ => false,
        };

        if (!allowed)
        {
            return Result<Uri>.Failure($"{scheme}://{host} is not a local UAT URL");
        }

        return Result<Uri>.Success(new Uri($"{scheme}://{host}:{port}/", UriKind.Absolute));
    }
}
