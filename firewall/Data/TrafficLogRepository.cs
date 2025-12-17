using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface ITrafficLogRepository
{
    Task AddAsync(TrafficLog log);
    Task AddBatchAsync(IEnumerable<TrafficLog> logs);
    Task<IEnumerable<TrafficLog>> GetRecentAsync(int count = 100);
    Task<IEnumerable<TrafficLog>> GetByDeviceAsync(int deviceId, int count = 100);
    Task<TrafficStats> GetStatsAsync(TimeSpan period);
    Task CleanupOldLogsAsync(int retentionDays);
}

public class TrafficLogRepository : ITrafficLogRepository
{
    private readonly FirewallDbContext _context;

    public TrafficLogRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TrafficLog log)
    {
        log.Timestamp = DateTime.UtcNow;
        _context.TrafficLogs.Add(log);
        await _context.SaveChangesAsync();
    }

    public async Task AddBatchAsync(IEnumerable<TrafficLog> logs)
    {
        var now = DateTime.UtcNow;
        foreach (var log in logs)
        {
            log.Timestamp = now;
        }
        _context.TrafficLogs.AddRange(logs);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<TrafficLog>> GetRecentAsync(int count = 100)
    {
        return await _context.TrafficLogs
            .Include(t => t.Device)
            .OrderByDescending(t => t.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<TrafficLog>> GetByDeviceAsync(int deviceId, int count = 100)
    {
        return await _context.TrafficLogs
            .Where(t => t.DeviceId == deviceId)
            .OrderByDescending(t => t.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<TrafficStats> GetStatsAsync(TimeSpan period)
    {
        var since = DateTime.UtcNow - period;
        var logs = await _context.TrafficLogs
            .Where(t => t.Timestamp >= since)
            .ToListAsync();

        return new TrafficStats
        {
            TotalPackets = logs.Count,
            TotalBytes = logs.Sum(l => l.PacketSize),
            InboundPackets = logs.Count(l => l.Direction == TrafficDirection.Inbound),
            OutboundPackets = logs.Count(l => l.Direction == TrafficDirection.Outbound),
            SuspiciousPackets = logs.Count(l => l.IsSuspicious),
            UniqueDevices = logs.Select(l => l.SourceMac).Distinct().Count(),
            TopProtocols = logs.GroupBy(l => l.Protocol)
                              .OrderByDescending(g => g.Count())
                              .Take(5)
                              .ToDictionary(g => g.Key, g => g.Count()),
            Period = period
        };
    }

    public async Task CleanupOldLogsAsync(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await _context.TrafficLogs
            .Where(t => t.Timestamp < cutoff)
            .ExecuteDeleteAsync();
    }
}

public class TrafficStats
{
    public int TotalPackets { get; set; }
    public long TotalBytes { get; set; }
    public int InboundPackets { get; set; }
    public int OutboundPackets { get; set; }
    public int SuspiciousPackets { get; set; }
    public int UniqueDevices { get; set; }
    public Dictionary<string, int> TopProtocols { get; set; } = new();
    public TimeSpan Period { get; set; }
}
