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
    Task<int> ScanNetworkAsync();
    Task<string?> ResolveHostnameAsync(string ipAddress);
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
    private readonly IOuiLookupService _ouiLookup;
    private readonly AppSettings _settings;
    
    // Suivre les MAC recemment vues pour eviter de traiter trop souvent
    private readonly ConcurrentDictionary<string, DateTime> _recentlySeenMacs = new();
    
    // Suivre les appareils qui ont déjà déclenché une alerte "inconnue"
    private readonly ConcurrentDictionary<string, DateTime> _unknownDeviceAlertsSent = new();
    
    // Suivre les appareils qui ont déjà déclenché une alerte "nouvel appareil"  
    private readonly ConcurrentDictionary<string, bool> _newDeviceAlertsSent = new();
    
    // Cache DNS pour éviter les requêtes répétées
    private readonly ConcurrentDictionary<string, (string? Hostname, DateTime CachedAt)> _dnsCache = new();
    
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    
    // Timer pour le nettoyage périodique
    private DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref uint PhyAddrLen);

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<DeviceDiscoveredEventArgs>? UnknownDeviceDetected;

    public DeviceDiscoveryService(
        ILogger<DeviceDiscoveryService> logger,
        IServiceScopeFactory scopeFactory,
        IScanSessionService scanSessionService,
        IOuiLookupService ouiLookup,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _scanSessionService = scanSessionService;
        _ouiLookup = ouiLookup;
        _settings = settings.Value;
    }

    public async Task<string?> ResolveHostnameAsync(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return null;

        // Vérifier le cache (5 minutes)
        if (_dnsCache.TryGetValue(ipAddress, out var cached) &&
            (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 5)
        {
            return cached.Hostname;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var hostEntry = await Dns.GetHostEntryAsync(ipAddress, cts.Token);
            var hostname = hostEntry.HostName;
            
            // Ne pas retourner l'IP comme hostname
            if (hostname != ipAddress)
            {
                _dnsCache[ipAddress] = (hostname, DateTime.UtcNow);
                return hostname;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("DNS timeout pour {Ip}", ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "DNS inverse échoué pour {Ip}", ipAddress);
        }

        _dnsCache[ipAddress] = (null, DateTime.UtcNow);
        return null;
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
        
        // Nettoyage périodique (évite de le faire à chaque paquet)
        if (now - _lastCleanup > CleanupInterval)
        {
            _lastCleanup = now;
            _ = Task.Run(() => CleanupOldEntries(now));
        }

        if (!await _processingLock.WaitAsync(100))
        {
            return; // Skip si le lock est occupé trop longtemps
        }
        
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
                    _ = CreateDeviceModifiedAlertAsync(existingDevice, "IP Address", existingDevice.IpAddress, packet.SourceIp);
                }
            }

            // Résoudre le hostname si on a une IP (fire-and-forget pour ne pas bloquer)
            var hostname = !string.IsNullOrEmpty(packet.SourceIp) 
                ? await ResolveHostnameAsync(packet.SourceIp) 
                : null;

            var device = new NetworkDevice
            {
                MacAddress = mac,
                IpAddress = packet.SourceIp,
                Hostname = hostname,
                Vendor = _ouiLookup.GetVendor(mac),
                Status = DeviceStatus.Online
            };

            device = await deviceRepo.AddOrUpdateAsync(device);

            var eventArgs = new DeviceDiscoveredEventArgs
            {
                Device = device,
                IsNew = isNew
            };

            DeviceDiscovered?.Invoke(this, eventArgs);

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
            // Alerter uniquement une fois par nouvel appareil
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
        foreach (var key in _recentlySeenMacs.Keys.ToArray())
        {
            if (_recentlySeenMacs.TryGetValue(key, out var value) && (now - value).TotalMinutes > 5)
            {
                _recentlySeenMacs.TryRemove(key, out _);
            }
        }

        // Nettoyer les alertes d'appareils inconnus de plus de 24 heures
        foreach (var key in _unknownDeviceAlertsSent.Keys.ToArray())
        {
            if (_unknownDeviceAlertsSent.TryGetValue(key, out var value) && (now - value).TotalHours > 24)
            {
                _unknownDeviceAlertsSent.TryRemove(key, out _);
            }
        }
        
        // Nettoyer le cache DNS (entrées > 10 minutes)
        foreach (var key in _dnsCache.Keys.ToArray())
        {
            if (_dnsCache.TryGetValue(key, out var value) && (now - value.CachedAt).TotalMinutes > 10)
            {
                _dnsCache.TryRemove(key, out _);
            }
        }
    }

    public async Task<IEnumerable<NetworkDevice>> GetOnlineDevicesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        return await deviceRepo.GetOnlineDevicesAsync();
    }

    public async Task<int> ScanNetworkAsync()
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
                
                var ips = Enumerable.Range(1, 254).Select(i => $"{subnet}.{i}");

                await Parallel.ForEachAsync(ips, new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (ip, ct) =>
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
                });
            }
            
            var summary = $"Scan completed. Scanned {scannedCount} hosts. Found {foundCount} active devices.";
            _logger.LogInformation(summary);
            await _scanSessionService.CompleteSessionAsync(session.Id, summary);
            
            return foundCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'analyse du reseau");
            await _scanSessionService.FailSessionAsync(session.Id, ex.Message);
            return 0;
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

            // Résoudre le hostname
            var hostname = await ResolveHostnameAsync(ip);

            var device = new NetworkDevice
            {
                MacAddress = mac,
                IpAddress = ip,
                Hostname = hostname,
                Vendor = _ouiLookup.GetVendor(mac),
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

        // Fallback: Check ARP table via command line
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
            await process.WaitForExitAsync();

            if (output.Contains(ipAddress))
            {
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
}
