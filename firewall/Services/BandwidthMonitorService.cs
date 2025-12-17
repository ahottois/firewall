using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IBandwidthMonitorService
{
    void RecordTraffic(string macAddress, string? ipAddress, long bytes, bool isInbound);
    BandwidthStats GetDeviceBandwidth(string macAddress);
    IEnumerable<BandwidthStats> GetAllDevicesBandwidth();
    IEnumerable<BandwidthStats> GetTopConsumers(int count = 10);
    NetworkBandwidthSummary GetNetworkSummary();
    Task CheckBandwidthAlertsAsync();
}

public class BandwidthMonitorService : IBandwidthMonitorService, IDisposable
{
    private readonly ILogger<BandwidthMonitorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly AppSettings _settings;
    
    private readonly ConcurrentDictionary<string, DeviceBandwidthTracker> _deviceTrackers = new();
    private readonly Timer _alertCheckTimer;
    private readonly Timer _cleanupTimer;
    
    // Network totals
    private long _totalBytesIn;
    private long _totalBytesOut;
    private long _totalPacketsIn;
    private long _totalPacketsOut;
    private DateTime _monitoringStartTime = DateTime.UtcNow;

    // Alert thresholds (configurable)
    private readonly long _highBandwidthThresholdBytesPerSec = 10_000_000; // 10 MB/s
    private readonly long _dailyQuotaBytes = 10_000_000_000; // 10 GB daily quota
    private readonly ConcurrentDictionary<string, DateTime> _alertCooldowns = new();

