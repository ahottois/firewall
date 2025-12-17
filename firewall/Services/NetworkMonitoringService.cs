using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// ????? Arrr! Service de monitoring réseau avancé - Pour surveiller les mers numériques!
/// </summary>
public interface INetworkMonitoringService
{
    NetworkHealthStatus GetNetworkHealth();
    IEnumerable<ConnectionInfo> GetActiveConnections();
    IEnumerable<ProtocolStats> GetProtocolBreakdown();
    IEnumerable<HourlyTrafficStats> GetHourlyTraffic(int hours = 24);
    IEnumerable<TopTalker> GetTopTalkers(int count = 10);
    IEnumerable<SuspiciousActivity> GetSuspiciousActivities(int count = 50);
    GeoTrafficStats GetGeoTrafficStats();
    void RecordConnection(PacketCapturedEventArgs packet);
    LiveNetworkMetrics GetLiveMetrics();
}

public class NetworkMonitoringService : INetworkMonitoringService
{
    private readonly ILogger<NetworkMonitoringService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThreatIntelligenceService _threatIntelligence;
    private readonly IBandwidthMonitorService _bandwidthMonitor;
    private readonly AppSettings _settings;

    // ????? Treasure maps for tracking network activity
    private readonly ConcurrentDictionary<string, ConnectionInfo> _activeConnections = new();
    private readonly ConcurrentDictionary<string, ProtocolStats> _protocolStats = new();
    private readonly ConcurrentDictionary<int, HourlyTrafficStats> _hourlyStats = new();
    private readonly ConcurrentDictionary<string, TopTalker> _topTalkers = new();
    private readonly ConcurrentQueue<SuspiciousActivity> _suspiciousActivities = new();
    
    // Live metrics
    private long _packetsPerSecond;
    private long _bytesPerSecond;
    private long _connectionsPerMinute;
    private DateTime _lastMetricUpdate = DateTime.UtcNow;
    private long _packetsSinceLastUpdate;
    private long _bytesSinceLastUpdate;
    private int _connectionsSinceLastUpdate;

    private const int MaxSuspiciousActivities = 1000;
    private const int ConnectionTimeoutMinutes = 5;

