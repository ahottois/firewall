using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface IAlertRepository
{
    Task<NetworkAlert> AddAsync(NetworkAlert alert);
    Task<IEnumerable<NetworkAlert>> GetRecentAsync(int count = 50);
    Task<IEnumerable<NetworkAlert>> GetUnreadAsync();
    Task<int> GetUnreadCountAsync();
    Task<bool> MarkAsReadAsync(int id);
    Task<bool> MarkAllAsReadAsync();
    Task<bool> ResolveAsync(int id);
    Task<bool> ResolveAllAsync();
    Task<bool> DeleteAsync(int id);
    Task<bool> DeleteAllAsync();
    Task<IEnumerable<NetworkAlert>> GetByDeviceAsync(int deviceId);
    Task CleanupOldAlertsAsync(int retentionDays);
}

public class AlertRepository : IAlertRepository
{
    private readonly FirewallDbContext _context;

    public AlertRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task<NetworkAlert> AddAsync(NetworkAlert alert)
    {
        alert.Timestamp = DateTime.UtcNow;
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync();
        return alert;
    }

    public async Task<IEnumerable<NetworkAlert>> GetRecentAsync(int count = 50)
    {
        return await _context.Alerts
            .Include(a => a.Device)
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkAlert>> GetUnreadAsync()
    {
        return await _context.Alerts
            .Include(a => a.Device)
            .Where(a => !a.IsRead)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync()
    {
        return await _context.Alerts.CountAsync(a => !a.IsRead);
    }

    public async Task<bool> MarkAsReadAsync(int id)
    {
        var alert = await _context.Alerts.FindAsync(id);
        if (alert == null) return false;
        
        alert.IsRead = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync()
    {
        await _context.Alerts
            .Where(a => !a.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsRead, true));
        return true;
    }

    public async Task<bool> ResolveAsync(int id)
    {
        var alert = await _context.Alerts.FindAsync(id);
        if (alert == null) return false;
        
        alert.IsResolved = true;
        alert.IsRead = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResolveAllAsync()
    {
        await _context.Alerts
            .Where(a => !a.IsResolved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsResolved, true)
                .SetProperty(a => a.IsRead, true));
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var alert = await _context.Alerts.FindAsync(id);
        if (alert == null) return false;

        _context.Alerts.Remove(alert);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAllAsync()
    {
        await _context.Alerts.ExecuteDeleteAsync();
        return true;
    }

    public async Task<IEnumerable<NetworkAlert>> GetByDeviceAsync(int deviceId)
    {
        return await _context.Alerts
            .Where(a => a.DeviceId == deviceId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task CleanupOldAlertsAsync(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await _context.Alerts
            .Where(a => a.Timestamp < cutoff && a.IsResolved)
            .ExecuteDeleteAsync();
    }
}