    public BandwidthMonitorService(
        ILogger<BandwidthMonitorService> logger,
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _settings = settings.Value;

        // Check for bandwidth alerts every minute
        _alertCheckTimer = new Timer(async _ => await CheckBandwidthAlertsAsync(), null, 
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        // Cleanup old data every hour
        _cleanupTimer = new Timer(_ => CleanupOldData(), null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public void RecordTraffic(string macAddress, string? ipAddress, long bytes, bool isInbound)
    {
        var tracker = _deviceTrackers.GetOrAdd(macAddress, _ => new DeviceBandwidthTracker
        {
            MacAddress = macAddress,
            IpAddress = ipAddress
        });

        tracker.IpAddress = ipAddress ?? tracker.IpAddress;
        tracker.LastActivity = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        lock (tracker)
        {
            if (isInbound)
            {
                tracker.TotalBytesIn += bytes;
                tracker.PacketsIn++;
                Interlocked.Add(ref _totalBytesIn, bytes);
                Interlocked.Increment(ref _totalPacketsIn);
            }
            else
            {
                tracker.TotalBytesOut += bytes;
                tracker.PacketsOut++;
                Interlocked.Add(ref _totalBytesOut, bytes);
                Interlocked.Increment(ref _totalPacketsOut);
            }

            // Track per-minute stats
            if (!tracker.MinuteStats.TryGetValue(currentMinute, out var minuteStat))
            {
                minuteStat = new MinuteStat();
                tracker.MinuteStats[currentMinute] = minuteStat;
            }

            if (isInbound)
            {
                minuteStat.BytesIn += bytes;
                minuteStat.PacketsIn++;
            }
            else
            {
                minuteStat.BytesOut += bytes;
                minuteStat.PacketsOut++;
            }

            // Keep only last 60 minutes
            var cutoff = currentMinute.AddMinutes(-60);
            var oldMinutes = tracker.MinuteStats.Keys.Where(k => k < cutoff).ToList();
            foreach (var old in oldMinutes)
            {
                tracker.MinuteStats.Remove(old);
            }

            // Track hourly stats
            var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            if (!tracker.HourlyStats.TryGetValue(currentHour, out var hourlyStat))
            {
                hourlyStat = new HourlyStat();
                tracker.HourlyStats[currentHour] = hourlyStat;
            }

            if (isInbound)
                hourlyStat.BytesIn += bytes;
            else
                hourlyStat.BytesOut += bytes;

            // Keep only last 24 hours
            var hourCutoff = currentHour.AddHours(-24);
            var oldHours = tracker.HourlyStats.Keys.Where(k => k < hourCutoff).ToList();
            foreach (var old in oldHours)
            {
                tracker.HourlyStats.Remove(old);
            }

            // Track daily stats
            var today = now.Date;
            if (!tracker.DailyStats.TryGetValue(today, out var dailyStat))
            {
                dailyStat = new DailyStat();
                tracker.DailyStats[today] = dailyStat;
            }

            if (isInbound)
                dailyStat.BytesIn += bytes;
            else
                dailyStat.BytesOut += bytes;

            // Keep only last 30 days
            var dayCutoff = today.AddDays(-30);
            var oldDays = tracker.DailyStats.Keys.Where(k => k < dayCutoff).ToList();
            foreach (var old in oldDays)
            {
                tracker.DailyStats.Remove(old);
            }
        }
    }

    public BandwidthStats GetDeviceBandwidth(string macAddress)
    {
        if (!_deviceTrackers.TryGetValue(macAddress, out var tracker))
        {
            return new BandwidthStats { MacAddress = macAddress };
        }

        return CalculateStats(tracker);
    }

    public IEnumerable<BandwidthStats> GetAllDevicesBandwidth()
    {
        return _deviceTrackers.Values.Select(CalculateStats).OrderByDescending(s => s.TotalBytes);
    }

    public IEnumerable<BandwidthStats> GetTopConsumers(int count = 10)
    {
        return GetAllDevicesBandwidth().Take(count);
    }

    public NetworkBandwidthSummary GetNetworkSummary()
    {
        var uptime = DateTime.UtcNow - _monitoringStartTime;
        var totalBytesIn = Interlocked.Read(ref _totalBytesIn);
        var totalBytesOut = Interlocked.Read(ref _totalBytesOut);

        return new NetworkBandwidthSummary
        {
            MonitoringStartTime = _monitoringStartTime,
            UptimeSeconds = (long)uptime.TotalSeconds,
            TotalBytesIn = totalBytesIn,
            TotalBytesOut = totalBytesOut,
            TotalBytes = totalBytesIn + totalBytesOut,
            TotalPacketsIn = Interlocked.Read(ref _totalPacketsIn),
            TotalPacketsOut = Interlocked.Read(ref _totalPacketsOut),
            AverageBytesPerSecIn = uptime.TotalSeconds > 0 ? (long)(totalBytesIn / uptime.TotalSeconds) : 0,
            AverageBytesPerSecOut = uptime.TotalSeconds > 0 ? (long)(totalBytesOut / uptime.TotalSeconds) : 0,
            ActiveDevices = _deviceTrackers.Count(t => t.Value.LastActivity > DateTime.UtcNow.AddMinutes(-5)),
            TotalDevices = _deviceTrackers.Count,
            TopConsumers = GetTopConsumers(5).ToList(),
            HourlyHistory = GetNetworkHourlyHistory()
        };
    }

    private List<HourlyBandwidth> GetNetworkHourlyHistory()
    {
        var history = new Dictionary<DateTime, HourlyBandwidth>();
        
        foreach (var tracker in _deviceTrackers.Values)
        {
            foreach (var (hour, stat) in tracker.HourlyStats)
            {
                if (!history.TryGetValue(hour, out var hourly))
                {
                    hourly = new HourlyBandwidth { Hour = hour };
                    history[hour] = hourly;
                }
                hourly.BytesIn += stat.BytesIn;
                hourly.BytesOut += stat.BytesOut;
            }
        }

        return history.Values.OrderBy(h => h.Hour).ToList();
    }

    private BandwidthStats CalculateStats(DeviceBandwidthTracker tracker)
    {
        var now = DateTime.UtcNow;
        var lastMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(-1);
        var lastHour = now.AddHours(-1);
        var today = now.Date;

        long bytesLastMinuteIn = 0, bytesLastMinuteOut = 0;
        long bytesLastHourIn = 0, bytesLastHourOut = 0;
        long bytesTodayIn = 0, bytesTodayOut = 0;

        lock (tracker)
        {
            // Last minute
            if (tracker.MinuteStats.TryGetValue(lastMinute, out var minuteStat))
            {
                bytesLastMinuteIn = minuteStat.BytesIn;
                bytesLastMinuteOut = minuteStat.BytesOut;
            }

            // Last hour
            foreach (var (minute, stat) in tracker.MinuteStats.Where(s => s.Key >= now.AddMinutes(-60)))
            {
                bytesLastHourIn += stat.BytesIn;
                bytesLastHourOut += stat.BytesOut;
            }

            // Today
            if (tracker.DailyStats.TryGetValue(today, out var dailyStat))
            {
                bytesTodayIn = dailyStat.BytesIn;
                bytesTodayOut = dailyStat.BytesOut;
            }
        }

        return new BandwidthStats
        {
            MacAddress = tracker.MacAddress,
            IpAddress = tracker.IpAddress,
            TotalBytesIn = tracker.TotalBytesIn,
            TotalBytesOut = tracker.TotalBytesOut,
            TotalBytes = tracker.TotalBytesIn + tracker.TotalBytesOut,
            TotalPacketsIn = tracker.PacketsIn,
            TotalPacketsOut = tracker.PacketsOut,
            BytesPerSecIn = bytesLastMinuteIn / 60,
            BytesPerSecOut = bytesLastMinuteOut / 60,
            BytesLastHourIn = bytesLastHourIn,
            BytesLastHourOut = bytesLastHourOut,
            BytesTodayIn = bytesTodayIn,
            BytesTodayOut = bytesTodayOut,
            LastActivity = tracker.LastActivity,
            HourlyHistory = tracker.HourlyStats
                .OrderBy(h => h.Key)
                .Select(h => new HourlyBandwidth
                {
                    Hour = h.Key,
                    BytesIn = h.Value.BytesIn,
                    BytesOut = h.Value.BytesOut
                })
                .ToList()
        };
    }

    public async Task CheckBandwidthAlertsAsync()
    {
        try
        {
            foreach (var (mac, tracker) in _deviceTrackers)
            {
                // Skip if device is not active
                if (tracker.LastActivity < DateTime.UtcNow.AddMinutes(-5))
                    continue;

                var stats = CalculateStats(tracker);

                // Check for high bandwidth usage
                var currentBandwidth = stats.BytesPerSecIn + stats.BytesPerSecOut;
                if (currentBandwidth > _highBandwidthThresholdBytesPerSec)
                {
                    await SendBandwidthAlertAsync(mac, stats, "High bandwidth usage detected",
                        $"Device is using {FormatBytes(currentBandwidth)}/s");
                }

                // Check daily quota
                var todayTotal = stats.BytesTodayIn + stats.BytesTodayOut;
                if (todayTotal > _dailyQuotaBytes * 0.9) // Alert at 90%
                {
                    await SendBandwidthAlertAsync(mac, stats, "Daily bandwidth quota almost reached",
                        $"Device has used {FormatBytes(todayTotal)} today (90% of quota)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking bandwidth alerts");
        }
    }

    private async Task SendBandwidthAlertAsync(string mac, BandwidthStats stats, string title, string details)
    {
        // Check cooldown (don't spam alerts)
        var alertKey = $"{mac}:{title}";
        if (_alertCooldowns.TryGetValue(alertKey, out var lastAlert) && 
            lastAlert > DateTime.UtcNow.AddMinutes(-30))
        {
            return;
        }
        _alertCooldowns[alertKey] = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            var device = await deviceRepo.GetByMacAddressAsync(mac);

            var alert = new NetworkAlert
            {
                Type = AlertType.HighTrafficVolume,
                Severity = AlertSeverity.Medium,
                Title = title,
                Message = $"{details}. IP: {stats.IpAddress ?? "Unknown"}, Total today: {FormatBytes(stats.BytesTodayIn + stats.BytesTodayOut)}",
                SourceMac = mac,
                SourceIp = stats.IpAddress,
                DeviceId = device?.Id
            };

            await alertRepo.AddAsync(alert);
            await _notificationService.SendAlertAsync(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending bandwidth alert");
        }
    }

    private void CleanupOldData()
    {
        // Remove inactive devices after 7 days
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var toRemove = _deviceTrackers
            .Where(t => t.Value.LastActivity < cutoff)
            .Select(t => t.Key)
            .ToList();

        foreach (var mac in toRemove)
        {
            _deviceTrackers.TryRemove(mac, out _);
        }

        // Cleanup alert cooldowns
        var alertCutoff = DateTime.UtcNow.AddHours(-1);
        var oldAlerts = _alertCooldowns.Where(a => a.Value < alertCutoff).Select(a => a.Key).ToList();
        foreach (var key in oldAlerts)
        {
            _alertCooldowns.TryRemove(key, out _);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        _alertCheckTimer?.Dispose();
        _cleanupTimer?.Dispose();
    }
}

// Tracker classes
public class DeviceBandwidthTracker
{
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public long TotalBytesIn { get; set; }
    public long TotalBytesOut { get; set; }
    public long PacketsIn { get; set; }
    public long PacketsOut { get; set; }
    public DateTime LastActivity { get; set; }
    public Dictionary<DateTime, MinuteStat> MinuteStats { get; } = new();
    public Dictionary<DateTime, HourlyStat> HourlyStats { get; } = new();
    public Dictionary<DateTime, DailyStat> DailyStats { get; } = new();
}

public class MinuteStat
{
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public long PacketsIn { get; set; }
    public long PacketsOut { get; set; }
}

public class HourlyStat
{
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
}

public class DailyStat
{
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
}

// Output models
public class BandwidthStats
{
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public long TotalBytesIn { get; set; }
    public long TotalBytesOut { get; set; }
    public long TotalBytes { get; set; }
    public long TotalPacketsIn { get; set; }
    public long TotalPacketsOut { get; set; }
    public long BytesPerSecIn { get; set; }
    public long BytesPerSecOut { get; set; }
    public long BytesLastHourIn { get; set; }
    public long BytesLastHourOut { get; set; }
    public long BytesTodayIn { get; set; }
    public long BytesTodayOut { get; set; }
    public DateTime LastActivity { get; set; }
    public List<HourlyBandwidth> HourlyHistory { get; set; } = new();
}

public class HourlyBandwidth
{
    public DateTime Hour { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
}

public class NetworkBandwidthSummary
{
    public DateTime MonitoringStartTime { get; set; }
    public long UptimeSeconds { get; set; }
    public long TotalBytesIn { get; set; }
    public long TotalBytesOut { get; set; }
    public long TotalBytes { get; set; }
    public long TotalPacketsIn { get; set; }
    public long TotalPacketsOut { get; set; }
    public long AverageBytesPerSecIn { get; set; }
    public long AverageBytesPerSecOut { get; set; }
    public int ActiveDevices { get; set; }
    public int TotalDevices { get; set; }
    public List<BandwidthStats> TopConsumers { get; set; } = new();
    public List<HourlyBandwidth> HourlyHistory { get; set; } = new();
}
