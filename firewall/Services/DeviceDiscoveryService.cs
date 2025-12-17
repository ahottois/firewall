using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IDeviceDiscoveryService
{
    event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    event EventHandler<DeviceDiscoveredEventArgs>? UnknownDeviceDetected;
    Task ProcessPacketAsync(PacketCapturedEventArgs packet);
    Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync();
    Task ScanNetworkAsync();
}

public class DeviceDiscoveredEventArgs : EventArgs
{
    public required NetworkDevice Device { get; init; }
    public bool IsNew { get; init; }
}

public class DeviceDiscoveryService : IDeviceDiscoveryService
{
    private readonly ILogger<DeviceDiscoveryService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<string, DateTime> _recentlySeenMacs = new();
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<DeviceDiscoveredEventArgs>? UnknownDeviceDetected;

    public DeviceDiscoveryService(
        ILogger<DeviceDiscoveryService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
    }

    public async Task ProcessPacketAsync(PacketCapturedEventArgs packet)
    {
        // Éviter de traiter le même MAC trop souvent
        var now = DateTime.UtcNow;
        if (_recentlySeenMacs.TryGetValue(packet.SourceMac, out var lastSeen) &&
            (now - lastSeen).TotalSeconds < 30)
        {
            return;
        }

        _recentlySeenMacs[packet.SourceMac] = now;

        // Nettoyer les anciennes entrées
        var oldEntries = _recentlySeenMacs
            .Where(kvp => (now - kvp.Value).TotalMinutes > 5)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in oldEntries)
        {
            _recentlySeenMacs.TryRemove(key, out _);
        }

        await _processingLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            var existingDevice = await deviceRepo.GetByMacAddressAsync(packet.SourceMac);
            var isNew = existingDevice == null;

            var device = new NetworkDevice
            {
                MacAddress = packet.SourceMac,
                IpAddress = packet.SourceIp,
                Vendor = GetVendorFromMac(packet.SourceMac),
                Status = DeviceStatus.Online
            };

            device = await deviceRepo.AddOrUpdateAsync(device);

            var eventArgs = new DeviceDiscoveredEventArgs
            {
                Device = device,
                IsNew = isNew
            };

            DeviceDiscovered?.Invoke(this, eventArgs);

            if (isNew || !device.IsKnown)
            {
                _logger.LogWarning("Unknown device detected: {Mac} ({Ip})", device.MacAddress, device.IpAddress);
                UnknownDeviceDetected?.Invoke(this, eventArgs);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        return await deviceRepo.GetOnlineDevicesAsync();
    }

    public async Task ScanNetworkAsync()
    {
        _logger.LogInformation("Starting network scan...");
        
        try
        {
            // Obtenir les adresses IP locales pour déterminer le sous-réseau
            var localAddresses = GetLocalIPAddresses();
            
            foreach (var localAddress in localAddresses)
            {
                var subnet = GetSubnet(localAddress);
                _logger.LogInformation("Scanning subnet: {Subnet}", subnet);
                
                var tasks = new List<Task>();
                for (int i = 1; i <= 254; i++)
                {
                    var ip = $"{subnet}.{i}";
                    tasks.Add(PingHostAsync(ip));
                }
                
                await Task.WhenAll(tasks);
            }
            
            _logger.LogInformation("Network scan completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during network scan");
        }
    }

    private async Task PingHostAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            
            if (reply.Status == IPStatus.Success)
            {
                _logger.LogDebug("Host {Ip} is alive", ip);
            }
        }
        catch
        {
            // Ignorer les erreurs de ping
        }
    }

    private static IEnumerable<string> GetLocalIPAddresses()
    {
        var addresses = new List<string>();
        
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    addresses.Add(ua.Address.ToString());
                }
            }
        }
        
        return addresses;
    }

    private static string GetSubnet(string ip)
    {
        var parts = ip.Split('.');
        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }

    private static string? GetVendorFromMac(string mac)
    {
        // OUI lookup simplifié - dans une version production, utiliser une base de données OUI complète
        var oui = mac.Replace(":", "").Replace("-", "").ToUpper()[..6];
        
        var vendors = new Dictionary<string, string>
        {
            { "000C29", "VMware" },
            { "005056", "VMware" },
            { "001C42", "Parallels" },
            { "080027", "VirtualBox" },
            { "DC21E2", "Apple" },
            { "A4C3F0", "Intel" },
            { "B827EB", "Raspberry Pi" },
            { "E45F01", "Raspberry Pi" },
            { "2CCF67", "Apple" },
            { "F0D5BF", "Google" },
            { "94E6F7", "Intel" },
            { "7483C2", "Intel" },
        };

        return vendors.TryGetValue(oui, out var vendor) ? vendor : null;
    }
}
