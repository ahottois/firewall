using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IDhcpService
{
    DhcpConfig GetConfig();
    void UpdateConfig(DhcpConfig config);
    IEnumerable<DhcpLease> GetLeases();
}

public class DhcpService : BackgroundService, IDhcpService
{
    private readonly ILogger<DhcpService> _logger;
    private DhcpConfig _config = new();
    private readonly List<DhcpLease> _leases = new();
    private UdpClient? _udpClient;
    private const int DhcpServerPort = 67;
    private const int DhcpClientPort = 68;
    private const string ConfigPath = "dhcp_config.json";
    private const string LeasesPath = "dhcp_leases.json";

    public DhcpService(ILogger<DhcpService> logger)
    {
        _logger = logger;
        LoadData();
    }

    public DhcpConfig GetConfig() => _config;

    public void UpdateConfig(DhcpConfig config)
    {
        _config = config;
        SaveData();
        
        // Restart service logic if needed, or just let the loop pick up changes
        if (!_config.Enabled && _udpClient != null)
        {
            _udpClient.Close();
            _udpClient = null;
        }
    }

    public IEnumerable<DhcpLease> GetLeases() => _leases;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.Enabled)
            {
                try
                {
                    if (_udpClient == null)
                    {
                        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, DhcpServerPort));
                        _udpClient.EnableBroadcast = true;
                        _logger.LogInformation("DHCP Server started on port 67");
                    }

                    var result = await _udpClient.ReceiveAsync(stoppingToken);
                    ProcessPacket(result.Buffer, result.RemoteEndPoint);
                }
                catch (SocketException ex)
                {
                    _logger.LogError(ex, "DHCP Socket Error (Port 67 might be in use)");
                    await Task.Delay(5000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DHCP Error");
                }
            }
            else
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private void ProcessPacket(byte[] data, IPEndPoint remoteEndPoint)
    {
        // Basic DHCP Packet Parsing (Simplified)
        // DHCP Message Type is Option 53
        // We need to handle DISCOVER (1) and REQUEST (3)
        
        // This is a placeholder for a full DHCP implementation.
        // Implementing a full DHCP parser/builder from scratch is complex.
        // For this demo, we will log the request.
        
        _logger.LogInformation("Received DHCP Packet from {Remote}", remoteEndPoint);
        
        // In a real implementation:
        // 1. Parse packet to get Message Type and Client MAC
        // 2. If DISCOVER: Find free IP, Send OFFER
        // 3. If REQUEST: Verify IP, Send ACK, Update Lease
    }

    private void LoadData()
    {
        try
        {
            if (File.Exists(ConfigPath))
                _config = JsonSerializer.Deserialize<DhcpConfig>(File.ReadAllText(ConfigPath)) ?? new DhcpConfig();
            
            if (File.Exists(LeasesPath))
                _leases.AddRange(JsonSerializer.Deserialize<List<DhcpLease>>(File.ReadAllText(LeasesPath)) ?? new List<DhcpLease>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading DHCP data");
        }
    }

    private void SaveData()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config));
            File.WriteAllText(LeasesPath, JsonSerializer.Serialize(_leases));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving DHCP data");
        }
    }
}
