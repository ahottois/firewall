using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface ITrafficLoggingService
{
    void LogPacket(PacketCapturedEventArgs packet);
    TrafficStats GetRealTimeStats();
    Task FlushAsync();
}

public class TrafficLoggingService : ITrafficLoggingService, IDisposable
{
    private readonly ILogger<TrafficLoggingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppSettings _settings;
    
    private readonly ConcurrentQueue<TrafficLog> _logQueue = new();
    private readonly ConcurrentDictionary<string, int> _protocolCounts = new();
    private readonly Timer _flushTimer;
    private readonly object _statsLock = new();
    
    // Real-time stats
    private long _totalPackets;
    private long _totalBytes;
    private long _inboundPackets;
    private long _outboundPackets;
    private long _suspiciousPackets;
    private readonly ConcurrentDictionary<string, byte> _uniqueDevices = new();
    private DateTime _statsStartTime = DateTime.UtcNow;

    private const int BatchSize = 100;
    private const int FlushIntervalMs = 5000; // Flush every 5 seconds

    public TrafficLoggingService(
        ILogger<TrafficLoggingService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;

        // Start periodic flush timer
        _flushTimer = new Timer(async _ => await FlushAsync(), null, FlushIntervalMs, FlushIntervalMs);
    }

    public void LogPacket(PacketCapturedEventArgs packet)
    {
        // Update real-time stats
        Interlocked.Increment(ref _totalPackets);
        Interlocked.Add(ref _totalBytes, packet.PacketSize);
        
        // Determine direction (simplified - assumes local network is 192.168.x.x or 10.x.x.x)
        var isInbound = IsInbound(packet);
        if (isInbound)
            Interlocked.Increment(ref _inboundPackets);
        else
            Interlocked.Increment(ref _outboundPackets);

        // Track unique devices
        _uniqueDevices.TryAdd(packet.SourceMac, 0);

        // Track protocols
        _protocolCounts.AddOrUpdate(packet.Protocol, 1, (_, count) => count + 1);

        // Check if suspicious
        var isSuspicious = IsSuspiciousPacket(packet);
        if (isSuspicious)
            Interlocked.Increment(ref _suspiciousPackets);

        // Create log entry
        var log = new TrafficLog
        {
            SourceMac = packet.SourceMac,
            DestinationMac = packet.DestinationMac,
            SourceIp = packet.SourceIp,
            DestinationIp = packet.DestinationIp,
            SourcePort = packet.SourcePort,
            DestinationPort = packet.DestinationPort,
            Protocol = packet.Protocol,
            PacketSize = packet.PacketSize,
            Direction = isInbound ? TrafficDirection.Inbound : TrafficDirection.Outbound,
            IsSuspicious = isSuspicious,
            Timestamp = packet.Timestamp
        };

        _logQueue.Enqueue(log);

        // Flush if queue is getting large
        if (_logQueue.Count >= BatchSize * 2)
        {
            _ = Task.Run(FlushAsync);
        }
    }

    public TrafficStats GetRealTimeStats()
    {
        var elapsed = DateTime.UtcNow - _statsStartTime;
        if (elapsed.TotalSeconds < 1) elapsed = TimeSpan.FromSeconds(1);

        return new TrafficStats
        {
            TotalPackets = (int)Interlocked.Read(ref _totalPackets),
            TotalBytes = Interlocked.Read(ref _totalBytes),
            InboundPackets = (int)Interlocked.Read(ref _inboundPackets),
            OutboundPackets = (int)Interlocked.Read(ref _outboundPackets),
            SuspiciousPackets = (int)Interlocked.Read(ref _suspiciousPackets),
            UniqueDevices = _uniqueDevices.Count,
            TopProtocols = _protocolCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Period = elapsed
        };
    }

    public async Task FlushAsync()
    {
        if (_logQueue.IsEmpty) return;

        var logsToSave = new List<TrafficLog>();
        
        while (logsToSave.Count < BatchSize && _logQueue.TryDequeue(out var log))
        {
            logsToSave.Add(log);
        }

        if (logsToSave.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            // Link logs to devices
            foreach (var log in logsToSave)
            {
                var device = await deviceRepo.GetByMacAddressAsync(log.SourceMac);
                if (device != null)
                {
                    log.DeviceId = device.Id;
                }
            }

            context.TrafficLogs.AddRange(logsToSave);
            await context.SaveChangesAsync();

            _logger.LogDebug("Flushed {Count} traffic logs to database", logsToSave.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing traffic logs");
            
            // Re-queue failed logs (up to a limit)
            if (_logQueue.Count < BatchSize * 10)
            {
                foreach (var log in logsToSave)
                {
                    _logQueue.Enqueue(log);
                }
            }
        }
    }

    private bool IsInbound(PacketCapturedEventArgs packet)
    {
        if (string.IsNullOrEmpty(packet.DestinationIp)) return false;
        
        // Consider it inbound if destination is a local IP
        return packet.DestinationIp.StartsWith("192.168.") ||
               packet.DestinationIp.StartsWith("10.") ||
               packet.DestinationIp.StartsWith("172.16.") ||
               packet.DestinationIp.StartsWith("172.17.") ||
               packet.DestinationIp.StartsWith("172.18.") ||
               packet.DestinationIp.StartsWith("172.19.") ||
               packet.DestinationIp.StartsWith("172.2") ||
               packet.DestinationIp.StartsWith("172.30.") ||
               packet.DestinationIp.StartsWith("172.31.");
    }

    private bool IsSuspiciousPacket(PacketCapturedEventArgs packet)
    {
        // Check for suspicious ports
        if (packet.DestinationPort.HasValue)
        {
            var port = packet.DestinationPort.Value;
            if (_settings.SuspiciousPorts.Contains(port))
                return true;
        }

        // Check for broadcast source MAC (potential spoofing)
        if (packet.SourceMac == "FF:FF:FF:FF:FF:FF")
            return true;

        // Check for null source MAC
        if (packet.SourceMac == "00:00:00:00:00:00")
            return true;

        return false;
    }

    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalPackets, 0);
        Interlocked.Exchange(ref _totalBytes, 0);
        Interlocked.Exchange(ref _inboundPackets, 0);
        Interlocked.Exchange(ref _outboundPackets, 0);
        Interlocked.Exchange(ref _suspiciousPackets, 0);
        _uniqueDevices.Clear();
        _protocolCounts.Clear();
        _statsStartTime = DateTime.UtcNow;
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        // Final flush
        FlushAsync().GetAwaiter().GetResult();
    }
}
