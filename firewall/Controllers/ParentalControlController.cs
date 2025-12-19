using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/parental")]
public class ParentalControlController : ControllerBase
{
    private readonly IParentalControlService _parentalControlService;
    private readonly IParentalControlRepository _repository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ILogger<ParentalControlController> _logger;

    public ParentalControlController(
        IParentalControlService parentalControlService,
        IParentalControlRepository repository,
        IDeviceRepository deviceRepository,
        ILogger<ParentalControlController> logger)
    {
        _parentalControlService = parentalControlService;
        _repository = repository;
        _deviceRepository = deviceRepository;
        _logger = logger;
    }

    #region Profiles

    /// <summary>
    /// Récupère tous les profils avec leur statut actuel
    /// </summary>
    [HttpGet("profiles")]
    public async Task<ActionResult<IEnumerable<ProfileStatus>>> GetAllProfiles()
    {
        var statuses = await _parentalControlService.GetAllProfileStatusAsync();
        return Ok(statuses);
    }

    /// <summary>
    /// Récupère un profil spécifique avec son statut
    /// </summary>
    [HttpGet("profiles/{id}")]
    public async Task<ActionResult<ProfileStatus>> GetProfile(int id)
    {
        var status = await _parentalControlService.GetProfileStatusAsync(id);
        if (status == null)
            return NotFound(new { message = "Profil non trouvé" });

        return Ok(status);
    }

    /// <summary>
    /// Récupère les détails complets d'un profil (pour édition)
    /// </summary>
    [HttpGet("profiles/{id}/details")]
    public async Task<ActionResult<ChildProfile>> GetProfileDetails(int id)
    {
        var profile = await _repository.GetProfileWithDetailsAsync(id);
        if (profile == null)
            return NotFound(new { message = "Profil non trouvé" });

        return Ok(profile);
    }

    /// <summary>
    /// Crée un nouveau profil enfant
    /// </summary>
    [HttpPost("profiles")]
    public async Task<ActionResult<ProfileStatus>> CreateProfile([FromBody] ChildProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Le nom du profil est requis" });

        try
        {
            var profile = await _parentalControlService.CreateProfileAsync(dto);
            var status = await _parentalControlService.GetProfileStatusAsync(profile.Id);
            
            _logger.LogInformation("Profile created: {Name} (ID: {Id})", profile.Name, profile.Id);
            return CreatedAtAction(nameof(GetProfile), new { id = profile.Id }, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile");
            return BadRequest(new { message = "Erreur lors de la création du profil: " + ex.Message });
        }
    }

    /// <summary>
    /// Met à jour un profil existant
    /// </summary>
    [HttpPut("profiles/{id}")]
    public async Task<ActionResult<ProfileStatus>> UpdateProfile(int id, [FromBody] ChildProfileDto dto)
    {
        try
        {
            var profile = await _parentalControlService.UpdateProfileAsync(id, dto);
            var status = await _parentalControlService.GetProfileStatusAsync(profile.Id);
            
            return Ok(status);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Profil non trouvé" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile {Id}", id);
            return BadRequest(new { message = "Erreur lors de la mise à jour: " + ex.Message });
        }
    }

