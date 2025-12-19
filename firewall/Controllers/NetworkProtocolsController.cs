using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NetworkProtocolsController : ControllerBase
{
    private readonly INetworkProtocolService _networkProtocolService;
    private readonly INatService _natService;
    private readonly ISshService _sshService;
    private readonly INtpService _ntpService;
    private readonly ISnmpService _snmpService;
    private readonly ILogger<NetworkProtocolsController> _logger;

    public NetworkProtocolsController(
        INetworkProtocolService networkProtocolService,
        INatService natService,
        ISshService sshService,
        INtpService ntpService,
        ISnmpService snmpService,
        ILogger<NetworkProtocolsController> logger)
    {
        _networkProtocolService = networkProtocolService;
        _natService = natService;
        _sshService = sshService;
        _ntpService = ntpService;
        _snmpService = snmpService;
        _logger = logger;
    }

    #region IP Configuration

    /// <summary>
    /// Obtenir la configuration IP de toutes les interfaces
    /// </summary>
    [HttpGet("ip/interfaces")]
    public async Task<ActionResult<IEnumerable<IpConfiguration>>> GetIpConfigurations()
    {
        var configs = await _networkProtocolService.GetIpConfigurationsAsync();
        return Ok(configs);
    }

    /// <summary>
    /// Obtenir la configuration IP d'une interface
    /// </summary>
    [HttpGet("ip/interfaces/{interfaceName}")]
    public async Task<ActionResult<IpConfiguration>> GetIpConfiguration(string interfaceName)
    {
        var config = await _networkProtocolService.GetIpConfigurationAsync(interfaceName);
        if (config == null)
            return NotFound(new { message = $"Interface {interfaceName} not found" });
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration IP d'une interface
    /// </summary>
    [HttpPut("ip/interfaces/{interfaceName}")]
    public async Task<ActionResult> UpdateIpConfiguration(string interfaceName, [FromBody] IpConfiguration config)
    {
        try
        {
            await _networkProtocolService.UpdateIpConfigurationAsync(interfaceName, config);
            return Ok(new { message = "IP configuration updated" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Obtenir les statistiques IP
    /// </summary>
    [HttpGet("ip/statistics")]
    public async Task<ActionResult<IpStatistics>> GetIpStatistics()
    {
        var stats = await _networkProtocolService.GetIpStatisticsAsync();
        return Ok(stats);
    }

    #endregion

    #region ICMP

    /// <summary>
    /// Ping un hôte
    /// </summary>
    [HttpPost("icmp/ping")]
    public async Task<ActionResult<PingResult>> Ping([FromBody] PingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { message = "Host is required" });

        var result = await _networkProtocolService.PingAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Ping multiple (statistiques)
    /// </summary>
    [HttpPost("icmp/ping/multiple")]
    public async Task<ActionResult<PingStatistics>> PingMultiple([FromBody] PingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { message = "Host is required" });

        var stats = await _networkProtocolService.PingMultipleAsync(request);
        return Ok(stats);
    }

    /// <summary>
    /// Traceroute vers un hôte
    /// </summary>
    [HttpPost("icmp/traceroute")]
    public async Task<ActionResult<TracerouteResult>> Traceroute([FromBody] TracerouteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { message = "Host is required" });

        var result = await _networkProtocolService.TracerouteAsync(request);
        return Ok(result);
    }

    #endregion

    #region Routing

    /// <summary>
    /// Obtenir la table de routage
    /// </summary>
    [HttpGet("routing/table")]
    public async Task<ActionResult<IEnumerable<RouteEntry>>> GetRoutingTable()
    {
        var routes = await _networkProtocolService.GetRoutingTableAsync();
        return Ok(routes);
    }

    /// <summary>
    /// Ajouter une route statique
    /// </summary>
    [HttpPost("routing/routes")]
    public async Task<ActionResult<RouteEntry>> AddRoute([FromBody] RouteEntryDto route)
    {
        try
        {
            var entry = await _networkProtocolService.AddRouteAsync(route);
            return CreatedAtAction(nameof(GetRoutingTable), entry);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Supprimer une route
    /// </summary>
    [HttpDelete("routing/routes/{id}")]
    public async Task<ActionResult> DeleteRoute(int id)
    {
        try
        {
            await _networkProtocolService.DeleteRouteAsync(id);
            return Ok(new { message = "Route deleted" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Route not found" });
        }
    }

    /// <summary>
    /// Obtenir la configuration RIP
    /// </summary>
    [HttpGet("routing/rip/config")]
    public async Task<ActionResult<RipConfig>> GetRipConfig()
    {
        var config = await _networkProtocolService.GetRipConfigAsync();
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration RIP
    /// </summary>
    [HttpPut("routing/rip/config")]
    public async Task<ActionResult> UpdateRipConfig([FromBody] RipConfig config)
    {
        await _networkProtocolService.UpdateRipConfigAsync(config);
        return Ok(new { message = "RIP configuration updated" });
    }

    /// <summary>
    /// Obtenir la configuration OSPF
    /// </summary>
    [HttpGet("routing/ospf/config")]
    public async Task<ActionResult<OspfConfig>> GetOspfConfig()
    {
        var config = await _networkProtocolService.GetOspfConfigAsync();
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration OSPF
    /// </summary>
    [HttpPut("routing/ospf/config")]
    public async Task<ActionResult> UpdateOspfConfig([FromBody] OspfConfig config)
    {
        await _networkProtocolService.UpdateOspfConfigAsync(config);
        return Ok(new { message = "OSPF configuration updated" });
    }

    /// <summary>
    /// Obtenir les voisins OSPF
    /// </summary>
    [HttpGet("routing/ospf/neighbors")]
    public async Task<ActionResult<IEnumerable<OspfNeighbor>>> GetOspfNeighbors()
    {
        var neighbors = await _networkProtocolService.GetOspfNeighborsAsync();
        return Ok(neighbors);
    }

    /// <summary>
    /// Obtenir la configuration BGP
    /// </summary>
    [HttpGet("routing/bgp/config")]
    public async Task<ActionResult<BgpConfig>> GetBgpConfig()
    {
        var config = await _networkProtocolService.GetBgpConfigAsync();
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration BGP
    /// </summary>
    [HttpPut("routing/bgp/config")]
    public async Task<ActionResult> UpdateBgpConfig([FromBody] BgpConfig config)
    {
        await _networkProtocolService.UpdateBgpConfigAsync(config);
        return Ok(new { message = "BGP configuration updated" });
    }

    /// <summary>
    /// Obtenir les voisins BGP
    /// </summary>
    [HttpGet("routing/bgp/neighbors")]
    public async Task<ActionResult<IEnumerable<BgpNeighbor>>> GetBgpNeighbors()
    {
        var neighbors = await _networkProtocolService.GetBgpNeighborsAsync();
        return Ok(neighbors);
    }

    #endregion

    #region NAT

    /// <summary>
    /// Obtenir la configuration NAT
    /// </summary>
    [HttpGet("nat/config")]
    public ActionResult<NatConfig> GetNatConfig()
    {
        var config = _natService.GetConfig();
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration NAT
    /// </summary>
    [HttpPut("nat/config")]
    public async Task<ActionResult> UpdateNatConfig([FromBody] NatConfig config)
    {
        await _natService.UpdateConfigAsync(config);
        return Ok(new { message = "NAT configuration updated" });
    }

    /// <summary>
    /// Obtenir les règles NAT
    /// </summary>
    [HttpGet("nat/rules")]
    public async Task<ActionResult<IEnumerable<NatRule>>> GetNatRules()
    {
        var rules = await _natService.GetRulesAsync();
        return Ok(rules);
    }

    /// <summary>
    /// Ajouter une règle NAT
    /// </summary>
    [HttpPost("nat/rules")]
    public async Task<ActionResult<NatRule>> AddNatRule([FromBody] NatRuleDto rule)
    {
        var entry = await _natService.AddRuleAsync(rule);
        return CreatedAtAction(nameof(GetNatRules), entry);
    }

    /// <summary>
    /// Mettre à jour une règle NAT
    /// </summary>
    [HttpPut("nat/rules/{id}")]
    public async Task<ActionResult> UpdateNatRule(int id, [FromBody] NatRuleDto rule)
    {
        try
        {
            await _natService.UpdateRuleAsync(id, rule);
            return Ok(new { message = "NAT rule updated" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "NAT rule not found" });
        }
    }

    /// <summary>
    /// Supprimer une règle NAT
    /// </summary>
    [HttpDelete("nat/rules/{id}")]
    public async Task<ActionResult> DeleteNatRule(int id)
    {
        try
        {
            await _natService.DeleteRuleAsync(id);
            return Ok(new { message = "NAT rule deleted" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "NAT rule not found" });
        }
    }

    /// <summary>
    /// Obtenir les connexions NAT actives
    /// </summary>
    [HttpGet("nat/connections")]
    public async Task<ActionResult<IEnumerable<NatConnection>>> GetNatConnections()
    {
        var connections = await _natService.GetConnectionsAsync();
        return Ok(connections);
    }

    /// <summary>
    /// Activer le masquerade
    /// </summary>
    [HttpPost("nat/masquerade/enable")]
    public async Task<ActionResult> EnableMasquerade([FromBody] MasqueradeRequest request)
    {
        await _natService.EnableMasqueradeAsync(request.WanInterface);
        return Ok(new { message = "Masquerade enabled" });
    }

    /// <summary>
    /// Désactiver le masquerade
    /// </summary>
    [HttpPost("nat/masquerade/disable")]
    public async Task<ActionResult> DisableMasquerade()
    {
        await _natService.DisableMasqueradeAsync();
        return Ok(new { message = "Masquerade disabled" });
    }

    #endregion

    #region SSH

    /// <summary>
    /// Obtenir la configuration SSH
    /// </summary>
    [HttpGet("ssh/config")]
    public ActionResult<SshConfig> GetSshConfig()
    {
        var config = _sshService.GetConfig();
        // Ne pas retourner les mots de passe
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration SSH
    /// </summary>
    [HttpPut("ssh/config")]
    public async Task<ActionResult> UpdateSshConfig([FromBody] SshConfig config)
    {
        await _sshService.UpdateConfigAsync(config);
        return Ok(new { message = "SSH configuration updated" });
    }

    /// <summary>
    /// Obtenir les sessions SSH actives
    /// </summary>
    [HttpGet("ssh/sessions")]
    public async Task<ActionResult<IEnumerable<SshSession>>> GetSshSessions()
    {
        var sessions = await _sshService.GetActiveSessionsAsync();
        return Ok(sessions);
    }

    /// <summary>
    /// Déconnecter une session SSH
    /// </summary>
    [HttpPost("ssh/sessions/{pid}/disconnect")]
    public async Task<ActionResult> DisconnectSshSession(int pid)
    {
        await _sshService.DisconnectSessionAsync(pid);
        return Ok(new { message = "Session disconnected" });
    }

    /// <summary>
    /// Obtenir les clés SSH autorisées
    /// </summary>
    [HttpGet("ssh/keys")]
    public async Task<ActionResult<IEnumerable<SshAuthorizedKey>>> GetSshKeys()
    {
        var keys = await _sshService.GetAuthorizedKeysAsync();
        return Ok(keys);
    }

    /// <summary>
    /// Ajouter une clé SSH
    /// </summary>
    [HttpPost("ssh/keys")]
    public async Task<ActionResult<SshAuthorizedKey>> AddSshKey([FromBody] AddSshKeyRequest request)
    {
        try
        {
            var key = await _sshService.AddAuthorizedKeyAsync(request.Name, request.PublicKey);
            return CreatedAtAction(nameof(GetSshKeys), key);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Supprimer une clé SSH
    /// </summary>
    [HttpDelete("ssh/keys/{id}")]
    public async Task<ActionResult> DeleteSshKey(int id)
    {
        try
        {
            await _sshService.DeleteAuthorizedKeyAsync(id);
            return Ok(new { message = "SSH key deleted" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "SSH key not found" });
        }
    }

    /// <summary>
    /// Redémarrer le service SSH
    /// </summary>
    [HttpPost("ssh/restart")]
    public async Task<ActionResult> RestartSsh()
    {
        await _sshService.RestartSshServiceAsync();
        return Ok(new { message = "SSH service restarted" });
    }

    #endregion

    #region NTP

    /// <summary>
    /// Obtenir la configuration NTP
    /// </summary>
    [HttpGet("ntp/config")]
    public ActionResult<NtpConfig> GetNtpConfig()
    {
        var config = _ntpService.GetConfig();
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration NTP
    /// </summary>
    [HttpPut("ntp/config")]
    public async Task<ActionResult> UpdateNtpConfig([FromBody] NtpConfig config)
    {
        await _ntpService.UpdateConfigAsync(config);
        return Ok(new { message = "NTP configuration updated" });
    }

    /// <summary>
    /// Obtenir le statut NTP
    /// </summary>
    [HttpGet("ntp/status")]
    public async Task<ActionResult<NtpStatus>> GetNtpStatus()
    {
        var status = await _ntpService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// Obtenir le statut des serveurs NTP
    /// </summary>
    [HttpGet("ntp/servers")]
    public async Task<ActionResult<IEnumerable<NtpServer>>> GetNtpServers()
    {
        var servers = await _ntpService.GetServersStatusAsync();
        return Ok(servers);
    }

    /// <summary>
    /// Forcer une synchronisation NTP
    /// </summary>
    [HttpPost("ntp/sync")]
    public async Task<ActionResult> SyncNtp()
    {
        await _ntpService.SyncNowAsync();
        return Ok(new { message = "NTP synchronization initiated" });
    }

    /// <summary>
    /// Définir le fuseau horaire
    /// </summary>
    [HttpPut("ntp/timezone")]
    public async Task<ActionResult> SetTimezone([FromBody] TimezoneRequest request)
    {
        await _ntpService.SetTimezoneAsync(request.Timezone);
        return Ok(new { message = "Timezone updated" });
    }

    /// <summary>
    /// Obtenir les fuseaux horaires disponibles
    /// </summary>
    [HttpGet("ntp/timezones")]
    public async Task<ActionResult<IEnumerable<string>>> GetTimezones()
    {
        var timezones = await _ntpService.GetAvailableTimezonesAsync();
        return Ok(timezones);
    }

    #endregion

    #region SNMP

    /// <summary>
    /// Obtenir la configuration SNMP
    /// </summary>
    [HttpGet("snmp/config")]
    public ActionResult<SnmpConfig> GetSnmpConfig()
    {
        var config = _snmpService.GetConfig();
        // Masquer les mots de passe
        foreach (var user in config.Users)
        {
            if (!string.IsNullOrEmpty(user.AuthPassword))
                user.AuthPassword = "********";
            if (!string.IsNullOrEmpty(user.PrivPassword))
                user.PrivPassword = "********";
        }
        return Ok(config);
    }

    /// <summary>
    /// Mettre à jour la configuration SNMP
    /// </summary>
    [HttpPut("snmp/config")]
    public async Task<ActionResult> UpdateSnmpConfig([FromBody] SnmpConfig config)
    {
        await _snmpService.UpdateConfigAsync(config);
        return Ok(new { message = "SNMP configuration updated" });
    }

    /// <summary>
    /// Obtenir les statistiques SNMP
    /// </summary>
    [HttpGet("snmp/statistics")]
    public async Task<ActionResult<SnmpStatistics>> GetSnmpStatistics()
    {
        var stats = await _snmpService.GetStatisticsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Obtenir les utilisateurs SNMP v3
    /// </summary>
    [HttpGet("snmp/users")]
    public async Task<ActionResult<IEnumerable<SnmpUser>>> GetSnmpUsers()
    {
        var users = await _snmpService.GetUsersAsync();
        // Masquer les mots de passe
        return Ok(users.Select(u => new
        {
            u.Username,
            u.SecurityLevel,
            u.AuthProtocol,
            u.PrivProtocol
        }));
    }

    /// <summary>
    /// Ajouter un utilisateur SNMP v3
    /// </summary>
    [HttpPost("snmp/users")]
    public async Task<ActionResult<SnmpUser>> AddSnmpUser([FromBody] SnmpUser user)
    {
        try
        {
            var result = await _snmpService.AddUserAsync(user);
            return CreatedAtAction(nameof(GetSnmpUsers), new { username = result.Username }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Supprimer un utilisateur SNMP
    /// </summary>
    [HttpDelete("snmp/users/{username}")]
    public async Task<ActionResult> DeleteSnmpUser(string username)
    {
        try
        {
            await _snmpService.DeleteUserAsync(username);
            return Ok(new { message = "SNMP user deleted" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "SNMP user not found" });
        }
    }

    /// <summary>
    /// Obtenir les récepteurs de traps
    /// </summary>
    [HttpGet("snmp/traps")]
    public async Task<ActionResult<IEnumerable<SnmpTrapReceiver>>> GetTrapReceivers()
    {
        var receivers = await _snmpService.GetTrapReceiversAsync();
        return Ok(receivers);
    }

    /// <summary>
    /// Ajouter un récepteur de trap
    /// </summary>
    [HttpPost("snmp/traps")]
    public async Task<ActionResult<SnmpTrapReceiver>> AddTrapReceiver([FromBody] SnmpTrapReceiver receiver)
    {
        var result = await _snmpService.AddTrapReceiverAsync(receiver);
        return CreatedAtAction(nameof(GetTrapReceivers), result);
    }

    /// <summary>
    /// Supprimer un récepteur de trap
    /// </summary>
    [HttpDelete("snmp/traps/{address}")]
    public async Task<ActionResult> DeleteTrapReceiver(string address)
    {
        try
        {
            await _snmpService.DeleteTrapReceiverAsync(address);
            return Ok(new { message = "Trap receiver deleted" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Trap receiver not found" });
        }
    }

    /// <summary>
    /// Envoyer un trap de test
    /// </summary>
    [HttpPost("snmp/traps/{address}/test")]
    public async Task<ActionResult> SendTestTrap(string address)
    {
        await _snmpService.SendTestTrapAsync(address);
        return Ok(new { message = "Test trap sent" });
    }

    /// <summary>
    /// Redémarrer le service SNMP
    /// </summary>
    [HttpPost("snmp/restart")]
    public async Task<ActionResult> RestartSnmp()
    {
        await _snmpService.RestartSnmpServiceAsync();
        return Ok(new { message = "SNMP service restarted" });
    }

    #endregion

    #region Summary

    /// <summary>
    /// Obtenir un résumé de tous les protocoles
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult> GetProtocolsSummary()
    {
        var ipStats = await _networkProtocolService.GetIpStatisticsAsync();
        var ntpStatus = await _ntpService.GetStatusAsync();
        var natConfig = _natService.GetConfig();
        var sshConfig = _sshService.GetConfig();
        var snmpConfig = _snmpService.GetConfig();

        return Ok(new
        {
            IP = new
            {
                PacketsReceived = ipStats.PacketsReceived,
                PacketsSent = ipStats.PacketsSent
            },
            NAT = new
            {
                natConfig.Enabled,
                natConfig.MasqueradeEnabled,
                RulesCount = natConfig.Rules.Count
            },
            SSH = new
            {
                sshConfig.Enabled,
                sshConfig.Port,
                sshConfig.PasswordAuthentication,
                sshConfig.PubkeyAuthentication
            },
            NTP = new
            {
                ntpStatus.Synchronized,
                ntpStatus.CurrentServer,
                ntpStatus.Offset,
                ntpStatus.Timezone
            },
            SNMP = new
            {
                snmpConfig.Enabled,
                Version = snmpConfig.Version.ToString(),
                UsersCount = snmpConfig.Users.Count,
                TrapReceiversCount = snmpConfig.TrapReceivers.Count
            }
        });
    }

    #endregion
}

#region Request DTOs

public class MasqueradeRequest
{
    public string WanInterface { get; set; } = string.Empty;
}

public class AddSshKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}

public class TimezoneRequest
{
    public string Timezone { get; set; } = string.Empty;
}

#endregion
