namespace Dhadgar.Lab.Tools;

/// <summary>
/// The loopback ports the lab's own daemon listeners hold right now (the barrier proxy today; the webhook sink from L36).
/// <c>lab_doctor</c> reads them so a port the daemon itself listens on counts as held by the lab (D-ML69).
/// </summary>
public sealed class LabListenerPorts
{
    private readonly object _gate = new();
    private readonly HashSet<int> _held = [];

    /// <summary>
    /// The ports held right now, as a copy taken under the lock.
    /// </summary>
    public IReadOnlySet<int> Held
    {
        get
        {
            lock (_gate)
            {
                return new HashSet<int>(_held);
            }
        }
    }

    /// <summary>
    /// Adds a port to the held set. Holding a port twice leaves one entry.
    /// </summary>
    public void Hold(int port)
    {
        lock (_gate)
        {
            _held.Add(port);
        }
    }

    /// <summary>
    /// Removes a port from the held set. Releasing a port that is not held is a no-op.
    /// </summary>
    public void Release(int port)
    {
        lock (_gate)
        {
            _held.Remove(port);
        }
    }
}
