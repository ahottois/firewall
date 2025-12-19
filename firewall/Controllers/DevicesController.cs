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

    // Reseaux a ignorer (Docker, virtualisation, etc.)
    private static readonly string[] IgnoredSubnets = new[]
    {
        "172.17.", "172.18.", "172.19.", "172.20.", "172.21.",
        "172.22.", "172.23.", "172.24.", "172.25.", "172.26.",
        "172.27.", "172.28.", "172.29.", "172.30.", "172.31.",
        "169.254.", "127."
    };

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

    /// <summary>
    /// Lancer un scan reseau complet
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> StartNetworkScan()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("SCAN RESEAU: Demande recue via API");
        _logger.LogInformation("========================================");
        
        try
        {
            // Executer le scan et attendre le resultat
            var devicesFound = await _discoveryService.ScanNetworkAsync();
            
            // Verifier combien d'appareils sont en base apres le scan
            var allDevices = await _deviceRepository.GetAllAsync();
            var deviceCount = allDevices.Count();
            
            _logger.LogInformation("SCAN TERMINE: {Found} appareils trouves, {Total} en base", devicesFound, deviceCount);
            
            return Ok(new { 
                message = $"Scan termine: {devicesFound} appareil(s) decouvert(s)",
                devicesFound = devicesFound,
                totalInDatabase = deviceCount,
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERREUR lors du scan reseau");
            return StatusCode(500, new { 
                message = "Erreur lors du scan reseau: " + ex.Message,
                success = false
            });
        }
    }

    /// <summary>
    /// Nettoyer les appareils fantomes (Docker, MAC random, etc.) ET les doublons
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupPhantomDevices()
    {
        _logger.LogInformation("CLEANUP: Demarrage du nettoyage des appareils fantomes et doublons");
        
        try
        {
            // Etape 1: Supprimer les doublons
            var duplicatesRemoved = await _deviceRepository.RemoveDuplicatesAsync();
            if (duplicatesRemoved > 0)
            {
                _logger.LogInformation("CLEANUP: {Count} doublons supprimes", duplicatesRemoved);
            }

            // Etape 2: Supprimer les appareils fantomes
            var allDevices = await _deviceRepository.GetAllAsync();
            var toDelete = new List<NetworkDevice>();
            
            foreach (var device in allDevices)
            {
                bool shouldDelete = false;
                string reason = "";

                // Verifier si l'IP est sur un reseau Docker
                if (!string.IsNullOrEmpty(device.IpAddress))
                {
                    foreach (var subnet in IgnoredSubnets)
                    {
                        if (device.IpAddress.StartsWith(subnet))
                        {
                            shouldDelete = true;
                            reason = $"Reseau Docker/virtuel ({subnet}x)";
                            break;
                        }
                    }
                }

                // Verifier si la MAC est localement administree ET sur reseau Docker
                if (!shouldDelete && IsLocallyAdministeredMac(device.MacAddress))
                {
                    if (!string.IsNullOrEmpty(device.IpAddress))
                    {
                        foreach (var subnet in IgnoredSubnets)
                        {
                            if (device.IpAddress.StartsWith(subnet))
                            {
                                shouldDelete = true;
                                reason = "MAC random sur reseau Docker";
                                break;
                            }
                        }
                    }
                }

                if (shouldDelete)
                {
                    toDelete.Add(device);
                    _logger.LogInformation("  CLEANUP: {Mac} ({Ip}) - {Reason}", 
                        device.MacAddress, device.IpAddress, reason);
                }
            }

            // Supprimer les appareils fantomes
            int phantomsDeleted = 0;
            foreach (var device in toDelete)
            {
                try
                {
                    await _deviceRepository.DeleteAsync(device.Id);
                    phantomsDeleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CLEANUP: Erreur suppression {Mac}", device.MacAddress);
                }
            }

            var totalDeleted = duplicatesRemoved + phantomsDeleted;
            _logger.LogInformation("CLEANUP TERMINE: {Duplicates} doublons + {Phantoms} fantomes = {Total} supprimes", 
                duplicatesRemoved, phantomsDeleted, totalDeleted);

            return Ok(new
            {
                message = $"Nettoyage termine: {duplicatesRemoved} doublons + {phantomsDeleted} fantomes = {totalDeleted} supprimes",
                duplicatesRemoved = duplicatesRemoved,
                phantomsDeleted = phantomsDeleted,
                totalDeleted = totalDeleted,
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLEANUP ERREUR");
            return StatusCode(500, new { message = ex.Message, success = false });
        }
    }

    /// <summary>
    /// Verifie si une adresse MAC est localement administree (randomisee)
    /// </summary>
    private static bool IsLocallyAdministeredMac(string? mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 2)
            return false;

        var cleanMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (cleanMac.Length < 2)
            return false;

        if (!int.TryParse(cleanMac.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int firstByte))
            return false;

        // Bit 1 (U/L) = 1 signifie localement administree
        return (firstByte & 0x02) != 0;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkDevice>>> GetAll()
    {
        var devices = await _deviceRepository.GetAllAsync();
        var count = devices.Count();
        _logger.LogInformation("API GetAll: {Count} appareils retournes", count);
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

    /// <summary>
    /// Purger tous les appareils de la base de donnees
    /// </summary>
    [HttpDelete("purge")]
    public async Task<IActionResult> PurgeAllDevices()
    {
        _logger.LogWarning("PURGE: Suppression de tous les appareils demandee");
        
        try
        {
            // D'abord debloquer tous les appareils bloques au niveau firewall
            var blockedDevices = await _deviceRepository.GetBlockedDevicesAsync();
            foreach (var device in blockedDevices)
            {
                try
                {
                    await _blockingService.UnblockDeviceAsync(device.MacAddress, device.IpAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PURGE: Erreur deblocage {Mac}", device.MacAddress);
                }
            }

            // Supprimer tous les appareils
            var allDevices = await _deviceRepository.GetAllAsync();
            int deletedCount = 0;
            
            foreach (var device in allDevices)
            {
                try
                {
                    await _deviceRepository.DeleteAsync(device.Id);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PURGE: Erreur suppression {Mac}", device.MacAddress);
                }
            }

            _logger.LogInformation("PURGE TERMINE: {Count} appareils supprimes", deletedCount);

            return Ok(new
            {
                message = $"Base de donnees purgee: {deletedCount} appareils supprimes",
                deletedCount = deletedCount,
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PURGE ERREUR");
            return StatusCode(500, new { message = ex.Message, success = false });
        }
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
