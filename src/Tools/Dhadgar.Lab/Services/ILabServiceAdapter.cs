using System.Text;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Values an adapter may read when it composes a service environment. Connection strings are resolved by the caller
/// (through <c>LabConnectionStrings.ServiceDb</c>), so an adapter never touches the vault itself.
/// </summary>
/// <param name="BuildId">Build the container runs (<c>build8</c> or dirty form).</param>
/// <param name="ConnectionStrings">Resolved connection strings keyed by service name (for example <c>nodes</c>).</param>
/// <param name="CaStateDirectory">Host folder for Nodes CA state (<c>LabPaths.State\nodes-ca</c>).</param>
/// <param name="RabbitMqUsername">RabbitMQ user for lab services.</param>
/// <param name="RabbitMqPassword">RabbitMQ password for lab services; never logged.</param>
/// <param name="AdminApiKey">Admin API key for services that authenticate admin routes (L36); from the vault, never logged. Null when the service has none.</param>
public sealed record LabServiceContext(
    string BuildId,
    IReadOnlyDictionary<string, string> ConnectionStrings,
    string CaStateDirectory,
    string RabbitMqUsername,
    string RabbitMqPassword,
    string? AdminApiKey = null)
{
    /// <summary>
    /// Prints the build, the connection-string keys (never their values) and the non-secret fields. Both passwords
    /// print as <c>[redacted]</c>, so <c>ToString()</c> and a failed assert never show them.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BuildId = ").Append(BuildId)
            .Append(", ConnectionStrings = [").Append(string.Join(", ", ConnectionStrings.Keys)).Append(']')
            .Append(", CaStateDirectory = ").Append(CaStateDirectory)
            .Append(", RabbitMqUsername = ").Append(RabbitMqUsername)
            .Append(", RabbitMqPassword = [redacted]")
            .Append(", AdminApiKey = [redacted]");
        return true;
    }
}

/// <summary>
/// One lab-managed service: its container identity, port and the environment it needs behind the proxies (L09).
/// </summary>
public interface ILabServiceAdapter
{
    /// <summary>Service name used in tools (<c>gateway</c>, <c>nodes</c>).</summary>
    string Name { get; }

    /// <summary>Docker container name (<c>meridian-lab-&lt;service&gt;</c>).</summary>
    string Container { get; }

    /// <summary>Build project the image is built from (<c>gateway</c>, <c>nodes</c>).</summary>
    string Project { get; }

    /// <summary>Loopback host port that maps to container port 8080.</summary>
    int HostPort { get; }

    /// <summary>Toxiproxy proxy names this service depends on (C10 rows).</summary>
    IReadOnlyList<string> Proxies { get; }

    /// <summary>Path polled on the loopback host port until it answers 200.</summary>
    string HealthPath { get; }

    /// <summary>Status that means the process is up and answering. Most services answer 200; a service may name another.</summary>
    System.Net.HttpStatusCode ReadyStatusCode => System.Net.HttpStatusCode.OK;

    /// <summary>Config keys the overlay may set for this service (C4 allowlist).</summary>
    IReadOnlySet<string> OverlayKeys { get; }

    /// <summary>Composes the container environment. Fails with <c>[Lab.InvalidArgument]</c> when a required value is missing.</summary>
    Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx);
}
