using NetworkFirewall.Models;

namespace NetworkFirewall.Services.Firewall;

/// <summary>
/// Interface pour le moteur de firewall - gestion des règles de blocage réseau
/// </summary>
public interface IFirewallEngine
{
    /// <summary>
    /// Indique si le moteur de firewall est supporté sur l'OS actuel
    /// </summary>
    bool IsSupported { get; }
    
    /// <summary>
    /// Nom du moteur de firewall (iptables, Windows Firewall, etc.)
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// Bloque un appareil réseau par MAC et/ou IP
    /// </summary>
    Task<FirewallResult> BlockDeviceAsync(string macAddress, string? ipAddress = null);

    /// <summary>
    /// Débloque un appareil réseau
    /// </summary>
    Task<FirewallResult> UnblockDeviceAsync(string macAddress, string? ipAddress = null);

    /// <summary>
    /// Vérifie si un appareil est actuellement bloqué au niveau firewall
    /// </summary>
    Task<bool> IsDeviceBlockedAsync(string macAddress);

    /// <summary>
    /// Récupère la liste de toutes les règles de blocage actives
    /// </summary>
    Task<IEnumerable<FirewallRule>> GetActiveRulesAsync();

    /// <summary>
    /// Applique toutes les règles de blocage depuis la base de données
    /// (utilisé au démarrage pour restaurer l'état)
    /// </summary>
    Task<int> RestoreRulesFromDatabaseAsync(IEnumerable<NetworkDevice> blockedDevices);

    /// <summary>
    /// Supprime toutes les règles de blocage WebGuard
    /// </summary>
    Task<FirewallResult> ClearAllRulesAsync();

    /// <summary>
    /// Vérifie si le service a les permissions nécessaires pour gérer le firewall
    /// </summary>
    Task<bool> CheckPermissionsAsync();
}

/// <summary>
/// Résultat d'une opération firewall
/// </summary>
public class FirewallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public FirewallErrorCode ErrorCode { get; set; } = FirewallErrorCode.None;

    public static FirewallResult Ok(string message = "Opération réussie") 
        => new() { Success = true, Message = message };

    public static FirewallResult Fail(string message, FirewallErrorCode code = FirewallErrorCode.Unknown, string? details = null) 
        => new() { Success = false, Message = message, ErrorCode = code, ErrorDetails = details };
}

/// <summary>
/// Codes d'erreur pour les opérations firewall
/// </summary>
public enum FirewallErrorCode
{
    None,
    Unknown,
    PermissionDenied,
    DeviceNotFound,
    AlreadyBlocked,
    NotBlocked,
    InvalidMacAddress,
    InvalidIpAddress,
    CommandFailed,
    UnsupportedPlatform,
    SelfBlockPrevented
}

/// <summary>
/// Représente une règle de firewall active
/// </summary>
public class FirewallRule
{
    public string RuleName { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public FirewallRuleDirection Direction { get; set; }
    public FirewallRuleAction Action { get; set; }
}

public enum FirewallRuleDirection
{
    Inbound,
    Outbound,
    Both
}

public enum FirewallRuleAction
{
    Block,
    Allow
}
