using System.Collections.Concurrent;

namespace NetworkFirewall.Services;

/// <summary>
/// Service pour les logs en temps réel des scans (caméras, réseau, etc.)
/// </summary>
public interface IScanLogService
{
    event EventHandler<ScanLogEntry>? LogEntryAdded;
    void Log(string source, string message, ScanLogLevel level = ScanLogLevel.Info);
    void LogProgress(string source, string message, int current, int total);
    void StartScan(string scanId, string description);
    void EndScan(string scanId, bool success, string? summary = null);
    IEnumerable<ScanLogEntry> GetRecentLogs(string? source = null, int count = 100);
    ScanStatus? GetScanStatus(string scanId);
    void ClearLogs(string? source = null);
}

public class ScanLogService : IScanLogService
{
    private readonly ConcurrentQueue<ScanLogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, ScanStatus> _activeScans = new();
    private const int MaxLogs = 1000;
    private int _logCounter = 0;

    public event EventHandler<ScanLogEntry>? LogEntryAdded;

    public void Log(string source, string message, ScanLogLevel level = ScanLogLevel.Info)
    {
        var entry = new ScanLogEntry
        {
            Id = Interlocked.Increment(ref _logCounter),
            Source = source,
            Message = message,
            Level = level,
            Timestamp = DateTime.UtcNow
        };

        _logs.Enqueue(entry);

        // Limit queue size
        while (_logs.Count > MaxLogs)
        {
            _logs.TryDequeue(out _);
        }

        // Notify subscribers
        LogEntryAdded?.Invoke(this, entry);
    }

    public void LogProgress(string source, string message, int current, int total)
    {
        var entry = new ScanLogEntry
        {
            Id = Interlocked.Increment(ref _logCounter),
            Source = source,
            Message = message,
            Level = ScanLogLevel.Progress,
            Timestamp = DateTime.UtcNow,
            Progress = new ScanProgress { Current = current, Total = total }
        };

        _logs.Enqueue(entry);

        while (_logs.Count > MaxLogs)
        {
            _logs.TryDequeue(out _);
        }

        LogEntryAdded?.Invoke(this, entry);
    }

    public void StartScan(string scanId, string description)
    {
        var status = new ScanStatus
        {
            ScanId = scanId,
            Description = description,
            StartTime = DateTime.UtcNow,
            IsRunning = true
        };

        _activeScans[scanId] = status;

        Log(scanId, $"?? Démarrage: {description}", ScanLogLevel.Info);
    }

    public void EndScan(string scanId, bool success, string? summary = null)
    {
        if (_activeScans.TryGetValue(scanId, out var status))
        {
            status.IsRunning = false;
            status.EndTime = DateTime.UtcNow;
            status.Success = success;
            status.Summary = summary;
        }

        var level = success ? ScanLogLevel.Success : ScanLogLevel.Error;
        var icon = success ? "?" : "?";
        Log(scanId, $"{icon} Terminé: {summary ?? (success ? "Succès" : "Échec")}", level);
    }

    public IEnumerable<ScanLogEntry> GetRecentLogs(string? source = null, int count = 100)
    {
        var logs = _logs.Reverse();
        
        if (!string.IsNullOrEmpty(source))
        {
            logs = logs.Where(l => l.Source == source);
        }

        return logs.Take(count).ToList();
    }

    public ScanStatus? GetScanStatus(string scanId)
    {
        _activeScans.TryGetValue(scanId, out var status);
        return status;
    }

    public void ClearLogs(string? source = null)
    {
        if (string.IsNullOrEmpty(source))
        {
            while (_logs.TryDequeue(out _)) { }
        }
        else
        {
            // Can't selectively remove from ConcurrentQueue, 
            // so we rebuild without the source
            var remaining = _logs.Where(l => l.Source != source).ToList();
            while (_logs.TryDequeue(out _)) { }
            foreach (var log in remaining)
            {
                _logs.Enqueue(log);
            }
        }
    }
}

public class ScanLogEntry
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ScanLogLevel Level { get; set; }
    public DateTime Timestamp { get; set; }
    public ScanProgress? Progress { get; set; }
}

public class ScanProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public int Percentage => Total > 0 ? (int)((double)Current / Total * 100) : 0;
}

public class ScanStatus
{
    public string ScanId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsRunning { get; set; }
    public bool Success { get; set; }
    public string? Summary { get; set; }
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}

public enum ScanLogLevel
{
    Debug,
    Info,
    Progress,
    Warning,
    Error,
    Success
}
