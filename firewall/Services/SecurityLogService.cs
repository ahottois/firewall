using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// Interface pour le service de logging de sécurité
/// </summary>
public interface ISecurityLogService
{
    /// <summary>
    /// Enregistre un événement de blocage de trafic
    /// </summary>
    Task LogBlockedTrafficAsync(string sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, string? deviceName = null);

    /// <summary>
    /// Enregistre une tentative de connexion bloquée
    /// </summary>
    Task LogBlockedConnectionAttemptAsync(string sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, int packetCount = 1, string? deviceName = null);

    /// <summary>
    /// Enregistre le blocage d'un appareil
    /// </summary>
    Task LogDeviceBlockedAsync(string macAddress, string? ipAddress, string? deviceName, string? reason);

    /// <summary>
    /// Enregistre le déblocage d'un appareil
    /// </summary>
    Task LogDeviceUnblockedAsync(string macAddress, string? ipAddress, string? deviceName);

    /// <summary>
    /// Enregistre l'ajout d'une règle firewall
    /// </summary>
    Task LogFirewallRuleAddedAsync(string ruleName, string? macAddress, string? ipAddress);

    /// <summary>
    /// Enregistre la suppression d'une règle firewall
    /// </summary>
    Task LogFirewallRuleRemovedAsync(string ruleName, string? macAddress, string? ipAddress);

    /// <summary>
    /// Enregistre une détection de scan de ports
    /// </summary>
    Task LogPortScanDetectedAsync(string sourceIp, string? sourceMac, int portCount, string? deviceName = null);

    /// <summary>
    /// Enregistre un trafic suspect
    /// </summary>
    Task LogSuspiciousTrafficAsync(string message, string? sourceIp, string? sourceMac, LogSeverity severity = LogSeverity.Warning);

    /// <summary>
    /// Enregistre un événement système
    /// </summary>
    Task LogSystemEventAsync(string message, LogSeverity severity = LogSeverity.Info);

    /// <summary>
    /// Enregistre un log personnalisé
    /// </summary>
    Task LogAsync(SecurityLog log);
}

