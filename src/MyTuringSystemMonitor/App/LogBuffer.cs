namespace MyTuringSystemMonitor.App;

public sealed record LogEntry(long Seq, DateTime Time, string Message);

/// <summary>
/// Thread-safe ring buffer of recent log lines, so the web UI can tail them. Every line is also
/// forwarded to an optional sink (console, ILogger, …).
/// </summary>
public sealed class LogBuffer
{
    private readonly LogEntry[] _entries;
    private readonly Action<string>? _sink;
    private readonly object _gate = new();
    private long _nextSeq = 1;
    private int _start;
    private int _count;

    public LogBuffer(int capacity = 500, Action<string>? sink = null)
    {
        _entries = new LogEntry[Math.Max(1, capacity)];
        _sink = sink;
    }

    public void Write(string message)
    {
        LogEntry entry;
        lock (_gate)
        {
            entry = new LogEntry(_nextSeq++, DateTime.Now, message);
            var index = (_start + _count) % _entries.Length;
            _entries[index] = entry;
            if (_count < _entries.Length) _count++;
            else _start = (_start + 1) % _entries.Length;
        }

        try { _sink?.Invoke(message); } catch { /* a broken sink must not break logging */ }
    }

    /// <summary>Entries with a sequence number greater than <paramref name="afterSeq"/>, oldest first.</summary>
    public IReadOnlyList<LogEntry> Since(long afterSeq = 0)
    {
        lock (_gate)
        {
            var result = new List<LogEntry>(_count);
            for (var i = 0; i < _count; i++)
            {
                var e = _entries[(_start + i) % _entries.Length];
                if (e.Seq > afterSeq) result.Add(e);
            }
            return result;
        }
    }
}
