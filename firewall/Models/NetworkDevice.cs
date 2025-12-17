namespace NetworkFirewall.Models;

/// <summary>
/// Représente un appareil réseau détecté
/// </summary>
public class NetworkDevice
{
    public int Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public bool IsKnown { get; set; }
    public bool IsTrusted { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string? Description { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    
    public ICollection<NetworkAlert> Alerts { get; set; } = new List<NetworkAlert>();
    public ICollection<TrafficLog> TrafficLogs { get; set; } = new List<TrafficLog>();
}

public enum DeviceStatus
{
    Unknown,
    Online,
    Offline,
    Blocked
}
