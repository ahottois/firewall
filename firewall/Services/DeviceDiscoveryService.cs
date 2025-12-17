using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
    private readonly IScanSessionService _scanSessionService;
    private readonly AppSettings _settings;
    
    // Suivre les MAC recemment vues pour eviter de traiter trop souvent
    private readonly ConcurrentDictionary<string, DateTime> _recentlySeenMacs = new();
    
    // Suivre les appareils qui ont déjà déclenché une alerte "inconnue"
    // Clé : adresse MAC, Valeur : Quand l'alerte a été envoyée
    private readonly ConcurrentDictionary<string, DateTime> _unknownDeviceAlertsSent = new();
    
    // Suivre les appareils qui ont déjà déclenché une alerte "nouvel appareil"  
    private readonly ConcurrentDictionary<string, bool> _newDeviceAlertsSent = new();
    
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref uint PhyAddrLen);

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<DeviceDiscoveredEventArgs>? UnknownDeviceDetected;

    public DeviceDiscoveryService(
        ILogger<DeviceDiscoveryService> logger,
        IServiceScopeFactory scopeFactory,
        IScanSessionService scanSessionService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _scanSessionService = scanSessionService;
        _settings = settings.Value;
    }

    public async Task ProcessPacketAsync(PacketCapturedEventArgs packet)
    {
        if (string.IsNullOrEmpty(packet.SourceMac) || 
            packet.SourceMac == "00:00:00:00:00:00" ||
            packet.SourceMac == "FF:FF:FF:FF:FF:FF")
        {
            return;
        }

        var now = DateTime.UtcNow;
        var mac = packet.SourceMac.ToUpperInvariant();

        // Éviter de traiter le même MAC trop souvent (fenêtre de 30 secondes)
        if (_recentlySeenMacs.TryGetValue(mac, out var lastSeen) &&
            (now - lastSeen).TotalSeconds < 30)
        {
            return;
        }

        _recentlySeenMacs[mac] = now;

        // Nettoyer les anciennes entrées de temps en temps
        CleanupOldEntries(now);

        await _processingLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            var existingDevice = await deviceRepo.GetByMacAddressAsync(mac);
            var isNew = existingDevice == null;

            // Check for changes if device exists
            if (!isNew && existingDevice != null)
            {
                if (existingDevice.IpAddress != packet.SourceIp && !string.IsNullOrEmpty(packet.SourceIp))
                {
                    // IP Changed
                    await CreateDeviceModifiedAlertAsync(existingDevice, "IP Address", existingDevice.IpAddress, packet.SourceIp);
                }
            }

            var device = new NetworkDevice
            {
                MacAddress = mac,
                IpAddress = packet.SourceIp,
                Vendor = GetVendorFromMac(mac),
                Status = DeviceStatus.Online
            };

            device = await deviceRepo.AddOrUpdateAsync(device);

            var eventArgs = new DeviceDiscoveredEventArgs
            {
                Device = device,
                IsNew = isNew
            };

            // Toujours déclencher DeviceDiscovered pour le suivi
            DeviceDiscovered?.Invoke(this, eventArgs);

            // Déclencher UnknownDeviceDetected uniquement si :
            // 1. L'appareil est reellement nouveau (jamais vu auparavant) - alerte unique
            // 2. OU l'appareil n'est pas connu et nous n'avons pas alerte recemment (temps de recharge de 1 heure)
            if (await ShouldAlertForDeviceAsync(device, isNew, now))
            {
                _logger.LogWarning("Alerte appareil inconnu/nouveau : {Mac} ({Ip}) - EstNouveau : {IsNew}", 
                    device.MacAddress, device.IpAddress, isNew);
                UnknownDeviceDetected?.Invoke(this, eventArgs);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task<bool> ShouldAlertForDeviceAsync(NetworkDevice device, bool isNew, DateTime now)
    {
        var mac = device.MacAddress.ToUpperInvariant();

        // Si l'appareil est marque comme connu/fidele, ne jamais alerter
        if (device.IsKnown || device.IsTrusted)
        {
            return false;
        }

        // Check database for existing active alerts to prevent duplicates on restart
        using var scope = _scopeFactory.CreateScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertType = isNew ? AlertType.NewDevice : AlertType.UnknownDevice;
        
        if (await alertRepo.HasActiveAlertAsync(mac, alertType))
        {
            // Restore in-memory state if needed
            if (isNew) _newDeviceAlertsSent.TryAdd(mac, true);
            else _unknownDeviceAlertsSent.TryAdd(mac, now);
            
            return false;
        }

        // Si c'est un nouvel appareil (jamais vu avant)
        if (isNew)
        {
            // Alerter uniquement une fois par nouvel appareil - jamais
            if (_newDeviceAlertsSent.TryAdd(mac, true))
            {
                _unknownDeviceAlertsSent[mac] = now;
                return true;
            }
            return false;
        }

        // Pour les appareils existants mais inconnus, appliquer un temps de recharge
        if (_unknownDeviceAlertsSent.TryGetValue(mac, out var lastAlert))
        {
            // Temps de recharge de 1 heure pour les alertes d'appareils inconnus
            if ((now - lastAlert).TotalHours < 1)
            {
                return false;
            }
        }

        // Envoyer une alerte et mettre a jour l'horodatage
        _unknownDeviceAlertsSent[mac] = now;
        return true;
    }

    private void CleanupOldEntries(DateTime now)
    {
        // Nettoyer les MAC récemment vues de plus de 5 minutes
        var oldMacs = _recentlySeenMacs
            .Where(kvp => (now - kvp.Value).TotalMinutes > 5)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in oldMacs)
        {
            _recentlySeenMacs.TryRemove(key, out _);
        }

        // Nettoyer les alertes d'appareils inconnus de plus de 24 heures
        var oldAlerts = _unknownDeviceAlertsSent
            .Where(kvp => (now - kvp.Value).TotalHours > 24)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in oldAlerts)
        {
            _unknownDeviceAlertsSent.TryRemove(key, out _);
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
        _logger.LogInformation("Debut de l'analyse du reseau...");
        var session = await _scanSessionService.StartSessionAsync(ScanType.Network);
        
        try
        {
            // Obtenir les adresses IP locales pour determiner le sous-reseau
            var localAddresses = GetLocalIPAddresses();
            var totalHosts = localAddresses.Count() * 254;
            await _scanSessionService.StartSessionAsync(ScanType.Network, totalHosts); // Update total items
            
            int scannedCount = 0;
            int foundCount = 0;

            foreach (var localAddress in localAddresses)
            {
                var subnet = GetSubnet(localAddress);
                _logger.LogInformation("Analyse du sous-reseau : {Subnet}", subnet);
                
                var tasks = new List<Task>();
                for (int i = 1; i <= 254; i++)
                {
                    var ip = $"{subnet}.{i}";
                    tasks.Add(Task.Run(async () => 
                    {
                        try
                        {
                            if (await PingHostAsync(ip))
                            {
                                Interlocked.Increment(ref foundCount);
                                var mac = await GetMacAddressAsync(ip);
                                if (!string.IsNullOrEmpty(mac))
                                {
                                    await RegisterDiscoveredDeviceAsync(ip, mac);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogTrace(ex, "Error scanning host {Ip}", ip);
                        }
                        
                        Interlocked.Increment(ref scannedCount);
                        if (scannedCount % 10 == 0) await _scanSessionService.UpdateProgressAsync(session.Id, scannedCount);
                    }));
                }
                
                await Task.WhenAll(tasks);
            }
            
            var summary = $"Scan completed. Scanned {scannedCount} hosts. Found {foundCount} active devices.";
            _logger.LogInformation(summary);
            await _scanSessionService.CompleteSessionAsync(session.Id, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'analyse du reseau");
            await _scanSessionService.FailSessionAsync(session.Id, ex.Message);
        }
    }

    private async Task RegisterDiscoveredDeviceAsync(string ip, string mac)
    {
        await _processingLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            var existingDevice = await deviceRepo.GetByMacAddressAsync(mac);
            var isNew = existingDevice == null;

            var device = new NetworkDevice
            {
                MacAddress = mac,
                IpAddress = ip,
                Vendor = GetVendorFromMac(mac),
                Status = DeviceStatus.Online
            };

            device = await deviceRepo.AddOrUpdateAsync(device);

            var eventArgs = new DeviceDiscoveredEventArgs
            {
                Device = device,
                IsNew = isNew
            };

            DeviceDiscovered?.Invoke(this, eventArgs);

            if (await ShouldAlertForDeviceAsync(device, isNew, DateTime.UtcNow))
            {
                UnknownDeviceDetected?.Invoke(this, eventArgs);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task<bool> PingHostAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            
            if (reply.Status == IPStatus.Success)
            {
                _logger.LogDebug("L'hte {Ip} est actif", ip);
                return true;
            }
        }
        catch
        {
            // Ignorer les erreurs de ping
        }
        return false;
    }

    private async Task<string?> GetMacAddressAsync(string ipAddress)
    {
        // Try Windows SendARP
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                IPAddress dst = IPAddress.Parse(ipAddress);
                byte[] macAddr = new byte[6];
                uint macAddrLen = (uint)macAddr.Length;

                if (SendARP(BitConverter.ToInt32(dst.GetAddressBytes(), 0), 0, macAddr, ref macAddrLen) == 0)
                {
                    return string.Join(":", macAddr.Select(b => b.ToString("X2")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to resolve MAC for {Ip} using SendARP", ipAddress);
            }
        }

        // Try Linux /proc/net/arp
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                if (File.Exists("/proc/net/arp"))
                {
                    var lines = await File.ReadAllLinesAsync("/proc/net/arp");
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 && parts[0] == ipAddress)
                        {
                            var mac = parts[3];
                            if (mac != "00:00:00:00:00:00")
                            {
                                return mac.ToUpperInvariant();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to resolve MAC for {Ip} using /proc/net/arp", ipAddress);
            }
        }

        // Fallback: Check ARP table via command line (cross-platform fallback)
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arp" : "ip",
                    Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "-a" : "neigh show",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            // Parse output for IP and extract MAC
            // This is a rough implementation, regex would be better
            if (output.Contains(ipAddress))
            {
                // Simple regex for MAC address
                var match = System.Text.RegularExpressions.Regex.Match(output, $@"{ipAddress}.*?([0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}})");
                if (match.Success)
                {
                    return match.Groups[1].Value.Replace("-", ":").ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private async Task CreateDeviceModifiedAlertAsync(NetworkDevice device, string property, string? oldValue, string? newValue)
    {
        using var scope = _scopeFactory.CreateScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var alert = new NetworkAlert
        {
            Type = AlertType.DeviceModified,
            Severity = AlertSeverity.Medium,
            Title = "Device Modified",
            Message = $"Device {device.MacAddress} changed {property}: {oldValue ?? "N/A"} -> {newValue ?? "N/A"}",
            SourceMac = device.MacAddress,
            SourceIp = newValue,
            DeviceId = device.Id
        };

        await alertRepo.AddAsync(alert);
        await notificationService.SendAlertAsync(alert);
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
        // Recherche OUI simplifiée - utiliser une base de données OUI complète en production
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
            { "D83ADD", "Raspberry Pi" },
            { "DCA632", "Raspberry Pi" },
            { "001E06", "WIBRAIN" },
            { "F4F5D8", "Google" },
            { "3C5AB4", "Google" },
            { "F8:FF:C2", "Apple" },
        };

        return vendors.TryGetValue(oui, out var vendor) ? vendor : null;
    }
}