/// <summary>
/// Service de logging de sécurité avec notifications temps réel
/// </summary>
public class SecurityLogService : ISecurityLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertHubNotifier _alertHubNotifier;
    private readonly ILogger<SecurityLogService> _logger;

    public SecurityLogService(
        IServiceScopeFactory scopeFactory,
        IAlertHubNotifier alertHubNotifier,
        ILogger<SecurityLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _alertHubNotifier = alertHubNotifier;
        _logger = logger;
    }

    public async Task LogBlockedTrafficAsync(string sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, string? deviceName = null)
    {
        var log = new SecurityLog
        {
            Severity = LogSeverity.Warning,
            Category = LogCategory.TrafficBlocked,
            ActionTaken = "Paquet rejeté",
            Message = $"Trafic bloqué de {deviceName ?? sourceMac} vers {destIp}:{destPort}",
            SourceMac = sourceMac,
            SourceIp = sourceIp,
            DestinationIp = destIp,
            DestinationPort = destPort,
            Protocol = protocol,
            DeviceName = deviceName
        };

        await LogAsync(log);
    }

    public async Task LogBlockedConnectionAttemptAsync(string sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, int packetCount = 1, string? deviceName = null)
    {
        var severity = packetCount > 10 ? LogSeverity.Critical : LogSeverity.Warning;
        
        var log = new SecurityLog
        {
            Severity = severity,
            Category = LogCategory.ConnectionAttemptBlocked,
            ActionTaken = "Tentative de connexion bloquée",
            Message = $"L'appareil {deviceName ?? sourceMac} a tenté de joindre {destIp} sur le port {destPort} ({packetCount} paquets)",
            SourceMac = sourceMac,
            SourceIp = sourceIp,
            DestinationIp = destIp,
            DestinationPort = destPort,
            Protocol = protocol,
            DeviceName = deviceName,
            PacketCount = packetCount
        };

        await LogAsync(log);

        // Envoyer aussi un événement de blocage
        await _alertHubNotifier.NotifyBlockEventAsync(new BlockEventDto
        {
            Timestamp = log.Timestamp,
            SourceMac = sourceMac,
            SourceIp = sourceIp,
            DestinationIp = destIp,
            DestinationPort = destPort,
            Protocol = protocol,
            ActionTaken = log.ActionTaken,
            DeviceName = deviceName,
            PacketCount = packetCount
        });
    }

    public async Task LogDeviceBlockedAsync(string macAddress, string? ipAddress, string? deviceName, string? reason)
    {
        var log = new SecurityLog
        {
            Severity = LogSeverity.Warning,
            Category = LogCategory.DeviceBlocked,
            ActionTaken = "Appareil bloqué",
            Message = $"L'appareil {deviceName ?? macAddress} ({ipAddress ?? "IP inconnue"}) a été bloqué. Raison: {reason ?? "Non spécifiée"}",
            SourceMac = macAddress,
            SourceIp = ipAddress,
            DeviceName = deviceName
        };

        await LogAsync(log);
    }

    public async Task LogDeviceUnblockedAsync(string macAddress, string? ipAddress, string? deviceName)
    {
        var log = new SecurityLog
        {
            Severity = LogSeverity.Info,
            Category = LogCategory.DeviceUnblocked,
            ActionTaken = "Appareil débloqué",
            Message = $"L'appareil {deviceName ?? macAddress} ({ipAddress ?? "IP inconnue"}) a été débloqué",
            SourceMac = macAddress,
            SourceIp = ipAddress,
            DeviceName = deviceName
        };

        await LogAsync(log);
    }

    public async Task LogFirewallRuleAddedAsync(string ruleName, string? macAddress, string? ipAddress)
    {
        var log = new SecurityLog
        {
            Severity = LogSeverity.Info,
            Category = LogCategory.FirewallRuleAdded,
            ActionTaken = "Règle firewall ajoutée",
            Message = $"Règle '{ruleName}' ajoutée pour bloquer {macAddress ?? ipAddress}",
            SourceMac = macAddress,
            SourceIp = ipAddress
        };

        await LogAsync(log);
    }

    public async Task LogFirewallRuleRemovedAsync(string ruleName, string? macAddress, string? ipAddress)
    {
        var log = new SecurityLog
        {
            Severity = LogSeverity.Info,
            Category = LogCategory.FirewallRuleRemoved,
            ActionTaken = "Règle firewall supprimée",
            Message = $"Règle '{ruleName}' supprimée pour {macAddress ?? ipAddress}",
            SourceMac = macAddress,
            SourceIp = ipAddress
        };

        await LogAsync(log);
    }

    public async Task LogPortScanDetectedAsync(string sourceIp, string? sourceMac, int portCount, string? deviceName = null)
    {
        var severity = portCount > 100 ? LogSeverity.Critical : LogSeverity.Warning;
        
        var log = new SecurityLog
        {
            Severity = severity,
            Category = LogCategory.PortScanDetected,
            ActionTaken = "Scan de ports détecté",
            Message = $"Scan de ports détecté depuis {deviceName ?? sourceIp}: {portCount} ports scannés",
            SourceIp = sourceIp,
            SourceMac = sourceMac,
            DeviceName = deviceName,
            PacketCount = portCount
        };

        await LogAsync(log);
    }

    public async Task LogSuspiciousTrafficAsync(string message, string? sourceIp, string? sourceMac, LogSeverity severity = LogSeverity.Warning)
    {
        var log = new SecurityLog
        {
            Severity = severity,
            Category = LogCategory.SuspiciousTraffic,
            ActionTaken = "Trafic suspect détecté",
            Message = message,
            SourceIp = sourceIp,
            SourceMac = sourceMac
        };

        await LogAsync(log);
    }

    public async Task LogSystemEventAsync(string message, LogSeverity severity = LogSeverity.Info)
    {
        var log = new SecurityLog
        {
            Severity = severity,
            Category = LogCategory.SystemEvent,
            ActionTaken = "Événement système",
            Message = message
        };

        await LogAsync(log);
    }

    public async Task LogAsync(SecurityLog log)
    {
        try
        {
            log.Timestamp = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISecurityLogRepository>();
            
            // Rechercher l'appareil associé si on a une MAC
            if (!string.IsNullOrEmpty(log.SourceMac))
            {
                var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
                var device = await deviceRepo.GetByMacAddressAsync(log.SourceMac);
                if (device != null)
                {
                    log.DeviceId = device.Id;
                    log.DeviceName ??= device.Description ?? device.Hostname;
                }
            }

            var savedLog = await repository.AddAsync(log);

            // Envoyer la notification SignalR
            await _alertHubNotifier.NotifyNewSecurityLogAsync(SecurityLogDto.FromEntity(savedLog));

            _logger.LogDebug("Log sécurité enregistré: [{Severity}] {Category} - {Message}", 
                log.Severity, log.Category, log.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'enregistrement du log de sécurité");
        }
    }
}
