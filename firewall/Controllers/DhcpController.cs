using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DhcpController : ControllerBase
{
    private readonly IDhcpService _dhcpService;
    private readonly ILogger<DhcpController> _logger;

    public DhcpController(IDhcpService dhcpService, ILogger<DhcpController> logger)
    {
        _dhcpService = dhcpService;
        _logger = logger;
    }

    /// <summary>
    /// Obtenir le statut du serveur DHCP
    /// </summary>
    [HttpGet("status")]
    public ActionResult<DhcpServerStatus> GetStatus()
    {
        return Ok(_dhcpService.GetStatus());
    }

    /// <summary>
    /// Obtenir la configuration DHCP
    /// </summary>
    [HttpGet("config")]
    public ActionResult<DhcpConfig> GetConfig()
    {
        return Ok(_dhcpService.GetConfig());
    }

    /// <summary>
    /// Mettre à jour la configuration DHCP
    /// </summary>
    [HttpPost("config")]
    public IActionResult UpdateConfig([FromBody] DhcpConfig config)
    {
        if (string.IsNullOrEmpty(config.RangeStart) || string.IsNullOrEmpty(config.RangeEnd))
        {
            return BadRequest(new { message = "La plage d'adresses est requise" });
        }

        // Valider les IPs
        try
        {
            var start = DhcpPacket.StringToIp(config.RangeStart);
            var end = DhcpPacket.StringToIp(config.RangeEnd);
            
            if (start >= end)
            {
                return BadRequest(new { message = "L'adresse de début doit être inférieure à l'adresse de fin" });
            }
            
            // Valider le masque de sous-réseau
            DhcpPacket.StringToIp(config.SubnetMask);
            
            // Valider la passerelle si spécifiée
            if (!string.IsNullOrEmpty(config.Gateway))
                DhcpPacket.StringToIp(config.Gateway);
            
            // Valider les DNS si spécifiés
            if (!string.IsNullOrEmpty(config.Dns1))
                DhcpPacket.StringToIp(config.Dns1);
            if (!string.IsNullOrEmpty(config.Dns2))
                DhcpPacket.StringToIp(config.Dns2);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Format d'adresse IP invalide" });
        }

        _dhcpService.UpdateConfig(config);
        _logger.LogInformation("DHCP: Configuration mise à jour - Enabled: {Enabled}, Range: {Start}-{End}",
            config.Enabled, config.RangeStart, config.RangeEnd);
        
        return Ok(new { message = "Configuration mise à jour" });
    }

    /// <summary>
    /// Activer le serveur DHCP
    /// </summary>
    [HttpPost("enable")]
    public IActionResult Enable()
    {
        var config = _dhcpService.GetConfig();
        config.Enabled = true;
        _dhcpService.UpdateConfig(config);
        return Ok(new { message = "Serveur DHCP activé" });
    }

    /// <summary>
    /// Désactiver le serveur DHCP
    /// </summary>
    [HttpPost("disable")]
    public IActionResult Disable()
    {
        var config = _dhcpService.GetConfig();
        config.Enabled = false;
        _dhcpService.UpdateConfig(config);
        return Ok(new { message = "Serveur DHCP désactivé" });
    }

    /// <summary>
    /// Obtenir la liste des baux actifs
    /// </summary>
    [HttpGet("leases")]
    public ActionResult<IEnumerable<DhcpLease>> GetLeases()
    {
        return Ok(_dhcpService.GetLeases());
    }

    /// <summary>
    /// Libérer un bail spécifique
    /// </summary>
    [HttpDelete("leases/{macAddress}")]
    public IActionResult ReleaseLease(string macAddress)
    {
        var normalizedMac = macAddress.ToUpperInvariant().Replace("-", ":");
        
        if (_dhcpService.ReleaseLease(normalizedMac))
        {
            return Ok(new { message = $"Bail libéré pour {normalizedMac}" });
        }
        
        return NotFound(new { message = "Bail non trouvé" });
    }

    /// <summary>
    /// Obtenir les réservations statiques
    /// </summary>
    [HttpGet("reservations")]
    public ActionResult<IEnumerable<DhcpStaticReservation>> GetReservations()
    {
        return Ok(_dhcpService.GetConfig().StaticReservations);
    }

    /// <summary>
    /// Ajouter une réservation statique
    /// </summary>
    [HttpPost("reservations")]
    public IActionResult AddReservation([FromBody] DhcpStaticReservation reservation)
    {
        if (string.IsNullOrEmpty(reservation.MacAddress))
        {
            return BadRequest(new { message = "L'adresse MAC est requise" });
        }

        if (string.IsNullOrEmpty(reservation.IpAddress))
        {
            return BadRequest(new { message = "L'adresse IP est requise" });
        }

        // Valider le format MAC
        var macParts = reservation.MacAddress.Replace("-", ":").Split(':');
        if (macParts.Length != 6 || !macParts.All(p => p.Length == 2))
        {
            return BadRequest(new { message = "Format MAC invalide (ex: AA:BB:CC:DD:EE:FF)" });
        }

        // Valider le format IP
        try
        {
            DhcpPacket.StringToIp(reservation.IpAddress);
        }
        catch
        {
            return BadRequest(new { message = "Format IP invalide" });
        }

        if (_dhcpService.AddStaticReservation(reservation))
        {
            return Ok(new { message = "Réservation ajoutée", reservation });
        }

        return BadRequest(new { message = "Impossible d'ajouter la réservation (IP hors plage?)" });
    }

    /// <summary>
    /// Supprimer une réservation statique
    /// </summary>
    [HttpDelete("reservations/{macAddress}")]
    public IActionResult RemoveReservation(string macAddress)
    {
        var normalizedMac = macAddress.ToUpperInvariant().Replace("-", ":");
        
        if (_dhcpService.RemoveStaticReservation(normalizedMac))
        {
            return Ok(new { message = $"Réservation supprimée pour {normalizedMac}" });
        }
        
        return NotFound(new { message = "Réservation non trouvée" });
    }

    /// <summary>
    /// Obtenir les statistiques du pool DHCP
    /// </summary>
    [HttpGet("pool/stats")]
    public IActionResult GetPoolStats()
    {
        var config = _dhcpService.GetConfig();
        var leases = _dhcpService.GetLeases().ToList();
        
        var rangeStart = DhcpPacket.StringToIp(config.RangeStart);
        var rangeEnd = DhcpPacket.StringToIp(config.RangeEnd);
        var totalIps = (int)(rangeEnd - rangeStart + 1);
        var usedIps = leases.Count;
        var reservedIps = config.StaticReservations.Count;
        
        return Ok(new
        {
            rangeStart = config.RangeStart,
            rangeEnd = config.RangeEnd,
            totalIps,
            usedIps,
            reservedIps,
            availableIps = totalIps - usedIps,
            utilizationPercent = totalIps > 0 ? Math.Round((double)usedIps / totalIps * 100, 1) : 0
        });
    }
}
