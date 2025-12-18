using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface ISecurityLogRepository
{
    Task<SecurityLog> AddAsync(SecurityLog log);
    Task<IEnumerable<SecurityLog>> GetRecentAsync(int count = 100);
    Task<IEnumerable<SecurityLog>> GetByDeviceAsync(int deviceId, int count = 50);
    Task<IEnumerable<SecurityLog>> GetBySeverityAsync(LogSeverity severity, int count = 50);
    Task<IEnumerable<SecurityLog>> GetByCategoryAsync(LogCategory category, int count = 50);
    Task<IEnumerable<SecurityLog>> GetUnreadAsync();
    Task<int> GetUnreadCountAsync();
    Task<bool> MarkAsReadAsync(int id);
    Task<bool> MarkAllAsReadAsync();
    Task<bool> ArchiveAsync(int id);
    Task<bool> ArchiveAllAsync();
    Task<bool> DeleteAsync(int id);
    Task<bool> DeleteAllAsync();
    Task<bool> DeleteArchivedAsync();
    Task CleanupOldLogsAsync(int retentionDays);
    Task<SecurityLogStats> GetStatsAsync();
}

public class SecurityLogRepository : ISecurityLogRepository
{
    private readonly FirewallDbContext _context;

    public SecurityLogRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task<SecurityLog> AddAsync(SecurityLog log)
    {
        if (log.Timestamp == default)
            log.Timestamp = DateTime.UtcNow;
            
        _context.SecurityLogs.Add(log);
        await _context.SaveChangesAsync();
        return log;
    }

    public async Task<IEnumerable<SecurityLog>> GetRecentAsync(int count = 100)
    {
        return await _context.SecurityLogs
            .Include(l => l.Device)
            .Where(l => !l.IsArchived)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<SecurityLog>> GetByDeviceAsync(int deviceId, int count = 50)
    {
        return await _context.SecurityLogs
            .Where(l => l.DeviceId == deviceId)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<SecurityLog>> GetBySeverityAsync(LogSeverity severity, int count = 50)
    {
        return await _context.SecurityLogs
            .Include(l => l.Device)
            .Where(l => l.Severity == severity && !l.IsArchived)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<SecurityLog>> GetByCategoryAsync(LogCategory category, int count = 50)
    {
        return await _context.SecurityLogs
            .Include(l => l.Device)
            .Where(l => l.Category == category && !l.IsArchived)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<SecurityLog>> GetUnreadAsync()
    {
        return await _context.SecurityLogs
            .Include(l => l.Device)
            .Where(l => !l.IsRead && !l.IsArchived)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync()
    {
        return await _context.SecurityLogs.CountAsync(l => !l.IsRead && !l.IsArchived);
    }

    public async Task<bool> MarkAsReadAsync(int id)
    {
        var log = await _context.SecurityLogs.FindAsync(id);
        if (log == null) return false;

        log.IsRead = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync()
    {
        await _context.SecurityLogs
            .Where(l => !l.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsRead, true));
        return true;
    }

    public async Task<bool> ArchiveAsync(int id)
    {
        var log = await _context.SecurityLogs.FindAsync(id);
        if (log == null) return false;

        log.IsArchived = true;
        log.IsRead = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ArchiveAllAsync()
    {
        await _context.SecurityLogs
            .Where(l => !l.IsArchived)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsArchived, true)
                .SetProperty(l => l.IsRead, true));
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var log = await _context.SecurityLogs.FindAsync(id);
        if (log == null) return false;

        _context.SecurityLogs.Remove(log);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAllAsync()
    {
        await _context.SecurityLogs.ExecuteDeleteAsync();
        return true;
    }

    public async Task<bool> DeleteArchivedAsync()
    {
        await _context.SecurityLogs
            .Where(l => l.IsArchived)
            .ExecuteDeleteAsync();
        return true;
    }

    public async Task CleanupOldLogsAsync(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await _context.SecurityLogs
            .Where(l => l.Timestamp < cutoff)
            .ExecuteDeleteAsync();
    }

    public async Task<SecurityLogStats> GetStatsAsync()
    {
        var logs = await _context.SecurityLogs
            .Where(l => !l.IsArchived)
            .ToListAsync();

        var last24Hours = logs.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-24)).ToList();
        var lastHour = logs.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1)).ToList();

        return new SecurityLogStats
        {
            Total = logs.Count,
            Unread = logs.Count(l => !l.IsRead),
            Critical = logs.Count(l => l.Severity == LogSeverity.Critical),
            Warning = logs.Count(l => l.Severity == LogSeverity.Warning),
            Info = logs.Count(l => l.Severity == LogSeverity.Info),
            Last24Hours = last24Hours.Count,
            LastHour = lastHour.Count,
            BlockedAttempts = logs.Count(l => l.Category == LogCategory.ConnectionAttemptBlocked || 
                                               l.Category == LogCategory.TrafficBlocked),
            ByCategoryLast24h = last24Hours
                .GroupBy(l => l.Category)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }
}

public class SecurityLogStats
{
    public int Total { get; set; }
    public int Unread { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
    public int Last24Hours { get; set; }
    public int LastHour { get; set; }
    public int BlockedAttempts { get; set; }
    public Dictionary<string, int> ByCategoryLast24h { get; set; } = new();
}
