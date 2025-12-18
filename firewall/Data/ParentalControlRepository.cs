using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Models;

namespace NetworkFirewall.Data;

public interface IParentalControlRepository
{
    // Profiles
    Task<IEnumerable<ChildProfile>> GetAllProfilesAsync();
    Task<ChildProfile?> GetProfileByIdAsync(int id);
    Task<ChildProfile?> GetProfileWithDetailsAsync(int id);
    Task<ChildProfile> CreateProfileAsync(ChildProfile profile);
    Task<ChildProfile> UpdateProfileAsync(ChildProfile profile);
    Task DeleteProfileAsync(int id);
    
    // Devices
    Task<ProfileDevice?> GetDeviceByMacAsync(string macAddress);
    Task<IEnumerable<ProfileDevice>> GetDevicesByProfileAsync(int profileId);
    Task<ProfileDevice> AddDeviceToProfileAsync(int profileId, ProfileDevice device);
    Task RemoveDeviceFromProfileAsync(int profileId, string macAddress);
    
    // Schedules
    Task<IEnumerable<TimeSchedule>> GetSchedulesByProfileAsync(int profileId);
    Task UpdateSchedulesAsync(int profileId, IEnumerable<TimeSchedule> schedules);
    
    // Web Filters
    Task<IEnumerable<WebFilterRule>> GetFiltersByProfileAsync(int profileId);
    Task<WebFilterRule> AddFilterAsync(WebFilterRule filter);
    Task RemoveFilterAsync(int filterId);
    Task UpdateFiltersAsync(int profileId, IEnumerable<WebFilterRule> filters);
    
    // Usage Logs
    Task<UsageLog?> GetTodayUsageAsync(int profileId);
    Task<IEnumerable<UsageLog>> GetUsageHistoryAsync(int profileId, int days = 7);
    Task UpdateUsageAsync(int profileId, int minutesToAdd);
    Task IncrementBlockCountAsync(int profileId);
    Task ResetDailyUsageAsync();
}

public class ParentalControlRepository : IParentalControlRepository
{
    private readonly FirewallDbContext _context;
    private readonly ILogger<ParentalControlRepository> _logger;

