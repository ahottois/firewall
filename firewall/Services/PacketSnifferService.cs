using System.Collections.Concurrent;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public class SnifferFilter
{
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? Port { get; set; }
    public string? Protocol { get; set; }
}

public interface IPacketSnifferService
{
    void StartSniffing(SnifferFilter? filter = null);
    void StopSniffing();
    IEnumerable<PacketCapturedEventArgs> GetPackets(int limit = 100);
    void ClearPackets();
    bool IsSniffing { get; }
    SnifferFilter? CurrentFilter { get; }
}

public class PacketSnifferService : IPacketSnifferService, IDisposable
{
    private readonly IPacketCaptureService _packetCapture;
    private readonly ConcurrentQueue<PacketCapturedEventArgs> _packetBuffer = new();
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

    public IEnumerable<PacketCapturedEventArgs> GetPackets(int limit = 100)
    {
        return _packetBuffer.Reverse().Take(limit).ToList();
    }

    private void OnPacketCaptured(object? sender, PacketCapturedEventArgs e)
    {
        if (!_isSniffing) return;

        if (!MatchesFilter(e)) return;

        _packetBuffer.Enqueue(e);

        // Maintain buffer size
        while (_packetBuffer.Count > MaxBufferSize)
        {
            _packetBuffer.TryDequeue(out _);
        }
    }

    private bool MatchesFilter(PacketCapturedEventArgs packet)
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

        return true;
    }

    public void Dispose()
    {
        _packetCapture.PacketCaptured -= OnPacketCaptured;
    }
}
