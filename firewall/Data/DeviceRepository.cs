using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Controllers;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface IDeviceRepository
{
    Task<NetworkDevice?> GetByMacAddressAsync(string macAddress);
    Task<NetworkDevice?> GetByIpAsync(string ipAddress);
    Task<NetworkDevice?> GetByIdAsync(int id);
    Task<IEnumerable<NetworkDevice>> GetAllAsync();
    Task<IEnumerable<NetworkDevice>> GetUnknownDevicesAsync();
    Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync();
    Task<IEnumerable<NetworkDevice>> GetBlockedDevicesAsync();
    Task<NetworkDevice> AddOrUpdateAsync(NetworkDevice device);
    Task<bool> SetTrustedAsync(int id, bool trusted);
    Task<bool> SetKnownAsync(int id, bool known, string? description = null);
    Task<bool> UpdateDeviceInfoAsync(int id, UpdateDeviceRequest request);
    Task<bool> UpdateStatusAsync(int id, DeviceStatus status);
    Task<bool> SetBlockedAsync(int id, bool blocked, string? reason = null);
    Task<bool> DeleteAsync(int id);
}

public class DeviceRepository(FirewallDbContext context) : IDeviceRepository
{
    public async Task<NetworkDevice?> GetByMacAddressAsync(string macAddress)
    {
        return await context.Devices
            .FirstOrDefaultAsync(d => d.MacAddress.ToLower() == macAddress.ToLower());
    }

    public async Task<NetworkDevice?> GetByIpAsync(string ipAddress)
    {
        return await context.Devices
            .FirstOrDefaultAsync(d => d.IpAddress == ipAddress);
    }

    public async Task<NetworkDevice?> GetByIdAsync(int id)
    {
        return await context.Devices
            .Include(d => d.Alerts.OrderByDescending(a => a.Timestamp).Take(10))
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IEnumerable<NetworkDevice>> GetAllAsync()
    {
        return await context.Devices
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetUnknownDevicesAsync()
    {
        return await context.Devices
            .Where(d => !d.IsKnown)
            .OrderByDescending(d => d.FirstSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-5);
        return await context.Devices
            .Where(d => d.LastSeen >= threshold)
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetBlockedDevicesAsync()
    {
        return await context.Devices
            .Where(d => d.Status == DeviceStatus.Blocked || d.IsBlocked)
            .OrderByDescending(d => d.BlockedAt ?? d.LastSeen)
            .ToListAsync();
    }

    public async Task<NetworkDevice> AddOrUpdateAsync(NetworkDevice device)
    {
        var existing = await GetByMacAddressAsync(device.MacAddress);
        
        if (existing == null)
        {
            device.FirstSeen = DateTime.UtcNow;
            device.LastSeen = DateTime.UtcNow;
            context.Devices.Add(device);
        }
        else
        {
            existing.IpAddress = device.IpAddress ?? existing.IpAddress;
            existing.Hostname = device.Hostname ?? existing.Hostname;
            existing.Vendor = device.Vendor ?? existing.Vendor;
            existing.LastSeen = DateTime.UtcNow;
            // Ne pas changer le statut si l'appareil est bloqué
            if (!existing.IsBlocked && existing.Status != DeviceStatus.Blocked)
            {
                existing.Status = DeviceStatus.Online;
            }
            device = existing;
        }
        
        await context.SaveChangesAsync();
        return device;
    }

    public async Task<bool> SetTrustedAsync(int id, bool trusted)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.IsTrusted = trusted;
        device.IsKnown = true;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetKnownAsync(int id, bool known, string? description = null)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.IsKnown = known;
        if (description != null) device.Description = description;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateDeviceInfoAsync(int id, UpdateDeviceRequest request)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;

        if (request.IpAddress != null) device.IpAddress = request.IpAddress;
        if (request.Vendor != null) device.Vendor = request.Vendor;
        if (request.Description != null) device.Description = request.Description;
        if (request.IsKnown.HasValue) device.IsKnown = request.IsKnown.Value;
        if (request.IsTrusted.HasValue) device.IsTrusted = request.IsTrusted.Value;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateStatusAsync(int id, DeviceStatus status)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.Status = status;
        if (status == DeviceStatus.Online)
        {
            device.LastSeen = DateTime.UtcNow;
        }
        
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetBlockedAsync(int id, bool blocked, string? reason = null)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;

        device.IsBlocked = blocked;
        device.Status = blocked ? DeviceStatus.Blocked : DeviceStatus.Unknown;
        
        if (blocked)
        {
            device.BlockedAt = DateTime.UtcNow;
            device.BlockReason = reason;
        }
        else
        {
            device.BlockedAt = null;
            device.BlockReason = null;
        }

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var device = await context.Devices.FindAsync(id);
        if (device == null) return false;
        
        context.Devices.Remove(device);
        await context.SaveChangesAsync();
        return true;
    }
}
