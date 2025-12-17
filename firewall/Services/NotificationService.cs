using System.Collections.Concurrent;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INotificationService
{
    event EventHandler<NetworkAlert>? AlertReceived;
    Task SendAlertAsync(NetworkAlert alert);
    IEnumerable<NetworkAlert> GetRecentNotifications(int count = 20);
    void ClearNotifications();
    NotificationStats GetStats();
}

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly ConcurrentQueue<NetworkAlert> _recentAlerts = new();
    private readonly ConcurrentDictionary<string, AlertCooldown> _alertCooldowns = new();
    private const int MaxRecentAlerts = 100;

    // Cooldown configuration per alert type (in minutes)
    private static readonly Dictionary<AlertType, int> AlertCooldownMinutes = new()
    {
        { AlertType.NewDevice, 60 * 24 },           // 24 hours - only alert once per new device
        { AlertType.UnknownDevice, 60 },            // 1 hour cooldown
        { AlertType.SuspiciousTraffic, 5 },         // 5 minutes
        { AlertType.PortScan, 15 },                 // 15 minutes
        { AlertType.ArpSpoofing, 1 },               // 1 minute - critical, allow more alerts
        { AlertType.DnsAnomaly, 10 },               // 10 minutes
        { AlertType.HighTrafficVolume, 10 },        // 10 minutes
        { AlertType.MalformedPacket, 30 },          // 30 minutes
        { AlertType.UnauthorizedAccess, 5 },        // 5 minutes
        { AlertType.ManInTheMiddle, 1 },            // 1 minute - critical
        { AlertType.ThreatDetected, 5 },            // 5 minutes
        { AlertType.SecurityVulnerability, 60 },    // 1 hour
        { AlertType.BruteForceAttempt, 5 },         // 5 minutes
        { AlertType.MalwareDetected, 1 },           // 1 minute - critical
        { AlertType.DataExfiltration, 5 },          // 5 minutes
        { AlertType.PolicyViolation, 15 }           // 15 minutes
    };

    // Stats
    private long _totalAlerts;
    private long _suppressedAlerts;
    private long _sentAlerts;

    public event EventHandler<NetworkAlert>? AlertReceived;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        
        // Cleanup timer - remove old cooldowns every 5 minutes
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                CleanupOldCooldowns();
            }
        });
    }

    public Task SendAlertAsync(NetworkAlert alert)
    {
        Interlocked.Increment(ref _totalAlerts);

        // Generate a unique key for this alert type + source
        var cooldownKey = GenerateCooldownKey(alert);

        // Check if we should suppress this alert
        if (ShouldSuppressAlert(cooldownKey, alert.Type))
        {
            Interlocked.Increment(ref _suppressedAlerts);
            _logger.LogDebug("Suppressed duplicate alert: {Type} for {Key}", alert.Type, cooldownKey);
            return Task.CompletedTask;
        }

        // Update cooldown
        UpdateCooldown(cooldownKey, alert);

        Interlocked.Increment(ref _sentAlerts);

        // Add to recent alerts queue
        _recentAlerts.Enqueue(alert);
        
        // Limit queue size
        while (_recentAlerts.Count > MaxRecentAlerts)
        {
            _recentAlerts.TryDequeue(out _);
        }

        // Trigger event for WebSocket/SSE clients
        AlertReceived?.Invoke(this, alert);

        // Log based on severity
        LogAlert(alert);

        return Task.CompletedTask;
    }

    private string GenerateCooldownKey(NetworkAlert alert)
    {
        // Create a unique key based on alert type and relevant identifiers
        var parts = new List<string> { alert.Type.ToString() };

        // Add source identifier (prefer MAC, then IP)
        if (!string.IsNullOrEmpty(alert.SourceMac))
            parts.Add($"mac:{alert.SourceMac}");
        else if (!string.IsNullOrEmpty(alert.SourceIp))
            parts.Add($"ip:{alert.SourceIp}");

        // For port-related alerts, include the port
        if (alert.DestinationPort.HasValue)
            parts.Add($"port:{alert.DestinationPort}");

        // For device-related alerts, include device ID
        if (alert.DeviceId.HasValue)
            parts.Add($"dev:{alert.DeviceId}");

        return string.Join("|", parts);
    }

    private bool ShouldSuppressAlert(string key, AlertType alertType)
    {
        if (!_alertCooldowns.TryGetValue(key, out var cooldown))
            return false;

        var cooldownMinutes = AlertCooldownMinutes.GetValueOrDefault(alertType, 5);
        var cooldownExpiry = cooldown.LastAlertTime.AddMinutes(cooldownMinutes);

        if (DateTime.UtcNow < cooldownExpiry)
        {
            // Still in cooldown period
            cooldown.SuppressedCount++;
            return true;
        }

        return false;
    }

    private void UpdateCooldown(string key, NetworkAlert alert)
    {
        var cooldown = _alertCooldowns.GetOrAdd(key, _ => new AlertCooldown());
        
        cooldown.LastAlertTime = DateTime.UtcNow;
        cooldown.AlertType = alert.Type;
        cooldown.LastTitle = alert.Title;
        cooldown.AlertCount++;
    }

    private void CleanupOldCooldowns()
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _alertCooldowns)
        {
            var maxCooldown = AlertCooldownMinutes.GetValueOrDefault(kvp.Value.AlertType, 5);
            var expiry = kvp.Value.LastAlertTime.AddMinutes(maxCooldown * 2); // Keep for 2x cooldown period

            if (now > expiry)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _alertCooldowns.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired alert cooldowns", keysToRemove.Count);
        }
    }

    public IEnumerable<NetworkAlert> GetRecentNotifications(int count = 20)
    {
        return _recentAlerts.Reverse().Take(count);
    }

    public void ClearNotifications()
    {
        while (_recentAlerts.TryDequeue(out _)) { }
        _alertCooldowns.Clear();
    }

    public NotificationStats GetStats()
    {
        return new NotificationStats
        {
            TotalAlerts = Interlocked.Read(ref _totalAlerts),
            SentAlerts = Interlocked.Read(ref _sentAlerts),
            SuppressedAlerts = Interlocked.Read(ref _suppressedAlerts),
            ActiveCooldowns = _alertCooldowns.Count,
            CooldownDetails = _alertCooldowns.Values
                .GroupBy(c => c.AlertType)
                .ToDictionary(g => g.Key.ToString(), g => new CooldownInfo
                {
                    Count = g.Count(),
                    TotalSuppressed = g.Sum(c => c.SuppressedCount)
                })
        };
    }

    private void LogAlert(NetworkAlert alert)
    {
        var prefix = alert.Severity switch
        {
            AlertSeverity.Critical => "?? [CRITICAL]",
            AlertSeverity.High => "?? [HIGH]",
            AlertSeverity.Medium => "?? [MEDIUM]",
            AlertSeverity.Low => "?? [LOW]",
            _ => "?? [INFO]"
        };

        _logger.LogWarning("{Prefix} {Type}: {Title} - {Message}",
            prefix, alert.Type, alert.Title, alert.Message);
    }

    private class AlertCooldown
    {
        public DateTime LastAlertTime { get; set; } = DateTime.UtcNow;
        public AlertType AlertType { get; set; }
        public string LastTitle { get; set; } = string.Empty;
        public int AlertCount { get; set; }
        public int SuppressedCount { get; set; }
    }
}

public class NotificationStats
{
    public long TotalAlerts { get; set; }
    public long SentAlerts { get; set; }
    public long SuppressedAlerts { get; set; }
    public int ActiveCooldowns { get; set; }
    public Dictionary<string, CooldownInfo> CooldownDetails { get; set; } = new();
}

public class CooldownInfo
{
    public int Count { get; set; }
    public int TotalSuppressed { get; set; }
}
