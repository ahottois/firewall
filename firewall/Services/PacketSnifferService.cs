using System.Collections.Concurrent;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public class SnifferFilter
{
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? Port { get; set; }
    public string? Protocol { get; set; }
    public TrafficDirection? Direction { get; set; }
}

public interface IPacketSnifferService
{
    void StartSniffing(SnifferFilter? filter = null);
    void StopSniffing();
    IEnumerable<SniffedPacket> GetPackets(int limit = 100);
    void ClearPackets();
    bool IsSniffing { get; }
    SnifferFilter? CurrentFilter { get; }
}

public class PacketSnifferService : IPacketSnifferService, IDisposable
{
    private readonly IPacketCaptureService _packetCapture;
    private readonly ConcurrentQueue<SniffedPacket> _packetBuffer = new();
    private const int MaxBufferSize = 5000;
    private bool _isSniffing;
    private SnifferFilter? _currentFilter;
    private readonly object _lock = new();

    public bool IsSniffing => _isSniffing;
    public SnifferFilter? CurrentFilter => _currentFilter;

    public PacketSnifferService(IPacketCaptureService packetCapture)
    {
        _packetCapture = packetCapture;
        _packetCapture.PacketCaptured += OnPacketCaptured;
    }

    public void StartSniffing(SnifferFilter? filter = null)
    {
        lock (_lock)
        {
            _currentFilter = filter;
            _isSniffing = true;
        }
    }

    public void StopSniffing()
    {
        lock (_lock)
        {
            _isSniffing = false;
        }
    }

    public void ClearPackets()
    {
        _packetBuffer.Clear();
    }

    public IEnumerable<SniffedPacket> GetPackets(int limit = 100)
    {
        return _packetBuffer.Reverse().Take(limit).ToList();
    }

    private void OnPacketCaptured(object? sender, PacketCapturedEventArgs e)
    {
        if (!_isSniffing) return;

        var direction = DetermineDirection(e);
        
        // Create SniffedPacket first to check filter against direction if needed
        var sniffedPacket = new SniffedPacket
        {
            SourceMac = e.SourceMac,
            DestinationMac = e.DestinationMac,
            SourceIp = e.SourceIp,
            DestinationIp = e.DestinationIp,
            SourcePort = e.SourcePort,
            DestinationPort = e.DestinationPort,
            Protocol = e.Protocol,
            PacketSize = e.PacketSize,
            Timestamp = e.Timestamp,
            RawData = e.RawData,
            Direction = direction
        };

        if (!MatchesFilter(sniffedPacket)) return;

        _packetBuffer.Enqueue(sniffedPacket);

        // Maintain buffer size
        while (_packetBuffer.Count > MaxBufferSize)
        {
            _packetBuffer.TryDequeue(out _);
        }
    }

    private bool MatchesFilter(SniffedPacket packet)
    {
        if (_currentFilter == null) return true;

        if (!string.IsNullOrEmpty(_currentFilter.SourceIp) && 
            !string.Equals(packet.SourceIp, _currentFilter.SourceIp, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(_currentFilter.DestinationIp) && 
            !string.Equals(packet.DestinationIp, _currentFilter.DestinationIp, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_currentFilter.Port.HasValue)
        {
            if (packet.SourcePort != _currentFilter.Port && packet.DestinationPort != _currentFilter.Port)
                return false;
        }

        if (!string.IsNullOrEmpty(_currentFilter.Protocol) && 
            !string.Equals(packet.Protocol, _currentFilter.Protocol, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_currentFilter.Direction.HasValue && packet.Direction != _currentFilter.Direction.Value)
            return false;

        return true;
    }

    private TrafficDirection DetermineDirection(PacketCapturedEventArgs packet)
    {
        var isSourceLocal = IsLocalIp(packet.SourceIp);
        var isDestLocal = IsLocalIp(packet.DestinationIp);

        if (isSourceLocal && isDestLocal)
            return TrafficDirection.Internal;
        
        if (isSourceLocal && !isDestLocal)
            return TrafficDirection.Outbound;
            
        if (!isSourceLocal && isDestLocal)
            return TrafficDirection.Inbound;

        // If neither is local (e.g. promiscuous mode seeing other traffic), treat as external/unknown or inbound?
        // Let's assume Inbound if we are the destination, but here we don't know "us".
        // Default to Inbound for external->external (unlikely in switched network unless ARP spoofing)
        return TrafficDirection.Inbound;
    }

    private bool IsLocalIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        return ip.StartsWith("192.168.") ||
               ip.StartsWith("10.") ||
               ip.StartsWith("172.16.") ||
               ip.StartsWith("172.17.") ||
               ip.StartsWith("172.18.") ||
               ip.StartsWith("172.19.") ||
               ip.StartsWith("172.2") ||
               ip.StartsWith("172.30.") ||
               ip.StartsWith("172.31.") ||
               ip == "127.0.0.1";
    }

    public void Dispose()
    {
        _packetCapture.PacketCaptured -= OnPacketCaptured;
    }
}
