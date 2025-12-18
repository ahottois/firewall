using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;
using NetworkFirewall.Services;
using NetworkFirewall.Services.Firewall;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly INetworkBlockingService _blockingService;
    private readonly IDeviceHubNotifier _hubNotifier;
    private readonly ISecurityLogService _securityLogService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IDeviceRepository deviceRepository,
        IDeviceDiscoveryService discoveryService,
        INetworkBlockingService blockingService,
        IDeviceHubNotifier hubNotifier,
        ISecurityLogService securityLogService,
        ILogger<DevicesController> logger)
    {
        _deviceRepository = deviceRepository;
        _discoveryService = discoveryService;
        _blockingService = blockingService;
        _hubNotifier = hubNotifier;
        _securityLogService = securityLogService;
        _logger = logger;
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

    [HttpGet("blocked")]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetBlocked()
    {
        var devices = await _deviceRepository.GetBlockedDevicesAsync();
        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<NetworkDevice>> GetById(int id)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null) return NotFound();
        return Ok(device);
    }

    /// <summary>
    /// Lancer un scan réseau complet
    /// </summary>
    [HttpPost("scan")]
    public IActionResult StartNetworkScan()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _discoveryService.ScanNetworkAsync();
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
    public async Task<IActionResult> BlockDevice(int id, [FromBody] BlockDeviceRequest? request = null)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null) 
            return NotFound(new { message = "Appareil non trouvé" });

        // Protection contre l'auto-blocage
        var selfBlockCheck = CheckSelfBlocking(device);
        if (selfBlockCheck.IsSelfBlock)
        {
            _logger.LogWarning("Tentative d'auto-blocage détectée pour l'appareil {Mac} depuis {ClientIp}", 
                device.MacAddress, selfBlockCheck.ClientIp);
            return BadRequest(new { 
                message = "Impossible de bloquer cet appareil : vous êtes connecté depuis cette adresse IP.",
                errorCode = "SELF_BLOCK_PREVENTED",
                clientIp = selfBlockCheck.ClientIp
            });
        }

        if (device.IsBlocked || device.Status == DeviceStatus.Blocked)
            return BadRequest(new { message = "Appareil déjà bloqué" });

        // Appliquer la règle de firewall
        var success = await _blockingService.BlockDeviceAsync(device.MacAddress, device.IpAddress);
        if (!success && _blockingService.IsSupported)
        {
            return StatusCode(500, new { message = "Échec du blocage réseau au niveau du firewall" });
        }

        // Mettre à jour le statut en base avec persistance
        var reason = request?.Reason ?? "Bloqué manuellement";
        await _deviceRepository.SetBlockedAsync(id, true, reason);
        
        // Recharger l'appareil pour avoir les valeurs mises à jour
        device = await _deviceRepository.GetByIdAsync(id);

        // Logger l'événement de sécurité
        var deviceName = device?.Description ?? device?.Hostname ?? device?.Vendor;
        await _securityLogService.LogDeviceBlockedAsync(
            device?.MacAddress ?? "Unknown",
            device?.IpAddress,
            deviceName,
            reason);

        // Notifier les clients SignalR
        if (device != null)
            await _hubNotifier.NotifyDeviceBlocked(device);

        _logger.LogInformation("Appareil bloqué: {Mac} ({Ip}) - Raison: {Reason}", 
            device?.MacAddress, device?.IpAddress, reason);

        return Ok(new { 
            message = "Appareil bloqué avec succès", 
            device,
            firewallApplied = _blockingService.IsSupported
        });
    }

    /// <summary>
    /// Débloquer un appareil sur le réseau
    /// </summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockDevice(int id)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null) 
            return NotFound(new { message = "Appareil non trouvé" });

        if (!device.IsBlocked && device.Status != DeviceStatus.Blocked)
            return BadRequest(new { message = "Appareil non bloqué" });

        // Supprimer la règle de firewall
        var success = await _blockingService.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
        if (!success && _blockingService.IsSupported)
        {
            return StatusCode(500, new { message = "Échec du déblocage réseau au niveau du firewall" });
        }

        // Mettre à jour le statut en base
        await _deviceRepository.SetBlockedAsync(id, false);
        
        // Recharger l'appareil
        var macAddress = device.MacAddress;
        var ipAddress = device.IpAddress;
        var deviceName = device.Description ?? device.Hostname ?? device.Vendor;
        device = await _deviceRepository.GetByIdAsync(id);

        // Logger l'événement de sécurité
        await _securityLogService.LogDeviceUnblockedAsync(macAddress, ipAddress, deviceName);

        // Notifier les clients SignalR
        if (device != null)
            await _hubNotifier.NotifyDeviceUnblocked(device);

        _logger.LogInformation("Appareil débloqué: {Mac} ({Ip})", macAddress, ipAddress);

        return Ok(new { 
            message = "Appareil débloqué avec succès", 
            device,
            firewallApplied = _blockingService.IsSupported
        });
    }

    /// <summary>
    /// Vérifie si l'utilisateur essaie de bloquer sa propre connexion
    /// </summary>
    private SelfBlockCheckResult CheckSelfBlocking(NetworkDevice device)
    {
        var result = new SelfBlockCheckResult();
        
        try
        {
            // Récupérer l'IP du client
            var clientIp = GetClientIpAddress();
            result.ClientIp = clientIp;

            if (string.IsNullOrEmpty(clientIp))
                return result;

            // Vérifier si l'IP du client correspond à l'appareil à bloquer
            if (!string.IsNullOrEmpty(device.IpAddress) && 
                device.IpAddress.Equals(clientIp, StringComparison.OrdinalIgnoreCase))
            {
                result.IsSelfBlock = true;
                return result;
            }

            // Vérifier également les IPs locales du serveur
            var localIps = GetLocalIpAddresses();
            if (localIps.Any(ip => ip.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)))
            {
                // L'appareil est le serveur lui-même
                result.IsSelfBlock = true;
                result.IsServerSelf = true;
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Erreur lors de la vérification d'auto-blocage");
        }

        return result;
    }

    private string? GetClientIpAddress()
    {
        // Vérifier les headers de proxy
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Récupérer l'IP de connexion
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            // Convertir IPv6 localhost en IPv4
            if (IPAddress.IsLoopback(remoteIp))
                return "127.0.0.1";

            // Mapper IPv6 vers IPv4 si possible
            if (remoteIp.IsIPv4MappedToIPv6)
                return remoteIp.MapToIPv4().ToString();

            return remoteIp.ToString();
        }

        return null;
    }

    private static IEnumerable<string> GetLocalIpAddresses()
    {
        var addresses = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    addresses.Add(ua.Address.ToString());
                }
            }
        }

        return addresses;
    }

    [HttpPost("{id}/trust")]
    public async Task<IActionResult> SetTrusted(int id, [FromBody] TrustRequest request)
    {
        var result = await _deviceRepository.SetTrustedAsync(id, request.Trusted);
        if (!result) return NotFound();
        
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device != null)
            await _hubNotifier.NotifyDeviceUpdated(device);
        
        return Ok();
    }

    [HttpPost("{id}/known")]
    public async Task<IActionResult> SetKnown(int id, [FromBody] KnownRequest request)
    {
        var result = await _deviceRepository.SetKnownAsync(id, request.Known, request.Description);
        if (!result) return NotFound();
        
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device != null)
            await _hubNotifier.NotifyDeviceUpdated(device);
        
        return Ok();
    }

    /// <summary>
    /// Mettre à jour les informations d'un appareil
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDeviceRequest request)
    {
        var result = await _deviceRepository.UpdateDeviceInfoAsync(id, request);
        if (!result) return NotFound();
        
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device != null)
            await _hubNotifier.NotifyDeviceUpdated(device);
        
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

        var saved = await _deviceRepository.AddOrUpdateAsync(device);
        
        await _hubNotifier.NotifyDeviceDiscovered(saved);
        
        return Ok(saved);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Avant de supprimer, débloquer si nécessaire
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device != null && (device.IsBlocked || device.Status == DeviceStatus.Blocked))
        {
            await _blockingService.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
        }

        var result = await _deviceRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Obtenir les informations sur le moteur de firewall
    /// </summary>
    [HttpGet("firewall/status")]
    public IActionResult GetFirewallStatus()
    {
        return Ok(new
        {
            isSupported = _blockingService.IsSupported,
            platform = Environment.OSVersion.Platform.ToString(),
            osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        });
    }
}

public record TrustRequest(bool Trusted);
public record KnownRequest(bool Known, string? Description);
public record UpdateDeviceRequest(string? IpAddress, string? Vendor, string? Description, bool? IsKnown, bool? IsTrusted);
public record CreateDeviceRequest(string MacAddress, string? IpAddress, string? Vendor, string? Description, bool IsTrusted);
public record BlockDeviceRequest(string? Reason);

internal class SelfBlockCheckResult
{
    public bool IsSelfBlock { get; set; }
    public bool IsServerSelf { get; set; }
    public string? ClientIp { get; set; }
}
