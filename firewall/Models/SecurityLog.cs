namespace NetworkFirewall.Models;

/// <summary>
/// Représente un log de sécurité pour le monitoring en temps réel
/// </summary>
public class SecurityLog
{
    public int Id { get; set; }
    
    /// <summary>
    /// Horodatage de l'événement
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Niveau de sévérité (Info, Warning, Critical)
    /// </summary>
    public LogSeverity Severity { get; set; }
    
    /// <summary>
    /// Catégorie du log
    /// </summary>
    public LogCategory Category { get; set; }
    
    /// <summary>
    /// Action effectuée (ex: "Paquet rejeté", "Tentative d'accès bloquée")
    /// </summary>
    public string ActionTaken { get; set; } = string.Empty;
    
    /// <summary>
    /// Message descriptif de l'événement
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Adresse IP source de l'appareil concerné
    /// </summary>
    public string? SourceIp { get; set; }
    
    /// <summary>
    /// Adresse MAC source de l'appareil concerné
    /// </summary>
    public string? SourceMac { get; set; }
    
    /// <summary>
    /// Adresse IP de destination
    /// </summary>
    public string? DestinationIp { get; set; }
    
    /// <summary>
    /// Port de destination
    /// </summary>
    public int? DestinationPort { get; set; }
    
    /// <summary>
    /// Protocole utilisé (TCP, UDP, ICMP, etc.)
    /// </summary>
    public string? Protocol { get; set; }
    
    /// <summary>
    /// Nom de l'appareil (si connu)
    /// </summary>
    public string? DeviceName { get; set; }
    
    /// <summary>
    /// Nombre de paquets bloqués (pour les logs groupés)
    /// </summary>
    public int PacketCount { get; set; } = 1;
    
    /// <summary>
    /// Indique si le log a été lu
    /// </summary>
    public bool IsRead { get; set; }
    
    /// <summary>
    /// Indique si l'événement a été résolu/archivé
    /// </summary>
    public bool IsArchived { get; set; }
    
    /// <summary>
    /// Données additionnelles en JSON
    /// </summary>
    public string? AdditionalData { get; set; }
    
    /// <summary>
    /// ID de l'appareil associé (optionnel)
    /// </summary>
    public int? DeviceId { get; set; }
    
    /// <summary>
    /// Référence à l'appareil
    /// </summary>
    public NetworkDevice? Device { get; set; }
}

/// <summary>
/// Niveau de sévérité des logs
/// </summary>
public enum LogSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>
/// Catégorie des logs de sécurité
/// </summary>
public enum LogCategory
{
    /// <summary>
    /// Blocage de trafic
    /// </summary>
    TrafficBlocked,
    
    /// <summary>
    /// Tentative de connexion bloquée
    /// </summary>
    ConnectionAttemptBlocked,
    
    /// <summary>
    /// Appareil bloqué
    /// </summary>
    DeviceBlocked,
    
    /// <summary>
    /// Appareil débloqué
    /// </summary>
    DeviceUnblocked,
    
    /// <summary>
    /// Règle de firewall ajoutée
    /// </summary>
    FirewallRuleAdded,
    
    /// <summary>
    /// Règle de firewall supprimée
    /// </summary>
    FirewallRuleRemoved,
    
    /// <summary>
    /// Scan de ports détecté
    /// </summary>
    PortScanDetected,
    
    /// <summary>
    /// Trafic suspect
    /// </summary>
    SuspiciousTraffic,
    
    /// <summary>
    /// Changement de configuration
    /// </summary>
    ConfigurationChange,
    
    /// <summary>
    /// Événement système
    /// </summary>
    SystemEvent,
    
    /// <summary>
    /// Violation de politique
    /// </summary>
    PolicyViolation
}

/// <summary>
/// DTO pour les logs de sécurité (pour SignalR)
/// </summary>
public class SecurityLogDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ActionTaken { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? SourceMac { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string? Protocol { get; set; }
    public string? DeviceName { get; set; }
    public int PacketCount { get; set; }
    public bool IsRead { get; set; }

    public static SecurityLogDto FromEntity(SecurityLog log) => new()
    {
        Id = log.Id,
        Timestamp = log.Timestamp,
        Severity = log.Severity.ToString(),
        Category = log.Category.ToString(),
        ActionTaken = log.ActionTaken,
        Message = log.Message,
        SourceIp = log.SourceIp,
        SourceMac = log.SourceMac,
        DestinationIp = log.DestinationIp,
        DestinationPort = log.DestinationPort,
        Protocol = log.Protocol,
        DeviceName = log.DeviceName ?? log.Device?.Hostname ?? log.Device?.Description,
        PacketCount = log.PacketCount,
        IsRead = log.IsRead
    };
}