    public ParentalControlRepository(FirewallDbContext context, ILogger<ParentalControlRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Profiles

    public async Task<IEnumerable<ChildProfile>> GetAllProfilesAsync()
    {
        return await _context.ChildProfiles
            .Include(p => p.Devices)
            .Include(p => p.Schedules)
            .Include(p => p.WebFilters)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<ChildProfile?> GetProfileByIdAsync(int id)
    {
        return await _context.ChildProfiles.FindAsync(id);
    }

    public async Task<ChildProfile?> GetProfileWithDetailsAsync(int id)
    {
        return await _context.ChildProfiles
            .Include(p => p.Devices)
            .Include(p => p.Schedules)
            .Include(p => p.WebFilters)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<ChildProfile> CreateProfileAsync(ChildProfile profile)
    {
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        
        _context.ChildProfiles.Add(profile);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Created child profile: {Name} (ID: {Id})", profile.Name, profile.Id);
        return profile;
    }

    public async Task<ChildProfile> UpdateProfileAsync(ChildProfile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        
        _context.ChildProfiles.Update(profile);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Updated child profile: {Name} (ID: {Id})", profile.Name, profile.Id);
        return profile;
    }

    public async Task DeleteProfileAsync(int id)
    {
        var profile = await _context.ChildProfiles.FindAsync(id);
        if (profile != null)
        {
            _context.ChildProfiles.Remove(profile);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Deleted child profile: {Name} (ID: {Id})", profile.Name, id);
        }
    }

    #endregion

    #region Devices

    public async Task<ProfileDevice?> GetDeviceByMacAsync(string macAddress)
    {
        return await _context.ProfileDevices
            .Include(d => d.Profile)
            .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == macAddress.ToUpper());
    }

    public async Task<IEnumerable<ProfileDevice>> GetDevicesByProfileAsync(int profileId)
    {
        return await _context.ProfileDevices
            .Where(d => d.ProfileId == profileId)
            .ToListAsync();
    }

    public async Task<ProfileDevice> AddDeviceToProfileAsync(int profileId, ProfileDevice device)
    {
        device.ProfileId = profileId;
        device.MacAddress = device.MacAddress.ToUpper();
        device.AddedAt = DateTime.UtcNow;
        
        // Vérifier si l'appareil n'est pas déjà dans un autre profil
        var existing = await GetDeviceByMacAsync(device.MacAddress);
        if (existing != null)
        {
            _context.ProfileDevices.Remove(existing);
            _logger.LogInformation("Device {Mac} moved from profile {Old} to {New}", 
                device.MacAddress, existing.ProfileId, profileId);
        }
        
        _context.ProfileDevices.Add(device);
        await _context.SaveChangesAsync();
        
        return device;
    }

    public async Task RemoveDeviceFromProfileAsync(int profileId, string macAddress)
    {
        var device = await _context.ProfileDevices
            .FirstOrDefaultAsync(d => d.ProfileId == profileId && 
                                      d.MacAddress.ToUpper() == macAddress.ToUpper());
        
        if (device != null)
        {
            _context.ProfileDevices.Remove(device);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Removed device {Mac} from profile {Id}", macAddress, profileId);
        }
    }

    #endregion

    #region Schedules

    public async Task<IEnumerable<TimeSchedule>> GetSchedulesByProfileAsync(int profileId)
    {
        return await _context.TimeSchedules
            .Where(s => s.ProfileId == profileId)
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task UpdateSchedulesAsync(int profileId, IEnumerable<TimeSchedule> schedules)
    {
        // Supprimer les anciens schedules
        var existing = await _context.TimeSchedules
            .Where(s => s.ProfileId == profileId)
            .ToListAsync();
        
        _context.TimeSchedules.RemoveRange(existing);
        
        // Ajouter les nouveaux
        foreach (var schedule in schedules)
        {
            schedule.ProfileId = profileId;
            _context.TimeSchedules.Add(schedule);
        }
        
        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated schedules for profile {Id}: {Count} slots", 
            profileId, schedules.Count());
    }

    #endregion

    #region Web Filters

    public async Task<IEnumerable<WebFilterRule>> GetFiltersByProfileAsync(int profileId)
    {
        return await _context.WebFilterRules
            .Where(f => f.ProfileId == profileId)
            .OrderBy(f => f.FilterType)
            .ThenBy(f => f.Value)
            .ToListAsync();
    }

    public async Task<WebFilterRule> AddFilterAsync(WebFilterRule filter)
    {
        _context.WebFilterRules.Add(filter);
        await _context.SaveChangesAsync();
        return filter;
    }

    public async Task RemoveFilterAsync(int filterId)
    {
        var filter = await _context.WebFilterRules.FindAsync(filterId);
        if (filter != null)
        {
            _context.WebFilterRules.Remove(filter);
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateFiltersAsync(int profileId, IEnumerable<WebFilterRule> filters)
    {
        // Supprimer les anciens filtres
        var existing = await _context.WebFilterRules
            .Where(f => f.ProfileId == profileId)
            .ToListAsync();
        
        _context.WebFilterRules.RemoveRange(existing);
        
        // Ajouter les nouveaux
        foreach (var filter in filters)
        {
            filter.ProfileId = profileId;
            _context.WebFilterRules.Add(filter);
        }
        
        await _context.SaveChangesAsync();
    }

    #endregion

    #region Usage Logs

    public async Task<UsageLog?> GetTodayUsageAsync(int profileId)
    {
        var today = DateTime.UtcNow.Date;
        return await _context.UsageLogs
            .FirstOrDefaultAsync(l => l.ProfileId == profileId && l.Date == today);
    }

    public async Task<IEnumerable<UsageLog>> GetUsageHistoryAsync(int profileId, int days = 7)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days);
        return await _context.UsageLogs
            .Where(l => l.ProfileId == profileId && l.Date >= startDate)
            .OrderByDescending(l => l.Date)
            .ToListAsync();
    }

    public async Task UpdateUsageAsync(int profileId, int minutesToAdd)
    {
        var today = DateTime.UtcNow.Date;
        var usage = await _context.UsageLogs
            .FirstOrDefaultAsync(l => l.ProfileId == profileId && l.Date == today);
        
        if (usage == null)
        {
            usage = new UsageLog
            {
                ProfileId = profileId,
                Date = today,
                MinutesUsed = minutesToAdd,
                ConnectionCount = 1
            };
            _context.UsageLogs.Add(usage);
        }
        else
        {
            usage.MinutesUsed += minutesToAdd;
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task IncrementBlockCountAsync(int profileId)
    {
        var today = DateTime.UtcNow.Date;
        var usage = await _context.UsageLogs
            .FirstOrDefaultAsync(l => l.ProfileId == profileId && l.Date == today);
        
        if (usage == null)
        {
            usage = new UsageLog
            {
                ProfileId = profileId,
                Date = today,
                BlockCount = 1
            };
            _context.UsageLogs.Add(usage);
        }
        else
        {
            usage.BlockCount++;
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task ResetDailyUsageAsync()
    {
        // Cette méthode est appelée à minuit pour préparer les logs du nouveau jour
        // On ne réinitialise pas les anciens logs, ils sont conservés pour l'historique
        _logger.LogInformation("Daily usage reset triggered at {Time}", DateTime.UtcNow);
    }

    #endregion
}