    /// <summary>
    /// Supprime un profil
    /// </summary>
    [HttpDelete("profiles/{id}")]
    public async Task<ActionResult> DeleteProfile(int id)
    {
        try
        {
            await _parentalControlService.DeleteProfileAsync(id);
            return Ok(new { message = "Profil supprimé" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile {Id}", id);
            return BadRequest(new { message = "Erreur lors de la suppression: " + ex.Message });
        }
    }

    #endregion

    #region Pause Control

    /// <summary>
    /// Active/désactive la pause pour un profil
    /// </summary>
    [HttpPost("profiles/{id}/toggle-pause")]
    public async Task<ActionResult> TogglePause(int id)
    {
        try
        {
            var isPaused = await _parentalControlService.TogglePauseAsync(id);
            var status = await _parentalControlService.GetProfileStatusAsync(id);
            
            return Ok(new { 
                isPaused, 
                message = isPaused ? "Accès Internet coupé" : "Accès Internet rétabli",
                status 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling pause for profile {Id}", id);
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    /// <summary>
    /// Active la pause (coupe tout)
    /// </summary>
    [HttpPost("profiles/{id}/pause")]
    public async Task<ActionResult> Pause(int id)
    {
        try
        {
            await _parentalControlService.SetPauseStateAsync(id, true);
            var status = await _parentalControlService.GetProfileStatusAsync(id);
            
            return Ok(new { message = "Accès Internet coupé", status });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    /// <summary>
    /// Désactive la pause (rétablit l'accès selon le planning)
    /// </summary>
    [HttpPost("profiles/{id}/unpause")]
    public async Task<ActionResult> Unpause(int id)
    {
        try
        {
            await _parentalControlService.SetPauseStateAsync(id, false);
            var status = await _parentalControlService.GetProfileStatusAsync(id);
            
            return Ok(new { message = "Accès Internet rétabli", status });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    #endregion

    #region Devices

    /// <summary>
    /// Ajoute un appareil à un profil
    /// </summary>
    [HttpPost("profiles/{profileId}/devices")]
    public async Task<ActionResult> AddDevice(int profileId, [FromBody] AddDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MacAddress))
            return BadRequest(new { message = "L'adresse MAC est requise" });

        try
        {
            var device = new ProfileDevice
            {
                MacAddress = request.MacAddress.ToUpper(),
                DeviceName = request.DeviceName,
                IpAddress = request.IpAddress
            };

            await _repository.AddDeviceToProfileAsync(profileId, device);
            
            // Forcer une vérification des règles
            await _parentalControlService.CheckAndEnforceSchedulesAsync();
            
            return Ok(new { message = "Appareil ajouté au profil", device });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    /// <summary>
    /// Retire un appareil d'un profil
    /// </summary>
    [HttpDelete("profiles/{profileId}/devices/{macAddress}")]
    public async Task<ActionResult> RemoveDevice(int profileId, string macAddress)
    {
        try
        {
            await _repository.RemoveDeviceFromProfileAsync(profileId, macAddress);
            
            // Forcer une vérification des règles
            await _parentalControlService.CheckAndEnforceSchedulesAsync();
            
            return Ok(new { message = "Appareil retiré du profil" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    /// <summary>
    /// Liste les appareils disponibles (non assignés à un profil)
    /// </summary>
    [HttpGet("available-devices")]
    public async Task<ActionResult> GetAvailableDevices()
    {
        var allDevices = await _deviceRepository.GetAllAsync();
        var assignedMacs = new HashSet<string>();

        var profiles = await _repository.GetAllProfilesAsync();
        foreach (var profile in profiles)
        {
            foreach (var device in profile.Devices)
            {
                assignedMacs.Add(device.MacAddress.ToUpper());
            }
        }

        var availableDevices = allDevices
            .Where(d => !assignedMacs.Contains(d.MacAddress.ToUpper()))
            .Select(d => new
            {
                d.MacAddress,
                d.IpAddress,
                d.Hostname,
                d.Vendor,
                d.Description,
                IsOnline = d.Status == DeviceStatus.Online
            });

        return Ok(availableDevices);
    }

    #endregion

    #region Schedules

    /// <summary>
    /// Récupère le planning d'un profil
    /// </summary>
    [HttpGet("profiles/{profileId}/schedules")]
    public async Task<ActionResult> GetSchedules(int profileId)
    {
        var schedules = await _repository.GetSchedulesByProfileAsync(profileId);
        return Ok(schedules);
    }

    /// <summary>
    /// Met à jour le planning complet d'un profil
    /// </summary>
    [HttpPut("profiles/{profileId}/schedules")]
    public async Task<ActionResult> UpdateSchedules(int profileId, [FromBody] List<TimeScheduleDto> schedules)
    {
        try
        {
            var entities = schedules.Select(s => new TimeSchedule
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                IsEnabled = s.IsEnabled
            });

            await _repository.UpdateSchedulesAsync(profileId, entities);
            
            // Forcer une vérification des règles
            await _parentalControlService.CheckAndEnforceSchedulesAsync();
            
            return Ok(new { message = "Planning mis à jour" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    #endregion

    #region Web Filters

    /// <summary>
    /// Récupère les catégories de filtrage disponibles
    /// </summary>
    [HttpGet("filter-categories")]
    public ActionResult GetFilterCategories()
    {
        var categories = WebFilterCategories.Categories.Select(kvp => new
        {
            Key = kvp.Key,
            kvp.Value.Name,
            kvp.Value.Description,
            kvp.Value.Icon,
            kvp.Value.Color
        });

        return Ok(categories);
    }

    /// <summary>
    /// Récupère les filtres d'un profil
    /// </summary>
    [HttpGet("profiles/{profileId}/filters")]
    public async Task<ActionResult> GetFilters(int profileId)
    {
        var filters = await _repository.GetFiltersByProfileAsync(profileId);
        return Ok(filters);
    }

    /// <summary>
    /// Ajoute un filtre à un profil
    /// </summary>
    [HttpPost("profiles/{profileId}/filters")]
    public async Task<ActionResult> AddFilter(int profileId, [FromBody] AddFilterRequest request)
    {
        try
        {
            var filter = new WebFilterRule
            {
                ProfileId = profileId,
                FilterType = request.FilterType,
                Value = request.Value,
                Description = request.Description,
                IsEnabled = true
            };

            await _repository.AddFilterAsync(filter);
            return Ok(new { message = "Filtre ajouté", filter });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    /// <summary>
    /// Supprime un filtre
    /// </summary>
    [HttpDelete("filters/{filterId}")]
    public async Task<ActionResult> RemoveFilter(int filterId)
    {
        try
        {
            await _repository.RemoveFilterAsync(filterId);
            return Ok(new { message = "Filtre supprimé" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Erreur: " + ex.Message });
        }
    }

    #endregion

    #region Usage Statistics

    /// <summary>
    /// Récupère l'utilisation du jour pour un profil
    /// </summary>
    [HttpGet("profiles/{profileId}/usage/today")]
    public async Task<ActionResult> GetTodayUsage(int profileId)
    {
        var usage = await _repository.GetTodayUsageAsync(profileId);
        return Ok(usage ?? new UsageLog { ProfileId = profileId, Date = DateTime.UtcNow.Date });
    }

    /// <summary>
    /// Récupère l'historique d'utilisation d'un profil
    /// </summary>
    [HttpGet("profiles/{profileId}/usage/history")]
    public async Task<ActionResult> GetUsageHistory(int profileId, [FromQuery] int days = 7)
    {
        var history = await _repository.GetUsageHistoryAsync(profileId, days);
        return Ok(history);
    }

    #endregion
}

#region Request DTOs

public class AddDeviceRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
}

public class AddFilterRequest
{
    public WebFilterType FilterType { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}

#endregion
