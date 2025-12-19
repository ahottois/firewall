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
    Task<int> RemoveDuplicatesAsync();
}

public class DeviceRepository : IDeviceRepository
{
    private readonly FirewallDbContext _context;
    private static readonly SemaphoreSlim _upsertLock = new(1, 1);

    public DeviceRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task<NetworkDevice?> GetByMacAddressAsync(string macAddress)
    {
        var normalizedMac = macAddress.ToUpperInvariant();
        return await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == normalizedMac);
    }

    public async Task<NetworkDevice?> GetByIpAsync(string ipAddress)
    {
        return await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.IpAddress == ipAddress);
    }

    public async Task<NetworkDevice?> GetByIdAsync(int id)
    {
        return await _context.Devices
            .Include(d => d.Alerts.OrderByDescending(a => a.Timestamp).Take(10))
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IEnumerable<NetworkDevice>> GetAllAsync()
    {
        return await _context.Devices
            .AsNoTracking()
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetUnknownDevicesAsync()
    {
        return await _context.Devices
            .AsNoTracking()
            .Where(d => !d.IsKnown)
            .OrderByDescending(d => d.FirstSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-5);
        return await _context.Devices
            .AsNoTracking()
            .Where(d => d.LastSeen >= threshold)
            .OrderByDescending(d => d.LastSeen)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkDevice>> GetBlockedDevicesAsync()
    {
        return await _context.Devices
            .AsNoTracking()
            .Where(d => d.Status == DeviceStatus.Blocked || d.IsBlocked)
            .OrderByDescending(d => d.BlockedAt ?? d.LastSeen)
            .ToListAsync();
    }

    /// <summary>
    /// Ajoute ou met a jour un appareil de maniere atomique.
    /// Utilise un verrou pour eviter les doublons en cas de concurrence.
    /// </summary>
    public async Task<NetworkDevice> AddOrUpdateAsync(NetworkDevice device)
    {
        var normalizedMac = device.MacAddress.ToUpperInvariant();
        
        // Utiliser un verrou pour eviter les conditions de concurrence
        await _upsertLock.WaitAsync();
        try
        {
            // Chercher l'appareil existant avec tracking
            var existing = await _context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == normalizedMac);
            
            if (existing == null)
            {
                // Nouveau device
                device.MacAddress = normalizedMac;
                device.FirstSeen = DateTime.UtcNow;
                device.LastSeen = DateTime.UtcNow;
                _context.Devices.Add(device);
                
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
                {
                    // Un autre thread a insere le meme appareil, on le recupere et on le met a jour
                    _context.Entry(device).State = EntityState.Detached;
                    existing = await _context.Devices
                        .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == normalizedMac);
                    
                    if (existing != null)
                    {
                        return await UpdateExistingDevice(existing, device);
                    }
                }
                
                return device;
            }
            else
            {
                return await UpdateExistingDevice(existing, device);
            }
        }
        finally
        {
            _upsertLock.Release();
        }
    }

    /// <summary>
    /// Met a jour un appareil existant
    /// </summary>
    private async Task<NetworkDevice> UpdateExistingDevice(NetworkDevice existing, NetworkDevice newData)
    {
        // Mettre a jour les informations non-nulles
        if (!string.IsNullOrEmpty(newData.IpAddress))
            existing.IpAddress = newData.IpAddress;
        
        if (!string.IsNullOrEmpty(newData.Hostname))
            existing.Hostname = newData.Hostname;
        
        if (!string.IsNullOrEmpty(newData.Vendor))
            existing.Vendor = newData.Vendor;
        
        existing.LastSeen = DateTime.UtcNow;
        
        // Ne pas changer le statut si l'appareil est bloque
        if (!existing.IsBlocked && existing.Status != DeviceStatus.Blocked)
        {
            existing.Status = DeviceStatus.Online;
        }
        
        await _context.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Verifie si l'exception est une violation de cle unique
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        var innerMessage = ex.InnerException?.Message?.ToLowerInvariant() ?? "";
        return innerMessage.Contains("unique") || 
               innerMessage.Contains("duplicate") ||
               innerMessage.Contains("constraint") ||
               innerMessage.Contains("primary key");
    }

    /// <summary>
    /// Supprime les doublons en gardant l'entree la plus ancienne (FirstSeen)
    /// </summary>
    public async Task<int> RemoveDuplicatesAsync()
    {
        // Trouver les MAC avec des doublons
        var duplicates = await _context.Devices
            .GroupBy(d => d.MacAddress.ToUpper())
            .Where(g => g.Count() > 1)
            .Select(g => new 
            { 
                Mac = g.Key, 
                Ids = g.OrderBy(d => d.FirstSeen).Select(d => d.Id).ToList() 
            })
            .ToListAsync();

        int deletedCount = 0;
        foreach (var dup in duplicates)
        {
            // Garder le premier (le plus ancien), supprimer les autres
            var idsToDelete = dup.Ids.Skip(1).ToList();
            foreach (var id in idsToDelete)
            {
                var deleted = await _context.Devices
                    .Where(d => d.Id == id)
                    .ExecuteDeleteAsync();
                deletedCount += deleted;
            }
        }

        return deletedCount;
    }

    public async Task<bool> SetTrustedAsync(int id, bool trusted)
    {
        return await _context.Devices
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsTrusted, trusted)
                .SetProperty(d => d.IsKnown, true)) > 0;
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

    public async Task<bool> UpdateDeviceInfoAsync(int id, UpdateDeviceRequest request)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;

        if (request.IpAddress != null) device.IpAddress = request.IpAddress;
        if (request.Vendor != null) device.Vendor = request.Vendor;
        if (request.Description != null) device.Description = request.Description;
        if (request.IsKnown.HasValue) device.IsKnown = request.IsKnown.Value;
        if (request.IsTrusted.HasValue) device.IsTrusted = request.IsTrusted.Value;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateStatusAsync(int id, DeviceStatus status)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;
        
        device.Status = status;
        if (status == DeviceStatus.Online)
        {
            device.LastSeen = DateTime.UtcNow;
        }
        
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetBlockedAsync(int id, bool blocked, string? reason = null)
    {
        var device = await _context.Devices.FindAsync(id);
        if (device == null) return false;

        device.IsBlocked = blocked;
        device.Status = blocked ? DeviceStatus.Blocked : DeviceStatus.Unknown;
        device.BlockedAt = blocked ? DateTime.UtcNow : null;
        device.BlockReason = blocked ? reason : null;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        return await _context.Devices
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync() > 0;
    }
}
