using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepository;

    public DevicesController(IDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetAll()
    {
        var devices = await _deviceRepository.GetAllAsync();
        return Ok(devices);
    }

    [HttpGet("online")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetOnline()
    {
        var devices = await _deviceRepository.GetOnlineDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("unknown")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetUnknown()
    {
        var devices = await _deviceRepository.GetUnknownDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<NetworkDevice>> GetById(int id)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound();
        return Ok(device);
    }

    [HttpPost("{id}/trust")]
    public async Task<IActionResult> SetTrusted(int id, [FromBody] TrustRequest request)
    {
        var result = await _deviceRepository.SetTrustedAsync(id, request.Trusted);
        if (!result) return NotFound();
        return Ok();
    }

    [HttpPost("{id}/known")]
    public async Task<IActionResult> SetKnown(int id, [FromBody] KnownRequest request)
    {
        var result = await _deviceRepository.SetKnownAsync(id, request.Known, request.Description);
        if (!result) return NotFound();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _deviceRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }
}

public class TrustRequest
{
    public bool Trusted { get; set; }
}

public class KnownRequest
{
    public bool Known { get; set; }
    public string? Description { get; set; }
}
