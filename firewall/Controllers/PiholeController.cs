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
        if (summary == null) return NotFound("Pi-hole stats not available");
        return Ok(summary);
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install()
    {
        var result = await _piholeService.InstallAsync();
        if (!result) return BadRequest("Installation failed to start or not supported on this OS.");
        return Ok(new { message = "Installation started" });
    }

    [HttpPost("uninstall")]
    public async Task<IActionResult> Uninstall()
    {
        var result = await _piholeService.UninstallAsync();
        if (!result) return BadRequest("Uninstallation failed");
        return Ok(new { message = "Uninstallation started" });
    }

    [HttpPost("password")]
    public async Task<IActionResult> SetPassword([FromBody] PasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.Password)) return BadRequest("Password required");
        
        var result = await _piholeService.SetPasswordAsync(request.Password);
        if (!result) return BadRequest("Failed to set password");
        return Ok(new { message = "Password updated" });
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
