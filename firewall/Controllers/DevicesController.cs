using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController(
    IDeviceRepository deviceRepository,
    IDeviceDiscoveryService discoveryService,
    INetworkBlockingService blockingService,
    IDeviceHubNotifier hubNotifier) : ControllerBase
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

    [HttpGet("blocked")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetBlocked()
    {
        var devices = await deviceRepository.GetBlockedDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<NetworkDevice>> GetById(int id)
    {
        var device = await deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound();
        return Ok(device);
    }

    /// <summary>
    /// Lancer un scan réseau complet
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> StartNetworkScan()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await discoveryService.ScanNetworkAsync();
            }
            catch (Exception)
            {
                // Log géré dans le service
            }
        });

        return Accepted(new { message = "Scan réseau démarré" });
    }

    /// <summary>
    /// Bloquer un appareil sur le réseau
    /// </summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockDevice(int id)
    {
        var device = await deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound(new { message = "Appareil non trouvé" });

        if (device.Status == DeviceStatus.Blocked)
            return BadRequest(new { message = "Appareil déjà bloqué" });

        var success = await blockingService.BlockDeviceAsync(device.MacAddress, device.IpAddress);
        if (!success && blockingService.IsSupported)
        {
            return StatusCode(500, new { message = "Échec du blocage réseau" });
        }

        // Mettre à jour le statut en base
        device.Status = DeviceStatus.Blocked;
        await deviceRepository.UpdateStatusAsync(id, DeviceStatus.Blocked);

        // Notifier les clients SignalR
        await hubNotifier.NotifyDeviceBlocked(device);

        return Ok(new { message = "Appareil bloqué", device });
    }

    /// <summary>
    /// Débloquer un appareil sur le réseau
    /// </summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockDevice(int id)
    {
        var device = await deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound(new { message = "Appareil non trouvé" });

        if (device.Status != DeviceStatus.Blocked)
            return BadRequest(new { message = "Appareil non bloqué" });

        var success = await blockingService.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
        if (!success && blockingService.IsSupported)
        {
            return StatusCode(500, new { message = "Échec du déblocage réseau" });
        }

        // Mettre à jour le statut en base (remettre à Unknown, le scanner déterminera le vrai statut)
        device.Status = DeviceStatus.Unknown;
        await deviceRepository.UpdateStatusAsync(id, DeviceStatus.Unknown);

        // Notifier les clients SignalR
        await hubNotifier.NotifyDeviceUnblocked(device);

        return Ok(new { message = "Appareil débloqué", device });
    }

    [HttpPost("{id}/trust")]
    public async Task<IActionResult> SetTrusted(int id, [FromBody] TrustRequest request)
    {
        var result = await deviceRepository.SetTrustedAsync(id, request.Trusted);
        if (!result) return NotFound();
        
        var device = await deviceRepository.GetByIdAsync(id);
        if (device != null)
            await hubNotifier.NotifyDeviceUpdated(device);
        
        return Ok();
    }

    [HttpPost("{id}/known")]
    public async Task<IActionResult> SetKnown(int id, [FromBody] KnownRequest request)
    {
        var result = await deviceRepository.SetKnownAsync(id, request.Known, request.Description);
        if (!result) return NotFound();
        
        var device = await deviceRepository.GetByIdAsync(id);
        if (device != null)
            await hubNotifier.NotifyDeviceUpdated(device);
        
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
        
        var device = await deviceRepository.GetByIdAsync(id);
        if (device != null)
            await hubNotifier.NotifyDeviceUpdated(device);
        
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
        
        await hubNotifier.NotifyDeviceDiscovered(saved);
        
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
