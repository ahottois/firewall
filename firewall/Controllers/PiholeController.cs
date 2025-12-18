using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PiholeController : ControllerBase
{
    private readonly IPiholeService _piholeService;

    public PiholeController(IPiholeService piholeService)
    {
        _piholeService = piholeService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = await _piholeService.GetStatusAsync();
        return Ok(status);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _piholeService.GetSummaryAsync();
        if (summary == null) 
        {
            // Return empty summary with status unavailable to avoid 404 errors in frontend console
            return Ok(new PiholeSummary { Status = "unavailable" });
        }
        return Ok(summary);
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install()
    {
        if (!_piholeService.IsLinux)
        {
            return BadRequest(new { message = "Pi-hole ne peut être installé que sur Linux." });
        }
        
        var result = await _piholeService.InstallAsync();
        if (!result) return BadRequest(new { message = "L'installation a échoué ou est déjà en cours." });
        return Ok(new { message = "Installation démarrée" });
    }

    [HttpPost("uninstall")]
    public async Task<IActionResult> Uninstall()
    {
        if (!_piholeService.IsLinux)
        {
            return BadRequest(new { message = "Opération non supportée sur ce système." });
        }
        
        var result = await _piholeService.UninstallAsync();
        if (!result) return BadRequest(new { message = "La désinstallation a échoué" });
        return Ok(new { message = "Désinstallation terminée" });
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable()
    {
        if (!_piholeService.IsLinux)
        {
            return BadRequest(new { message = "Opération non supportée sur ce système." });
        }
        
        var result = await _piholeService.EnableAsync();
        if (!result) return BadRequest(new { message = "Impossible d'activer Pi-hole" });
        return Ok(new { message = "Pi-hole activé" });
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] DisableRequest? request)
    {
        if (!_piholeService.IsLinux)
        {
            return BadRequest(new { message = "Opération non supportée sur ce système." });
        }
        
        var result = await _piholeService.DisableAsync(request?.Duration);
        if (!result) return BadRequest(new { message = "Impossible de désactiver Pi-hole" });
        
        var msg = request?.Duration.HasValue == true 
            ? $"Pi-hole désactivé pour {request.Duration} secondes" 
            : "Pi-hole désactivé";
        return Ok(new { message = msg });
    }

    [HttpPost("password")]
    public async Task<IActionResult> SetPassword([FromBody] PasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.Password)) 
            return BadRequest(new { message = "Mot de passe requis" });
        
        var result = await _piholeService.SetPasswordAsync(request.Password);
        if (!result) return BadRequest(new { message = "Impossible de modifier le mot de passe" });
        return Ok(new { message = "Mot de passe mis à jour" });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs()
    {
        var logs = await _piholeService.GetInstallLogAsync();
        return Ok(new { logs });
    }
}

public class PasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

public class DisableRequest
{
    public int? Duration { get; set; }
}
