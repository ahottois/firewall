namespace NetworkFirewall.Models;

/// <summary>
/// Représente un log de trafic réseau
/// </summary>
public class TrafficLog
{
    public long Id { get; set; }
    public string SourceMac { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public int? SourcePort { get; set; }
    public string DestinationMac { get; set; } = string.Empty;
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public int PacketSize { get; set; }
    public DateTime Timestamp { get; set; }
    public TrafficDirection Direction { get; set; }
    public bool IsSuspicious { get; set; }
    
    public int? DeviceId { get; set; }
    public NetworkDevice? Device { get; set; }
}

public enum TrafficDirection
{
    Inbound,
    Outbound,
    Internal
}
