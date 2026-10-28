namespace Dhadgar.Lab;

/// <summary>
/// Host-side ports. Service ports are observed directly; faults go through Toxiproxy proxies only where noted.
/// </summary>
public static class LabPorts
{
    // Services (host side, observation path; C10)
    public const int Gateway = 15000;
    public const int GatewayTls = 15443;          // L41
    public const int Identity = 15010;            // L34
    public const int Nodes = 15040;
    public const int Notifications = 15090;       // L36
    public const int Discord = 15120;             // L36
    public const int BetterAuth = 15130;          // L34
    public const int Panel = 14321;               // L34
    public const int AgentIngress = 25000;        // Toxiproxy agent-ingress -> gateway
    public const int AgentIngressTls = 25443;     // L41
    public const int Barrier = 25080;             // L49: in-daemon lab-only HTTP barrier proxy -> AgentIngress (C10)
    // Infrastructure (host side)
    public const int Postgres = 15432;
    public const int RabbitMq = 15673;
    public const int RabbitMqManagement = 16672;  // L21
    public const int Redis = 16379;
    public const int ToxiproxyApi = 18474;
    public const int Sink = 18090;                // L36
    public static readonly IReadOnlySet<int> ServicePorts = new HashSet<int>
        { Gateway, GatewayTls, Identity, Nodes, Notifications, Discord, BetterAuth, Panel, AgentIngress, AgentIngressTls, Barrier };
    public static readonly IReadOnlySet<int> InfraPorts = new HashSet<int>
        { RabbitMqManagement, ToxiproxyApi, Sink };     // HTTP observation endpoints only
}
