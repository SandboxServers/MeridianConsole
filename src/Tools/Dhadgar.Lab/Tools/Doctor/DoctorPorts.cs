using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Tools;

/// <summary>How a loopback port looks to the doctor.</summary>
internal enum PortState
{
    /// <summary>Nothing listens on the port and the OS does not reserve it.</summary>
    Free,

    /// <summary>Something else holds the port (or the probe could not bind it).</summary>
    InUse,

    /// <summary>The OS reserves the port (for example an excluded range); no process can bind it.</summary>
    Reserved,
}

/// <summary>
/// The port half of <c>lab_doctor</c>: the lab's own ports, and the probe and verdict behind them.
/// </summary>
internal static class DoctorPorts
{
    private static readonly Regex LabPublished = new(@"127\.0\.0\.1:(\d+)->", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The ports the ports check covers: every service port and the infrastructure ports, sorted.
    /// </summary>
    public static readonly IReadOnlyList<int> Checked = LabPorts.ServicePorts
        .Concat([LabPorts.Postgres, LabPorts.RabbitMq, LabPorts.RabbitMqManagement, LabPorts.Redis, LabPorts.ToxiproxyApi, LabPorts.Sink])
        .Distinct()
        .Order()
        .ToArray();

    /// <summary>
    /// Probes one loopback port by binding it, then releasing it. <c>AccessDenied</c> means the OS reserves it.
    /// </summary>
    public static PortState Probe(int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            listener.Stop();
            return PortState.Free;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return PortState.Reserved;
        }
        catch (SocketException)
        {
            return PortState.InUse;
        }
    }

    /// <summary>
    /// Reads the host ports that lab containers publish on loopback, from <c>docker ps --format {{.Ports}}</c> output.
    /// </summary>
    public static IReadOnlySet<int> ParseLabPublished(string portsText)
    {
        var owned = new HashSet<int>();
        foreach (Match match in LabPublished.Matches(portsText))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            {
                owned.Add(port);
            }
        }

        return owned;
    }

    /// <summary>
    /// The pure verdict for the ports check. A null <paramref name="labOwned"/> means docker was unreachable.
    /// A port in <paramref name="daemonHeld"/> is held by the lab's own daemon listeners (D-ML69): it is never
    /// reported as in use by something else, and never as owner unknown.
    /// </summary>
    internal static DoctorCheck EvaluatePorts(
        IReadOnlyList<int> ports,
        Func<int, PortState> probe,
        IReadOnlySet<int>? labOwned,
        IReadOnlySet<int>? daemonHeld = null)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(probe);

        var states = ports.Select(port => (Port: port, State: probe(port))).ToList();
        bool IsDaemonHeld(int port) => daemonHeld is not null && daemonHeld.Contains(port);

        var foreign = states
            .Where(s => s.State == PortState.Reserved
                || (s.State == PortState.InUse && labOwned is not null && !labOwned.Contains(s.Port) && !IsDaemonHeld(s.Port)))
            .Select(s => s.State == PortState.Reserved
                ? $"{s.Port} (reserved by the OS)"
                : s.Port.ToString(CultureInfo.InvariantCulture))
            .ToList();
        if (foreign.Count > 0)
        {
            return new DoctorCheck("ports", DoctorStatus.Fail, $"in use by something else: {string.Join(", ", foreign)}");
        }

        // Without docker, an in-use port can only be reported as owner unknown.
        var unknown = labOwned is null
            ? states.Where(s => s.State == PortState.InUse && !IsDaemonHeld(s.Port)).Select(s => s.Port.ToString(CultureInfo.InvariantCulture)).ToList()
            : [];
        if (unknown.Count > 0)
        {
            return new DoctorCheck("ports", DoctorStatus.Warn, $"in use, owner unknown (docker unreachable): {string.Join(", ", unknown)}");
        }

        int free = states.Count(s => s.State == PortState.Free);
        int held = states.Count(s => s.State == PortState.InUse);
        return new DoctorCheck("ports", DoctorStatus.Ok, $"{free} free, {held} held by the lab");
    }
}
