using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;
using NetworkFirewall.Services.Firewall;

namespace NetworkFirewall.Services;

public interface IParentalControlService
{
    Task<IEnumerable<ProfileStatus>> GetAllProfileStatusAsync();
    Task<ProfileStatus?> GetProfileStatusAsync(int profileId);
    Task<ChildProfile> CreateProfileAsync(ChildProfileDto dto);
    Task<ChildProfile> UpdateProfileAsync(int id, ChildProfileDto dto);
    Task DeleteProfileAsync(int id);
    Task<bool> TogglePauseAsync(int profileId);
    Task<bool> SetPauseStateAsync(int profileId, bool isPaused);
    Task CheckAndEnforceSchedulesAsync();
    bool IsWithinAllowedTime(ChildProfile profile);
    Task<ProfileAccessStatus> GetCurrentAccessStatusAsync(ChildProfile profile);
}

public class ParentalControlService : BackgroundService, IParentalControlService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParentalControlService> _logger;
    private readonly IHubContext<ParentalControlHub> _hubContext;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    
    // Cache des statuts pour éviter les notifications inutiles
    private readonly Dictionary<int, ProfileAccessStatus> _lastKnownStatus = new();

    public ParentalControlService(
        IServiceScopeFactory scopeFactory,
        ILogger<ParentalControlService> logger,
        IHubContext<ParentalControlHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ParentalControlService started");

        // Attendre un peu que les autres services démarrent
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndEnforceSchedulesAsync();
                await UpdateUsageTimeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ParentalControlService loop");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task CheckAndEnforceSchedulesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
        var firewallEngine = scope.ServiceProvider.GetRequiredService<IFirewallEngine>();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        var profiles = await repo.GetAllProfilesAsync();

        foreach (var profile in profiles)
        {
            if (!profile.IsActive) continue;

            var currentStatus = await GetCurrentAccessStatusAsync(profile);
            var previousStatus = _lastKnownStatus.GetValueOrDefault(profile.Id, ProfileAccessStatus.Allowed);

            // Vérifier si le statut a changé
            bool statusChanged = currentStatus != previousStatus;
            _lastKnownStatus[profile.Id] = currentStatus;

            // Appliquer les règles firewall
            foreach (var device in profile.Devices)
            {
                bool shouldBlock = currentStatus != ProfileAccessStatus.Allowed;
                
                if (shouldBlock)
                {
                    var result = await firewallEngine.BlockDeviceAsync(device.MacAddress, device.IpAddress);
                    if (result.Success && statusChanged)
                    {
                        _logger.LogInformation("Blocked device {Mac} for profile {Name} - Reason: {Status}",
                            device.MacAddress, profile.Name, currentStatus);
                        
                        // Incrémenter le compteur de blocages
                        await repo.IncrementBlockCountAsync(profile.Id);
                    }
                }
                else
                {
                    var result = await firewallEngine.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
                    if (result.Success && statusChanged)
                    {
                        _logger.LogInformation("Unblocked device {Mac} for profile {Name}",
                            device.MacAddress, profile.Name);
                    }
                }
            }

            // Notifier les clients si le statut a changé
            if (statusChanged)
            {
                var status = await BuildProfileStatusAsync(profile, repo, firewallEngine);
                await _hubContext.Clients.All.SendAsync("ProfileStatusChanged", status);
                
                _logger.LogInformation("Profile {Name} status changed: {Old} -> {New}",
                    profile.Name, previousStatus, currentStatus);
            }
        }
    }

    private async Task UpdateUsageTimeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
        var firewallEngine = scope.ServiceProvider.GetRequiredService<IFirewallEngine>();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        var profiles = await repo.GetAllProfilesAsync();

        foreach (var profile in profiles)
        {
            if (!profile.IsActive) continue;
            
            // Compter les appareils en ligne
            bool hasOnlineDevice = false;
            foreach (var device in profile.Devices)
            {
                var networkDevice = await deviceRepo.GetByMacAddressAsync(device.MacAddress);
                if (networkDevice != null && networkDevice.Status == DeviceStatus.Online)
                {
                    hasOnlineDevice = true;
                    break;
                }
            }

            // Si au moins un appareil est en ligne et non bloqué, ajouter du temps d'utilisation
            if (hasOnlineDevice)
            {
                var currentStatus = await GetCurrentAccessStatusAsync(profile);
                if (currentStatus == ProfileAccessStatus.Allowed)
                {
                    await repo.UpdateUsageAsync(profile.Id, 1); // +1 minute
                }
            }
        }
    }

    public async Task<ProfileAccessStatus> GetCurrentAccessStatusAsync(ChildProfile profile)
    {
        if (!profile.IsActive)
            return ProfileAccessStatus.Disabled;

        if (profile.IsPaused)
            return ProfileAccessStatus.Paused;

        if (!IsWithinAllowedTime(profile))
            return ProfileAccessStatus.BlockedBySchedule;

        // Vérifier la limite de temps quotidien
        if (profile.DailyTimeLimitMinutes > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
            var usage = await repo.GetTodayUsageAsync(profile.Id);
            
            if (usage != null && usage.MinutesUsed >= profile.DailyTimeLimitMinutes)
                return ProfileAccessStatus.BlockedByTimeLimit;
        }

        return ProfileAccessStatus.Allowed;
    }

    public bool IsWithinAllowedTime(ChildProfile profile)
    {
        if (profile.Schedules == null || !profile.Schedules.Any())
            return true; // Pas de planning = toujours autorisé

        var now = DateTime.Now;
        var currentDayOfWeek = (int)now.DayOfWeek;
        var currentTime = now.TimeOfDay;

        foreach (var schedule in profile.Schedules.Where(s => s.IsEnabled && s.DayOfWeek == currentDayOfWeek))
        {
            if (TimeSpan.TryParse(schedule.StartTime, out var start) &&
                TimeSpan.TryParse(schedule.EndTime, out var end))
            {
                // Gérer le cas où end < start (passage de minuit)
                if (end < start)
                {
                    if (currentTime >= start || currentTime <= end)
                        return true;
                }
                else
                {
                    if (currentTime >= start && currentTime <= end)
                        return true;
                }
            }
        }

        return false;
    }

    public async Task<IEnumerable<ProfileStatus>> GetAllProfileStatusAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
        var firewallEngine = scope.ServiceProvider.GetRequiredService<IFirewallEngine>();

        var profiles = await repo.GetAllProfilesAsync();
        var statuses = new List<ProfileStatus>();

        foreach (var profile in profiles)
        {
            var status = await BuildProfileStatusAsync(profile, repo, firewallEngine);
            statuses.Add(status);
        }

        return statuses;
    }

    public async Task<ProfileStatus?> GetProfileStatusAsync(int profileId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
        var firewallEngine = scope.ServiceProvider.GetRequiredService<IFirewallEngine>();

        var profile = await repo.GetProfileWithDetailsAsync(profileId);
        if (profile == null) return null;

        return await BuildProfileStatusAsync(profile, repo, firewallEngine);
    }

    private async Task<ProfileStatus> BuildProfileStatusAsync(
        ChildProfile profile,
        IParentalControlRepository repo,
        IFirewallEngine firewallEngine)
    {
        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        
        var status = new ProfileStatus
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            AvatarUrl = profile.AvatarUrl,
            Color = profile.Color,
            Status = await GetCurrentAccessStatusAsync(profile)
        };

        // Définir la raison du blocage
        status.BlockReason = status.Status switch
        {
            ProfileAccessStatus.BlockedBySchedule => "Hors plage horaire autorisée",
            ProfileAccessStatus.BlockedByTimeLimit => "Temps d'écran quotidien dépassé",
            ProfileAccessStatus.Paused => "Pause activée manuellement",
            ProfileAccessStatus.Disabled => "Profil désactivé",
            _ => null
        };

        // Calculer le temps restant
        if (profile.DailyTimeLimitMinutes > 0)
        {
            var usage = await repo.GetTodayUsageAsync(profile.Id);
            var usedMinutes = usage?.MinutesUsed ?? 0;
            status.RemainingMinutes = Math.Max(0, profile.DailyTimeLimitMinutes - usedMinutes);
            status.UsagePercentage = (int)Math.Min(100, (usedMinutes * 100.0 / profile.DailyTimeLimitMinutes));
        }

        // Trouver la prochaine plage horaire
        if (status.Status == ProfileAccessStatus.BlockedBySchedule && profile.Schedules.Any())
        {
            var nextSlot = FindNextAllowedSlot(profile.Schedules);
            if (nextSlot != null)
            {
                status.NextAllowedTime = $"{GetDayName(nextSlot.DayOfWeek)} {nextSlot.StartTime}";
            }
        }

        // Si autorisé, trouver quand la plage actuelle se termine
        if (status.Status == ProfileAccessStatus.Allowed && profile.Schedules.Any())
        {
            var currentSlot = FindCurrentSlot(profile.Schedules);
            if (currentSlot != null)
            {
                status.CurrentSlotEnds = currentSlot.EndTime;
            }
        }

        // Statut des appareils
        foreach (var device in profile.Devices)
        {
            var networkDevice = await deviceRepo.GetByMacAddressAsync(device.MacAddress);
            var isBlocked = await firewallEngine.IsDeviceBlockedAsync(device.MacAddress);
            
            status.Devices.Add(new ProfileDeviceStatus
            {
                MacAddress = device.MacAddress,
                DeviceName = device.DeviceName ?? networkDevice?.Hostname ?? device.MacAddress,
                IpAddress = device.IpAddress ?? networkDevice?.IpAddress,
                IsOnline = networkDevice?.Status == DeviceStatus.Online,
                IsBlocked = isBlocked
            });

            if (networkDevice?.Status == DeviceStatus.Online)
                status.IsOnline = true;
        }

        return status;
    }

    private TimeSchedule? FindCurrentSlot(ICollection<TimeSchedule> schedules)
    {
        var now = DateTime.Now;
        var currentDayOfWeek = (int)now.DayOfWeek;
        var currentTime = now.TimeOfDay;

        return schedules
            .Where(s => s.IsEnabled && s.DayOfWeek == currentDayOfWeek)
            .FirstOrDefault(s =>
            {
                if (TimeSpan.TryParse(s.StartTime, out var start) &&
                    TimeSpan.TryParse(s.EndTime, out var end))
                {
                    return currentTime >= start && currentTime <= end;
                }
                return false;
            });
    }

    private TimeSchedule? FindNextAllowedSlot(ICollection<TimeSchedule> schedules)
    {
        var now = DateTime.Now;
        var currentDayOfWeek = (int)now.DayOfWeek;
        var currentTime = now.TimeOfDay;

        // Chercher dans les 7 prochains jours
        for (int i = 0; i < 7; i++)
        {
            var dayToCheck = (currentDayOfWeek + i) % 7;
            var slotsForDay = schedules
                .Where(s => s.IsEnabled && s.DayOfWeek == dayToCheck)
                .OrderBy(s => s.StartTime)
                .ToList();

            foreach (var slot in slotsForDay)
            {
                if (TimeSpan.TryParse(slot.StartTime, out var start))
                {
                    // Si c'est aujourd'hui, vérifier que le créneau est dans le futur
                    if (i == 0 && start <= currentTime) continue;
                    return slot;
                }
            }
        }

        return null;
    }

    private string GetDayName(int dayOfWeek)
    {
        return dayOfWeek switch
        {
            0 => "Dim",
            1 => "Lun",
            2 => "Mar",
            3 => "Mer",
            4 => "Jeu",
            5 => "Ven",
            6 => "Sam",
            _ => ""
        };
    }

    public async Task<ChildProfile> CreateProfileAsync(ChildProfileDto dto)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();

        var profile = new ChildProfile
        {
            Name = dto.Name,
            AvatarUrl = dto.AvatarUrl,
            Color = dto.Color,
            DailyTimeLimitMinutes = dto.DailyTimeLimitMinutes,
            IsActive = dto.IsActive,
            BlockedMessage = dto.BlockedMessage
        };

        await repo.CreateProfileAsync(profile);

        // Ajouter les appareils
        foreach (var mac in dto.DeviceMacs)
        {
            await repo.AddDeviceToProfileAsync(profile.Id, new ProfileDevice { MacAddress = mac });
        }

        // Ajouter les schedules
        var schedules = dto.Schedules.Select(s => new TimeSchedule
        {
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            IsEnabled = s.IsEnabled
        });
        await repo.UpdateSchedulesAsync(profile.Id, schedules);

        // Ajouter les filtres web (catégories)
        var filters = new List<WebFilterRule>();
        foreach (var category in dto.BlockedCategories)
        {
            filters.Add(new WebFilterRule
            {
                FilterType = WebFilterType.Category,
                Value = category,
                IsEnabled = true
            });
        }
        foreach (var domain in dto.BlockedDomains)
        {
            filters.Add(new WebFilterRule
            {
                FilterType = WebFilterType.Domain,
                Value = domain,
                IsEnabled = true
            });
        }
        await repo.UpdateFiltersAsync(profile.Id, filters);

        // Notifier les clients
        var status = await GetProfileStatusAsync(profile.Id);
        await _hubContext.Clients.All.SendAsync("ProfileCreated", status);

        return profile;
    }

    public async Task<ChildProfile> UpdateProfileAsync(int id, ChildProfileDto dto)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();

        var profile = await repo.GetProfileWithDetailsAsync(id);
        if (profile == null)
            throw new KeyNotFoundException($"Profile {id} not found");

        profile.Name = dto.Name;
        profile.AvatarUrl = dto.AvatarUrl;
        profile.Color = dto.Color;
        profile.DailyTimeLimitMinutes = dto.DailyTimeLimitMinutes;
        profile.IsActive = dto.IsActive;
        profile.BlockedMessage = dto.BlockedMessage;

        await repo.UpdateProfileAsync(profile);

        // Mettre à jour les appareils
        var existingMacs = profile.Devices.Select(d => d.MacAddress.ToUpper()).ToHashSet();
        var newMacs = dto.DeviceMacs.Select(m => m.ToUpper()).ToHashSet();

        // Supprimer les appareils retirés
        foreach (var mac in existingMacs.Except(newMacs))
        {
            await repo.RemoveDeviceFromProfileAsync(id, mac);
        }

        // Ajouter les nouveaux appareils
        foreach (var mac in newMacs.Except(existingMacs))
        {
            await repo.AddDeviceToProfileAsync(id, new ProfileDevice { MacAddress = mac });
        }

        // Mettre à jour les schedules
        var schedules = dto.Schedules.Select(s => new TimeSchedule
        {
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            IsEnabled = s.IsEnabled
        });
        await repo.UpdateSchedulesAsync(id, schedules);

        // Mettre à jour les filtres
        var filters = new List<WebFilterRule>();
        foreach (var category in dto.BlockedCategories)
        {
            filters.Add(new WebFilterRule
            {
                FilterType = WebFilterType.Category,
                Value = category,
                IsEnabled = true
            });
        }
        foreach (var domain in dto.BlockedDomains)
        {
            filters.Add(new WebFilterRule
            {
                FilterType = WebFilterType.Domain,
                Value = domain,
                IsEnabled = true
            });
        }
        await repo.UpdateFiltersAsync(id, filters);

        // Forcer une vérification immédiate des règles
        await CheckAndEnforceSchedulesAsync();

        // Notifier les clients
        var status = await GetProfileStatusAsync(id);
        await _hubContext.Clients.All.SendAsync("ProfileUpdated", status);

        return profile;
    }

    public async Task DeleteProfileAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();
        var firewallEngine = scope.ServiceProvider.GetRequiredService<IFirewallEngine>();

        var profile = await repo.GetProfileWithDetailsAsync(id);
        if (profile == null) return;

        // Débloquer tous les appareils du profil avant suppression
        foreach (var device in profile.Devices)
        {
            await firewallEngine.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
        }

        await repo.DeleteProfileAsync(id);
        _lastKnownStatus.Remove(id);

        // Notifier les clients
        await _hubContext.Clients.All.SendAsync("ProfileDeleted", id);
    }

    public async Task<bool> TogglePauseAsync(int profileId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();

        var profile = await repo.GetProfileByIdAsync(profileId);
        if (profile == null) return false;

        profile.IsPaused = !profile.IsPaused;
        await repo.UpdateProfileAsync(profile);

        // Appliquer immédiatement
        await CheckAndEnforceSchedulesAsync();

        return profile.IsPaused;
    }

    public async Task<bool> SetPauseStateAsync(int profileId, bool isPaused)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IParentalControlRepository>();

        var profile = await repo.GetProfileByIdAsync(profileId);
        if (profile == null) return false;

        profile.IsPaused = isPaused;
        await repo.UpdateProfileAsync(profile);

        // Appliquer immédiatement
        await CheckAndEnforceSchedulesAsync();

        return isPaused;
    }
}
