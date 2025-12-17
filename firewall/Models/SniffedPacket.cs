namespace NetworkFirewall.Models;

public class SniffedPacket
{
    public string SourceMac { get; set; } = string.Empty;
    public string DestinationMac { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? SourcePort { get; set; }
    public int? DestinationPort { get; set; }
    public string Protocol { get; set; } = "Unknown";
    public int PacketSize { get; set; }
    public DateTime Timestamp { get; set; }
    public TrafficDirection Direction { get; set; }
    public byte[]? RawData { get; set; }
}
