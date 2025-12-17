using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace NetworkFirewall.Services;

public interface IPacketCaptureService
{
    event EventHandler<PacketCapturedEventArgs>? PacketCaptured;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    IEnumerable<NetworkInterfaceInfo> GetAvailableInterfaces();
    bool IsCapturing { get; }
    string? CurrentInterface { get; }
}

public record PacketCapturedEventArgs
{
    public required string SourceMac { get; init; }
    public required string DestinationMac { get; init; }
    public string? SourceIp { get; init; }
    public string? DestinationIp { get; init; }
    public int? SourcePort { get; init; }
    public int? DestinationPort { get; init; }
    public string Protocol { get; init; } = "Unknown";
    public int PacketSize { get; init; }
    public byte[]? RawData { get; init; }
    public DateTime Timestamp { get; init; }
}

public class NetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public bool IsUp { get; set; }
}

public class PacketCaptureService : IPacketCaptureService, IDisposable
{
    private readonly ILogger<PacketCaptureService> _logger;
    private readonly AppSettings _settings;
    private ILiveDevice? _device;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public event EventHandler<PacketCapturedEventArgs>? PacketCaptured;
    public bool IsCapturing => _device?.Started ?? false;
    public string? CurrentInterface => _device?.Description;

    public PacketCaptureService(ILogger<PacketCaptureService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public IEnumerable<NetworkInterfaceInfo> GetAvailableInterfaces()
    {
        var devices = CaptureDeviceList.Instance;
        return devices.Select(d => new NetworkInterfaceInfo
        {
            Name = d.Name,
            Description = d.Description,
            MacAddress = (d as LibPcapLiveDevice)?.MacAddress?.ToString(),
            IsUp = true
        }).ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsCapturing)
        {
            _logger.LogWarning("Packet capture already running");
            return;
        }

        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
        {
            _logger.LogError("No network interfaces found. Make sure libpcap/WinPcap is installed.");
            return;
        }

        // Sélectionner l'interface
        if (!string.IsNullOrEmpty(_settings.NetworkInterface))
        {
            _device = devices.FirstOrDefault(d => 
                d.Name.Contains(_settings.NetworkInterface, StringComparison.OrdinalIgnoreCase) ||
                d.Description.Contains(_settings.NetworkInterface, StringComparison.OrdinalIgnoreCase));
        }

        _device ??= devices.FirstOrDefault(d => d.Description.Contains("Ethernet") || d.Description.Contains("eth"));
        _device ??= devices.First();

        _logger.LogInformation("Starting packet capture on: {Interface}", _device.Description);

        try
        {
            _device.Open(DeviceModes.Promiscuous, 1000);
            _device.OnPacketArrival += OnPacketArrival;
            _device.StartCapture();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureTask = Task.Run(() => MonitorCapture(_cts.Token), _cts.Token);

            _logger.LogInformation("Packet capture started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start packet capture. Run with elevated privileges (sudo on Linux).");
            throw;
        }
    }

    private async Task MonitorCapture(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsCapturing)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var rawPacket = e.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            
            if (packet == null) return;

            var ethernetPacket = packet.Extract<EthernetPacket>();
            if (ethernetPacket == null) return;

            var args = new PacketCapturedEventArgs
            {
                SourceMac = ethernetPacket.SourceHardwareAddress.ToString(),
                DestinationMac = ethernetPacket.DestinationHardwareAddress.ToString(),
                PacketSize = rawPacket.Data.Length,
                RawData = rawPacket.Data,
                Timestamp = rawPacket.Timeval.Date
            };

            // Extraire les informations IP
            var ipPacket = packet.Extract<IPPacket>();
            if (ipPacket != null)
            {
                args = args with
                {
                    SourceIp = ipPacket.SourceAddress.ToString(),
                    DestinationIp = ipPacket.DestinationAddress.ToString(),
                    Protocol = ipPacket.Protocol.ToString()
                };

                // Extraire les informations TCP
                var tcpPacket = packet.Extract<TcpPacket>();
                if (tcpPacket != null)
                {
                    args = args with
                    {
                        SourcePort = tcpPacket.SourcePort,
                        DestinationPort = tcpPacket.DestinationPort,
                        Protocol = "TCP"
                    };
                }
                else
                {
                    // Extraire les informations UDP
                    var udpPacket = packet.Extract<UdpPacket>();
                    if (udpPacket != null)
                    {
                        args = args with
                        {
                            SourcePort = udpPacket.SourcePort,
                            DestinationPort = udpPacket.DestinationPort,
                            Protocol = "UDP"
                        };
                    }
                }
            }
            else
            {
                // Vérifier si c'est un paquet ARP
                var arpPacket = packet.Extract<ArpPacket>();
                if (arpPacket != null)
                {
                    args = args with
                    {
                        SourceIp = arpPacket.SenderProtocolAddress.ToString(),
                        DestinationIp = arpPacket.TargetProtocolAddress.ToString(),
                        Protocol = "ARP"
                    };
                }
            }

            PacketCaptured?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing packet");
        }
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping packet capture...");
        
        _cts?.Cancel();
        
        if (_device != null)
        {
            _device.OnPacketArrival -= OnPacketArrival;
            if (_device.Started)
            {
                _device.StopCapture();
            }
            _device.Close();
        }

        if (_captureTask != null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("Packet capture stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _device?.Dispose();
        _cts?.Dispose();
    }
}
