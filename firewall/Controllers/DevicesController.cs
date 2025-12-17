using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController(IDeviceRepository deviceRepository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetAll()
    {
        var devices = await deviceRepository.GetAllAsync();
        return Ok(devices);
    }

    [HttpGet("online")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetOnline()
    {
        var devices = await deviceRepository.GetOnlineDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("unknown")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetUnknown()
    {
        var devices = await deviceRepository.GetUnknownDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<NetworkDevice>> GetById(int id)
    {
        var device = await deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound();
        return Ok(device);
    }

    [HttpPost("{id}/trust")]
    public async Task<IActionResult> SetTrusted(int id, [FromBody] TrustRequest request)
    {
        var result = await deviceRepository.SetTrustedAsync(id, request.Trusted);
        if (!result) return NotFound();
        return Ok();
    }

    [HttpPost("{id}/known")]
    public async Task<IActionResult> SetKnown(int id, [FromBody] KnownRequest request)
    {
        var result = await deviceRepository.SetKnownAsync(id, request.Known, request.Description);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Mettre à jour les informations d'un appareil
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDeviceRequest request)
    {
        var result = await deviceRepository.UpdateDeviceInfoAsync(id, request);
        if (!result) return NotFound();
        return Ok(new { message = "Appareil mis à jour" });
    }

    /// <summary>
    /// Ajouter un appareil manuellement
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<NetworkDevice>> Create([FromBody] CreateDeviceRequest request)
    {
        if (string.IsNullOrEmpty(request.MacAddress))
        {
            return BadRequest(new { message = "L'adresse MAC est requise" });
        }

        var device = new NetworkDevice
        {
            MacAddress = request.MacAddress.ToUpperInvariant(),
            IpAddress = request.IpAddress,
            Vendor = request.Vendor,
            Description = request.Description,
            IsKnown = true,
            IsTrusted = request.IsTrusted,
            Status = DeviceStatus.Unknown
        };

        var saved = await deviceRepository.AddOrUpdateAsync(device);
        return Ok(saved);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await deviceRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }
}

public record TrustRequest(bool Trusted);

public record KnownRequest(bool Known, string? Description);

public record UpdateDeviceRequest(string? IpAddress, string? Vendor, string? Description, bool? IsKnown, bool? IsTrusted);

public record CreateDeviceRequest(string MacAddress, string? IpAddress, string? Vendor, string? Description, bool IsTrusted);
