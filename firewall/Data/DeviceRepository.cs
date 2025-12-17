using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface IDeviceRepository
{
    Task<NetworkDevice?> GetByMacAddressAsync(string macAddress);
    Task<NetworkDevice?> GetByIdAsync(int id);
    Task<IEnumerable<NetworkDevice>> GetAllAsync();
    Task<IEnumerable<NetworkDevice>> GetUnknownDevicesAsync();
    Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync();
    Task<NetworkDevice> AddOrUpdateAsync(NetworkDevice device);
    Task<bool> SetTrustedAsync(int id, bool trusted);
    Task<bool> SetKnownAsync(int id, bool known, string? description = null);
    Task<bool> DeleteAsync(int id);
}

public class DeviceRepository : IDeviceRepository
{
    private readonly FirewallDbContext _context;

    public DeviceRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task<NetworkDevice?> GetByMacAddressAsync(string macAddress)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.MacAddress.ToLower() == macAddress.ToLower());
    }

    public async Task<NetworkDevice?> GetByIdAsync(int id)
    {
        return await _context.Devices
            .Include(d => d.Alerts.OrderByDescending(a => a.Timestamp).Take(10))
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IEnumerable<NetworkDevice>> GetAllAsync()
    {
        return await _context.Devices
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetUnknownDevicesAsync()
    {
        return await _context.Devices
            .Where(d => !d.IsKnown)
            .OrderByDescending(d => d.FirstSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-5);
        return await _context.Devices
            .Where(d => d.LastSeen >= threshold)
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<NetworkDevice> AddOrUpdateAsync(NetworkDevice device)
    {
        var existing = await GetByMacAddressAsync(device.MacAddress);
        
        if (existing == null)
        {
            device.FirstSeen = DateTime.UtcNow;
            device.LastSeen = DateTime.UtcNow;
            _context.Devices.Add(device);
        }
        else
        {
            existing.IpAddress = device.IpAddress ?? existing.IpAddress;
            existing.Hostname = device.Hostname ?? existing.Hostname;
            existing.Vendor = device.Vendor ?? existing.Vendor;
            existing.LastSeen = DateTime.UtcNow;
            existing.Status = DeviceStatus.Online;
            device = existing;
        }
        
        await _context.SaveChangesAsync();
        return device;
    }

    public async Task<bool> SetTrustedAsync(int id, bool trusted)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.IsTrusted = trusted;
        device.IsKnown = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetKnownAsync(int id, bool known, string? description = null)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.IsKnown = known;
        if (description != null) device.Description = description;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;
        
        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return true;
    }
}
