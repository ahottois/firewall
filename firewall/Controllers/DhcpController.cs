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
    /// Obtenir la configuration DHCP complète
    /// </summary>
    [HttpGet("config")]
    public ActionResult<DhcpConfig> GetConfig()
    {
        return Ok(_dhcpService.GetConfig());
    }

    /// <summary>
    /// Obtenir la configuration DHCP avec aide selon le niveau
    /// </summary>
    [HttpGet("config/{level}")]
    public ActionResult<DhcpConfigResponse> GetConfigByLevel(string level)
    {
        if (!Enum.TryParse<DhcpConfigLevel>(level, true, out var configLevel))
        {
            return BadRequest(new { message = "Niveau invalide. Utilisez: easy, intermediate, expert" });
        }

        var config = _dhcpService.GetConfig();
        var help = DhcpHelpProvider.GetHelp(configLevel);

        return Ok(new DhcpConfigResponse
        {
            Config = config,
            Level = configLevel,
            Help = help
        });
    }

    /// <summary>
    /// Obtenir uniquement l'aide pour un niveau donné
    /// </summary>
    [HttpGet("help/{level}")]
    public ActionResult<List<DhcpSettingHelp>> GetHelp(string level)
    {
        if (!Enum.TryParse<DhcpConfigLevel>(level, true, out var configLevel))
        {
            return BadRequest(new { message = "Niveau invalide. Utilisez: easy, intermediate, expert" });
        }

        return Ok(DhcpHelpProvider.GetHelp(configLevel));
    }

    /// <summary>
    /// Mettre à jour la configuration DHCP (niveau facile)
    /// </summary>
    [HttpPost("config/easy")]
    public IActionResult UpdateConfigEasy([FromBody] DhcpConfigEasy easyConfig)
    {
        var config = _dhcpService.GetConfig();
        
        // Appliquer uniquement les paramètres faciles
        config.Enabled = easyConfig.Enabled;
        config.RangeStart = easyConfig.RangeStart;
        config.RangeEnd = easyConfig.RangeEnd;
        config.Gateway = easyConfig.Gateway;
        config.Dns1 = easyConfig.Dns1;
        config.Dns2 = easyConfig.Dns2;
        
        // Valider
        var validationError = ValidateConfig(config);
        if (validationError != null)
            return BadRequest(new { message = validationError });

        _dhcpService.UpdateConfig(config);
        _logger.LogInformation("DHCP: Configuration (facile) mise à jour");
        
        return Ok(new { message = "Configuration mise à jour", level = "easy" });
    }

    /// <summary>
    /// Mettre à jour la configuration DHCP (niveau intermédiaire)
    /// </summary>
    [HttpPost("config/intermediate")]
    public IActionResult UpdateConfigIntermediate([FromBody] DhcpConfigIntermediate intermediateConfig)
    {
        var config = _dhcpService.GetConfig();
        
        // Paramètres faciles
        config.Enabled = intermediateConfig.Enabled;
        config.RangeStart = intermediateConfig.RangeStart;
        config.RangeEnd = intermediateConfig.RangeEnd;
        config.Gateway = intermediateConfig.Gateway;
        config.Dns1 = intermediateConfig.Dns1;
        config.Dns2 = intermediateConfig.Dns2;
        
        // Paramètres intermédiaires
        config.SubnetMask = intermediateConfig.SubnetMask;
        config.LeaseTimeMinutes = intermediateConfig.LeaseTimeMinutes;
        config.DomainName = intermediateConfig.DomainName;
        config.NetworkInterface = intermediateConfig.NetworkInterface;
        config.NtpServer1 = intermediateConfig.NtpServer1;
        config.AuthoritativeMode = intermediateConfig.AuthoritativeMode;
        config.StaticReservations = intermediateConfig.StaticReservations ?? new();
        
        // Valider
        var validationError = ValidateConfig(config);
        if (validationError != null)
            return BadRequest(new { message = validationError });

        _dhcpService.UpdateConfig(config);
        _logger.LogInformation("DHCP: Configuration (intermédiaire) mise à jour");
        
        return Ok(new { message = "Configuration mise à jour", level = "intermediate" });
    }

    /// <summary>
    /// Mettre à jour la configuration DHCP complète (niveau expert)
    /// </summary>
    [HttpPost("config/expert")]
    [HttpPost("config")]
    public IActionResult UpdateConfig([FromBody] DhcpConfig config)
    {
        var validationError = ValidateConfig(config);
        if (validationError != null)
            return BadRequest(new { message = validationError });

        _dhcpService.UpdateConfig(config);
        _logger.LogInformation("DHCP: Configuration (expert) mise à jour");
        
        return Ok(new { message = "Configuration mise à jour", level = "expert" });
    }

    /// <summary>
    /// Valider une configuration DHCP
    /// </summary>
    private string? ValidateConfig(DhcpConfig config)
    {
        if (string.IsNullOrEmpty(config.RangeStart) || string.IsNullOrEmpty(config.RangeEnd))
            return "La plage d'adresses est requise";

        try
        {
            var start = DhcpPacket.StringToIp(config.RangeStart);
            var end = DhcpPacket.StringToIp(config.RangeEnd);
            
            if (start >= end)
                return "L'adresse de début doit être inférieure à l'adresse de fin";
            
            DhcpPacket.StringToIp(config.SubnetMask);
            
            if (!string.IsNullOrEmpty(config.Gateway))
                DhcpPacket.StringToIp(config.Gateway);
            
            if (!string.IsNullOrEmpty(config.Dns1))
                DhcpPacket.StringToIp(config.Dns1);
            if (!string.IsNullOrEmpty(config.Dns2))
                DhcpPacket.StringToIp(config.Dns2);
                
            // Validations expert
            if (config.LeaseTimeMinutes < 1)
                return "La durée du bail doit être d'au moins 1 minute";
            
            if (config.MaxLeaseTimeMinutes > 0 && config.LeaseTimeMinutes > config.MaxLeaseTimeMinutes)
                return "La durée du bail ne peut pas dépasser la durée maximale";
                
            if (config.MinLeaseTimeMinutes > 0 && config.LeaseTimeMinutes < config.MinLeaseTimeMinutes)
                return "La durée du bail ne peut pas être inférieure à la durée minimale";
        }
        catch (Exception)
        {
            return "Format d'adresse IP invalide";
        }

        return null;
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
            return Ok(new { message = $"Bail libéré pour {normalizedMac}" });
        
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
            return BadRequest(new { message = "L'adresse MAC est requise" });

        if (string.IsNullOrEmpty(reservation.IpAddress))
            return BadRequest(new { message = "L'adresse IP est requise" });

        var macParts = reservation.MacAddress.Replace("-", ":").Split(':');
        if (macParts.Length != 6 || !macParts.All(p => p.Length == 2))
            return BadRequest(new { message = "Format MAC invalide (ex: AA:BB:CC:DD:EE:FF)" });

        try
        {
            DhcpPacket.StringToIp(reservation.IpAddress);
        }
        catch
        {
            return BadRequest(new { message = "Format IP invalide" });
        }

        if (_dhcpService.AddStaticReservation(reservation))
            return Ok(new { message = "Réservation ajoutée", reservation });

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
            return Ok(new { message = $"Réservation supprimée pour {normalizedMac}" });
        
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
