namespace NetworkFirewall.Models;

/// <summary>
/// Représente une alerte de sécurité réseau
/// </summary>
public class NetworkAlert
{
    public int Id { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceMac { get; set; }
    public string? SourceIp { get; set; }
    public string? DestinationMac { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string? Protocol { get; set; }
    public string? RawPacketData { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
    public bool IsResolved { get; set; }
    
    public int? DeviceId { get; set; }
    public NetworkDevice? Device { get; set; }
}

public enum AlertType
{
    NewDevice,
    UnknownDevice,
    SuspiciousTraffic,
    PortScan,
    ArpSpoofing,
    DnsAnomaly,
    HighTrafficVolume,
    MalformedPacket,
    UnauthorizedAccess,
    ManInTheMiddle
}

public enum AlertSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}
