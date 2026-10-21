namespace LandmarkLocal;

/// <summary>
/// Waiting progress: log once when the wait message changes, then a heartbeat with elapsed time.
/// Optional live status ticks (for the UI banner) can fire every call without flooding the log.
/// </summary>
public sealed class ProgressThrottle
{
    readonly IProgress<string>? _log;
    readonly Action<string>? _status;
    readonly TimeSpan _heartbeat;
    string? _lastBase;
    DateTime _lastLogUtc = DateTime.MinValue;

    public ProgressThrottle(IProgress<string>? log, TimeSpan? heartbeat = null, Action<string>? status = null)
    {
        _log = log;
        _status = status;
        _heartbeat = heartbeat ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>Legacy: report a changing message once, then heartbeats without elapsed.</summary>
    public void Report(string message) => ReportWaiting(message, startedUtc: null);

    /// <summary>
    /// Live status every call; activity log on first sighting and every heartbeat, with optional elapsed.
    /// </summary>
    public void ReportWaiting(string message, DateTime? startedUtc, string? firstTip = null)
    {
        string elapsed = startedUtc is DateTime t ? FormatElapsed(DateTime.UtcNow - t) : "";
        string withTime = elapsed.Length == 0 ? message : $"{message} ({elapsed})";
        _status?.Invoke(withTime);

        if (_log == null)
            return;

        DateTime now = DateTime.UtcNow;
        bool changed = !string.Equals(message, _lastBase, StringComparison.Ordinal);
        if (changed)
        {
            _lastBase = message;
            _lastLogUtc = now;
            _log.Report(withTime);
            if (!string.IsNullOrEmpty(firstTip))
                _log.Report(firstTip);
            return;
        }

        if (now - _lastLogUtc >= _heartbeat)
        {
            _lastLogUtc = now;
            _log.Report("Still " + LowerFirst(withTime));
        }
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
    }

    static string LowerFirst(string s)
        => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
