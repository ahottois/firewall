using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface ICameraRepository
{
    Task<NetworkCamera?> GetByIdAsync(int id);
    Task<NetworkCamera?> GetByIpAndPortAsync(string ip, int port);
    Task<IEnumerable<NetworkCamera>> GetAllAsync();
    Task<IEnumerable<NetworkCamera>> GetOnlineAsync();
    Task<IEnumerable<NetworkCamera>> GetWithDefaultPasswordAsync();
    Task<NetworkCamera> AddOrUpdateAsync(NetworkCamera camera);
    Task<bool> UpdateStatusAsync(int id, CameraStatus status, PasswordStatus passwordStatus);
    Task<bool> DeleteAsync(int id);
}

public class CameraRepository : ICameraRepository
{
    private readonly FirewallDbContext _context;

    public CameraRepository(FirewallDbContext context)
    {
        _context = context;
    }

    public async Task<NetworkCamera?> GetByIdAsync(int id)
    {
        return await _context.Cameras
            .Include(c => c.Device)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<NetworkCamera?> GetByIpAndPortAsync(string ip, int port)
    {
        return await _context.Cameras
            .Include(c => c.Device)
            .FirstOrDefaultAsync(c => c.IpAddress == ip && c.Port == port);
    }

    public async Task<IEnumerable<NetworkCamera>> GetAllAsync()
    {
        return await _context.Cameras
            .Include(c => c.Device)
            .OrderByDescending(c => c.LastChecked)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkCamera>> GetOnlineAsync()
    {
        return await _context.Cameras
            .Include(c => c.Device)
            .Where(c => c.Status == CameraStatus.Online || c.Status == CameraStatus.Authenticated)
            .OrderByDescending(c => c.LastChecked)
            .ToListAsync();
    }

    public async Task<IEnumerable<NetworkCamera>> GetWithDefaultPasswordAsync()
    {
        return await _context.Cameras
            .Include(c => c.Device)
            .Where(c => c.PasswordStatus == PasswordStatus.DefaultPassword || c.PasswordStatus == PasswordStatus.NoPassword)
            .ToListAsync();
    }

    public async Task<NetworkCamera> AddOrUpdateAsync(NetworkCamera camera)
    {
        var existing = await GetByIpAndPortAsync(camera.IpAddress, camera.Port);
        
        if (existing == null)
        {
            camera.FirstDetected = DateTime.UtcNow;
            camera.LastChecked = DateTime.UtcNow;
            _context.Cameras.Add(camera);
        }
        else
        {
            existing.Manufacturer = camera.Manufacturer ?? existing.Manufacturer;
            existing.Model = camera.Model ?? existing.Model;
            existing.StreamUrl = camera.StreamUrl ?? existing.StreamUrl;
            existing.SnapshotUrl = camera.SnapshotUrl ?? existing.SnapshotUrl;
            existing.Status = camera.Status;
            existing.PasswordStatus = camera.PasswordStatus;
            existing.DetectedCredentials = camera.DetectedCredentials ?? existing.DetectedCredentials;
            existing.IsAccessible = camera.IsAccessible;
            existing.LastChecked = DateTime.UtcNow;
            camera = existing;
        }
        
        await _context.SaveChangesAsync();
        return camera;
    }

    public async Task<bool> UpdateStatusAsync(int id, CameraStatus status, PasswordStatus passwordStatus)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return false;
        
        camera.Status = status;
        camera.PasswordStatus = passwordStatus;
        camera.LastChecked = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return false;
        
        _context.Cameras.Remove(camera);
        await _context.SaveChangesAsync();
        return true;
    }
}