    public NetworkMonitoringService(
        ILogger<NetworkMonitoringService> logger,
        IServiceScopeFactory scopeFactory,
        IThreatIntelligenceService threatIntelligence,
        IBandwidthMonitorService bandwidthMonitor,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _threatIntelligence = threatIntelligence;
        _bandwidthMonitor = bandwidthMonitor;
        _settings = settings.Value;

        // Start cleanup timer - every minute
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                CleanupOldData();
                UpdateLiveMetrics();
            }
        });

        _logger.LogInformation("????? Network Monitoring Service started - Ready to patrol the digital seas!");
    }

    public void RecordConnection(PacketCapturedEventArgs packet)
    {
        var now = DateTime.UtcNow;
        
        Interlocked.Increment(ref _packetsSinceLastUpdate);
        Interlocked.Add(ref _bytesSinceLastUpdate, packet.PacketSize);

        // Track active connection
        var connKey = GenerateConnectionKey(packet);
        _activeConnections.AddOrUpdate(connKey,
            _ =>
            {
                Interlocked.Increment(ref _connectionsSinceLastUpdate);
                return new ConnectionInfo
                {
                    SourceIp = packet.SourceIp ?? "Unknown",
                    SourceMac = packet.SourceMac,
                    DestinationIp = packet.DestinationIp ?? "Unknown",
                    DestinationPort = packet.DestinationPort,
                    Protocol = packet.Protocol,
                    StartTime = now,
                    LastSeen = now,
                    BytesTransferred = packet.PacketSize,
                    PacketCount = 1
                };
            },
            (_, existing) =>
            {
                existing.LastSeen = now;
                existing.BytesTransferred += packet.PacketSize;
                existing.PacketCount++;
                return existing;
            });

        // Track protocol stats
        _protocolStats.AddOrUpdate(packet.Protocol,
            _ => new ProtocolStats
            {
                Protocol = packet.Protocol,
                PacketCount = 1,
                ByteCount = packet.PacketSize,
                LastSeen = now
            },
            (_, existing) =>
            {
                existing.PacketCount++;
                existing.ByteCount += packet.PacketSize;
                existing.LastSeen = now;
                return existing;
            });

        // Track hourly stats
        var hourKey = now.Hour;
        _hourlyStats.AddOrUpdate(hourKey,
            _ => new HourlyTrafficStats
            {
                Hour = hourKey,
                Date = now.Date,
                PacketCount = 1,
                ByteCount = packet.PacketSize,
                UniqueDevices = new HashSet<string> { packet.SourceMac }
            },
            (_, existing) =>
            {
                if (existing.Date != now.Date)
                {
                    // New day, reset
                    existing.Date = now.Date;
                    existing.PacketCount = 1;
                    existing.ByteCount = packet.PacketSize;
                    existing.UniqueDevices = new HashSet<string> { packet.SourceMac };
                }
                else
                {
                    existing.PacketCount++;
                    existing.ByteCount += packet.PacketSize;
                    existing.UniqueDevices.Add(packet.SourceMac);
                }
                return existing;
            });

        // Track top talkers
        _topTalkers.AddOrUpdate(packet.SourceMac,
            _ => new TopTalker
            {
                MacAddress = packet.SourceMac,
                IpAddress = packet.SourceIp,
                BytesSent = packet.PacketSize,
                PacketsSent = 1,
                LastActivity = now
            },
            (_, existing) =>
            {
                existing.BytesSent += packet.PacketSize;
                existing.PacketsSent++;
                existing.LastActivity = now;
                existing.IpAddress = packet.SourceIp ?? existing.IpAddress;
                return existing;
            });

        // Check for suspicious activity
        _ = CheckSuspiciousActivityAsync(packet);
    }

    private async Task CheckSuspiciousActivityAsync(PacketCapturedEventArgs packet)
    {
        var suspiciousReasons = new List<string>();

        // Check known malicious IPs
        if (!string.IsNullOrEmpty(packet.SourceIp))
        {
            var threat = await _threatIntelligence.CheckIpReputationAsync(packet.SourceIp);
            if (threat != null)
            {
                suspiciousReasons.Add($"????? Malicious IP: {threat.ThreatType} ({threat.ThreatLevel})");
            }
        }

        // Check suspicious ports
        if (packet.DestinationPort.HasValue)
        {
            var port = packet.DestinationPort.Value;
            if (_settings.SuspiciousPorts.Contains(port))
            {
                suspiciousReasons.Add($"?? Suspicious port: {port}");
            }
            
            // Additional pirate-worthy suspicious ports
            var extraSuspiciousPorts = new[] { 4444, 5555, 6666, 31337, 12345, 1234, 6667, 6668, 6669 };
            if (extraSuspiciousPorts.Contains(port))
            {
                suspiciousReasons.Add($"?? Known malware port: {port}");
            }
        }

        // Check for port scanning behavior
        var connCount = _activeConnections.Values
            .Where(c => c.SourceMac == packet.SourceMac && 
                       (DateTime.UtcNow - c.StartTime).TotalMinutes < 1)
            .Select(c => c.DestinationPort)
            .Distinct()
            .Count();
        
        if (connCount > 10)
        {
            suspiciousReasons.Add($"?? Port scanning detected: {connCount} ports in 1 min");
        }

        if (suspiciousReasons.Any())
        {
            var activity = new SuspiciousActivity
            {
                Timestamp = DateTime.UtcNow,
                SourceMac = packet.SourceMac,
                SourceIp = packet.SourceIp,
                DestinationIp = packet.DestinationIp,
                DestinationPort = packet.DestinationPort,
                Protocol = packet.Protocol,
                Reasons = suspiciousReasons,
                Severity = suspiciousReasons.Any(r => r.Contains("Malicious") || r.Contains("malware")) 
                    ? SuspiciousSeverity.Critical 
                    : SuspiciousSeverity.Warning
            };

            _suspiciousActivities.Enqueue(activity);

            // Keep queue bounded
            while (_suspiciousActivities.Count > MaxSuspiciousActivities)
            {
                _suspiciousActivities.TryDequeue(out _);
            }
        }
    }

    public NetworkHealthStatus GetNetworkHealth()
    {
        var now = DateTime.UtcNow;
        var activeConns = _activeConnections.Values
            .Where(c => (now - c.LastSeen).TotalMinutes < ConnectionTimeoutMinutes)
            .ToList();

        var recentSuspicious = _suspiciousActivities
            .Where(s => (now - s.Timestamp).TotalHours < 1)
            .ToList();

        var criticalThreats = recentSuspicious.Count(s => s.Severity == SuspiciousSeverity.Critical);
        var warningThreats = recentSuspicious.Count(s => s.Severity == SuspiciousSeverity.Warning);

        // Calculate health score (0-100)
        var score = 100;
        score -= criticalThreats * 20;
        score -= warningThreats * 5;
        score -= Math.Min(30, activeConns.Count(c => c.Protocol == "Unknown") * 2);
        score = Math.Max(0, score);

        var status = score switch
        {
            >= 90 => "?? Excellent - Smooth sailing!",
            >= 70 => "?? Good - Minor waves ahead",
            >= 50 => "?? Warning - Rough seas detected",
            >= 30 => "?? Critical - Storm approaching!",
            _ => "?? Danger - All hands on deck!"
        };

        return new NetworkHealthStatus
        {
            Score = score,
            Status = status,
            ActiveConnections = activeConns.Count,
            CriticalThreats = criticalThreats,
            WarningThreats = warningThreats,
            TotalBytesLastHour = _hourlyStats.Values
                .Where(h => h.Date == now.Date && h.Hour == now.Hour)
                .Sum(h => h.ByteCount),
            UniqueDevices = _topTalkers.Count,
            TopProtocol = _protocolStats.Values
                .OrderByDescending(p => p.PacketCount)
                .FirstOrDefault()?.Protocol ?? "None",
            LastUpdated = now
        };
    }

    public IEnumerable<ConnectionInfo> GetActiveConnections()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-ConnectionTimeoutMinutes);
        return _activeConnections.Values
            .Where(c => c.LastSeen >= cutoff)
            .OrderByDescending(c => c.BytesTransferred)
            .Take(100);
    }

    public IEnumerable<ProtocolStats> GetProtocolBreakdown()
    {
        return _protocolStats.Values
            .OrderByDescending(p => p.PacketCount)
            .ToList();
    }

    public IEnumerable<HourlyTrafficStats> GetHourlyTraffic(int hours = 24)
    {
        var now = DateTime.UtcNow;
        var result = new List<HourlyTrafficStats>();

        for (int i = 0; i < hours; i++)
        {
            var targetTime = now.AddHours(-i);
            var hourKey = targetTime.Hour;
            
            if (_hourlyStats.TryGetValue(hourKey, out var stats) && stats.Date == targetTime.Date)
            {
                result.Add(stats);
            }
            else
            {
                result.Add(new HourlyTrafficStats
                {
                    Hour = hourKey,
                    Date = targetTime.Date,
                    PacketCount = 0,
                    ByteCount = 0,
                    UniqueDevices = new HashSet<string>()
                });
            }
        }

        return result;
    }

    public IEnumerable<TopTalker> GetTopTalkers(int count = 10)
    {
        return _topTalkers.Values
            .OrderByDescending(t => t.BytesSent)
            .Take(count)
            .ToList();
    }

    public IEnumerable<SuspiciousActivity> GetSuspiciousActivities(int count = 50)
    {
        return _suspiciousActivities
            .Reverse()
            .Take(count)
            .ToList();
    }

    public GeoTrafficStats GetGeoTrafficStats()
    {
        // Simplified geo stats based on IP ranges
        var internalTraffic = _activeConnections.Values
            .Count(c => IsPrivateIp(c.DestinationIp));
        var externalTraffic = _activeConnections.Values
            .Count(c => !IsPrivateIp(c.DestinationIp));

        return new GeoTrafficStats
        {
            InternalConnections = internalTraffic,
            ExternalConnections = externalTraffic,
            InternalBytes = _activeConnections.Values
                .Where(c => IsPrivateIp(c.DestinationIp))
                .Sum(c => c.BytesTransferred),
            ExternalBytes = _activeConnections.Values
                .Where(c => !IsPrivateIp(c.DestinationIp))
                .Sum(c => c.BytesTransferred)
        };
    }

    public LiveNetworkMetrics GetLiveMetrics()
    {
        return new LiveNetworkMetrics
        {
            PacketsPerSecond = Interlocked.Read(ref _packetsPerSecond),
            BytesPerSecond = Interlocked.Read(ref _bytesPerSecond),
            ConnectionsPerMinute = Interlocked.Read(ref _connectionsPerMinute),
            ActiveConnections = _activeConnections.Count,
            Timestamp = DateTime.UtcNow
        };
    }

    private void UpdateLiveMetrics()
    {
        var elapsed = (DateTime.UtcNow - _lastMetricUpdate).TotalSeconds;
        if (elapsed < 1) elapsed = 1;

        Interlocked.Exchange(ref _packetsPerSecond, (long)(_packetsSinceLastUpdate / elapsed));
        Interlocked.Exchange(ref _bytesPerSecond, (long)(_bytesSinceLastUpdate / elapsed));
        Interlocked.Exchange(ref _connectionsPerMinute, _connectionsSinceLastUpdate);

        Interlocked.Exchange(ref _packetsSinceLastUpdate, 0);
        Interlocked.Exchange(ref _bytesSinceLastUpdate, 0);
        Interlocked.Exchange(ref _connectionsSinceLastUpdate, 0);
        _lastMetricUpdate = DateTime.UtcNow;
    }

    private void CleanupOldData()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-ConnectionTimeoutMinutes);
        
        // Clean old connections
        var oldConnections = _activeConnections
            .Where(kvp => kvp.Value.LastSeen < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in oldConnections)
        {
            _activeConnections.TryRemove(key, out _);
        }

        // Clean old top talkers (inactive for more than 1 hour)
        var oldTalkers = _topTalkers
            .Where(kvp => (DateTime.UtcNow - kvp.Value.LastActivity).TotalHours > 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldTalkers)
        {
            _topTalkers.TryRemove(key, out _);
        }
    }

    private static string GenerateConnectionKey(PacketCapturedEventArgs packet)
    {
        return $"{packet.SourceMac}:{packet.SourceIp}:{packet.DestinationIp}:{packet.DestinationPort}:{packet.Protocol}";
    }

    private static bool IsPrivateIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        return ip.StartsWith("192.168.") || 
               ip.StartsWith("10.") || 
               ip.StartsWith("172.16.") ||
               ip.StartsWith("172.17.") ||
               ip.StartsWith("172.18.") ||
               ip.StartsWith("172.19.") ||
               ip.StartsWith("172.2") ||
               ip.StartsWith("172.30.") ||
               ip.StartsWith("172.31.") ||
               ip == "127.0.0.1";
    }
}

