namespace Dhadgar.Lab.Faults;

/// <summary>
/// One Toxiproxy proxy from the C10 table. <see cref="Packet"/> names the packet that creates it.
/// </summary>
/// <param name="Name">The proxy name; the target id is <c>p:&lt;Name&gt;</c>.</param>
/// <param name="ListenPort">The port the proxy listens on inside the Toxiproxy container.</param>
/// <param name="Upstream">The upstream address inside the lab network.</param>
/// <param name="Packet">The packet that owns the proxy.</param>
public sealed record ProxyDefinition(string Name, int ListenPort, string Upstream, string Packet);

/// <summary>
/// The C10 proxy table as static data. <c>LabStack</c> creates <c>StackProxies</c>.
/// </summary>
public static class ProxyCatalog
{
    /// <summary>
    /// Every proxy in C10, in table order.
    /// </summary>
    public static IReadOnlyList<ProxyDefinition> All { get; } =
    [
        new("gateway-redis", 20010, "meridian-lab-redis:6379", "L08"),
        new("gateway-identity", 20011, "meridian-lab-identity:8080", "L34"),
        new("gateway-nodes", 20012, "meridian-lab-nodes:8080", "L08"),
        new("gateway-betterauth", 20013, "meridian-lab-betterauth:8080", "L34"),
        new("gateway-notifications", 20014, "meridian-lab-notifications:8080", "L36"),
        new("gateway-discord", 20015, "meridian-lab-discord:8080", "L36"),
        new("nodes-postgres", 20020, "meridian-lab-postgres:5432", "L08"),
        new("nodes-rabbitmq", 20021, "meridian-lab-rabbitmq:5672", "L08"),
        new("identity-postgres", 20030, "meridian-lab-postgres:5432", "L34"),
        new("identity-redis", 20031, "meridian-lab-redis:6379", "L34"),
        new("identity-rabbitmq", 20032, "meridian-lab-rabbitmq:5672", "L34"),
        new("betterauth-postgres", 20040, "meridian-lab-postgres:5432", "L34"),
        new("notifications-postgres", 20050, "meridian-lab-postgres:5432", "L36"),
        new("notifications-rabbitmq", 20051, "meridian-lab-rabbitmq:5672", "L36"),
        new("notifications-webhook", 20052, "meridian-lab-sink:8080", "L36"),
        new("discord-postgres", 20060, "meridian-lab-postgres:5432", "L36"),
        new("discord-webhook", 20061, "meridian-lab-sink:8080", "L36"),
        new("discord-rabbitmq", 20062, "meridian-lab-rabbitmq:5672", "L36"),
        new("agent-ingress", 25000, "meridian-lab-gateway:8080", "L08"),
        new("agent-ingress-tls", 25443, "meridian-lab-gateway:8443", "L41"),
    ];

    /// <summary>
    /// The proxies <c>stack_up</c> creates: every row except L41's agent-ingress-tls, which L41 makes live with the Gateway's
    /// 8443 listener (D-ML150). <see cref="ProxyDefinition.Packet"/> names who first needs a row, not who creates it.
    /// </summary>
    public static IEnumerable<ProxyDefinition> StackProxies =>
        All.Where(proxy => !string.Equals(proxy.Packet, "L41", StringComparison.Ordinal));

    /// <summary>
    /// The proxies owned by one packet.
    /// </summary>
    public static IEnumerable<ProxyDefinition> ForPacket(string packet) =>
        All.Where(proxy => string.Equals(proxy.Packet, packet, StringComparison.Ordinal));

    /// <summary>
    /// The proxy with this name, or null.
    /// </summary>
    public static ProxyDefinition? Find(string name) =>
        All.FirstOrDefault(proxy => string.Equals(proxy.Name, name, StringComparison.Ordinal));
}
