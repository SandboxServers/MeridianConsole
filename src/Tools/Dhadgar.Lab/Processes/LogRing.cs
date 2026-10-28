namespace Dhadgar.Lab.Processes;

/// <summary>
/// One captured log line. The message and category are already redacted when the line reaches the ring.
/// </summary>
public sealed record LogLine(DateTimeOffset Timestamp, string Level, string Category, int? EventId, string Message);

/// <summary>
/// A bounded, thread-safe ring of captured lines for one process. The oldest line is dropped first.
/// </summary>
public sealed class LogRing
{
    /// <summary>
    /// The per-process capacity from L07 (20 000 lines).
    /// </summary>
    public const int DefaultCapacity = 20_000;

    private readonly LogLine?[] _items;
    private readonly object _gate = new();
    private int _next;
    private int _count;

    /// <summary>
    /// Creates a ring with the given capacity.
    /// </summary>
    public LogRing(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _items = new LogLine?[capacity];
    }

    /// <summary>
    /// Appends a line, dropping the oldest when full.
    /// </summary>
    public void Add(LogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            _items[_next] = line;
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// A copy of the held lines, oldest first.
    /// </summary>
    public IReadOnlyList<LogLine> Snapshot()
    {
        lock (_gate)
        {
            var result = new List<LogLine>(_count);
            int start = _count < _items.Length ? 0 : _next;
            for (int i = 0; i < _count; i++)
            {
                result.Add(_items[(start + i) % _items.Length]!);
            }

            return result;
        }
    }
}