// ????? Data models for the pirate's treasure trove

public class NetworkHealthStatus
{
    public int Score { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public int CriticalThreats { get; set; }
    public int WarningThreats { get; set; }
    public long TotalBytesLastHour { get; set; }
    public int UniqueDevices { get; set; }
    public string TopProtocol { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class ConnectionInfo
{
    public string SourceIp { get; set; } = string.Empty;
    public string SourceMac { get; set; } = string.Empty;
    public string DestinationIp { get; set; } = string.Empty;
    public int? DestinationPort { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime LastSeen { get; set; }
    public long BytesTransferred { get; set; }
    public int PacketCount { get; set; }
}

public class ProtocolStats
{
    public string Protocol { get; set; } = string.Empty;
    public long PacketCount { get; set; }
    public long ByteCount { get; set; }
    public DateTime LastSeen { get; set; }
    public double Percentage => 0; // Calculated at retrieval time
}

public class HourlyTrafficStats
{
    public int Hour { get; set; }
    public DateTime Date { get; set; }
    public long PacketCount { get; set; }
    public long ByteCount { get; set; }
    public HashSet<string> UniqueDevices { get; set; } = new();
    public int DeviceCount => UniqueDevices.Count;
}

public class TopTalker
{
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public long BytesSent { get; set; }
    public long PacketsSent { get; set; }
    public DateTime LastActivity { get; set; }
}

public class SuspiciousActivity
{
    public DateTime Timestamp { get; set; }
    public string SourceMac { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public List<string> Reasons { get; set; } = new();
    public SuspiciousSeverity Severity { get; set; }
}

public enum SuspiciousSeverity
{
    Info,
    Warning,
    Critical
}

public class GeoTrafficStats
{
    public int InternalConnections { get; set; }
    public int ExternalConnections { get; set; }
    public long InternalBytes { get; set; }
    public long ExternalBytes { get; set; }
}

public class LiveNetworkMetrics
{
    public long PacketsPerSecond { get; set; }
    public long BytesPerSecond { get; set; }
    public long ConnectionsPerMinute { get; set; }
    public int ActiveConnections { get; set; }
    public DateTime Timestamp { get; set; }
}
