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
    
    private readonly ConcurrentDictionary<string, DateTime> _recentlySeenMacs = new();
    private readonly ConcurrentDictionary<string, DateTime> _unknownDeviceAlertsSent = new();
    private readonly ConcurrentDictionary<string, bool> _newDeviceAlertsSent = new();
    private readonly ConcurrentDictionary<string, (string? Hostname, DateTime CachedAt)> _dnsCache = new();
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

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
            
            if (hostname != ipAddress)
            {
                _dnsCache[ipAddress] = (hostname, DateTime.UtcNow);
                return hostname;
            }
        }
        catch { }

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

        if (_recentlySeenMacs.TryGetValue(mac, out var lastSeen) &&
            (now - lastSeen).TotalSeconds < 30)
        {
            return;
        }

        _recentlySeenMacs[mac] = now;
        
        if (now - _lastCleanup > CleanupInterval)
        {
            _lastCleanup = now;
            _ = Task.Run(() => CleanupOldEntries(now));
        }

        if (!await _processingLock.WaitAsync(100))
        {
            return;
        }
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            var existingDevice = await deviceRepo.GetByMacAddressAsync(mac);
            var isNew = existingDevice == null;

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

            var eventArgs = new DeviceDiscoveredEventArgs { Device = device, IsNew = isNew };
            DeviceDiscovered?.Invoke(this, eventArgs);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private void CleanupOldEntries(DateTime now)
    {
        foreach (var key in _recentlySeenMacs.Keys.ToArray())
        {
            if (_recentlySeenMacs.TryGetValue(key, out var value) && (now - value).TotalMinutes > 5)
                _recentlySeenMacs.TryRemove(key, out _);
        }

        foreach (var key in _unknownDeviceAlertsSent.Keys.ToArray())
        {
            if (_unknownDeviceAlertsSent.TryGetValue(key, out var value) && (now - value).TotalHours > 24)
                _unknownDeviceAlertsSent.TryRemove(key, out _);
        }
        
        foreach (var key in _dnsCache.Keys.ToArray())
        {
            if (_dnsCache.TryGetValue(key, out var value) && (now - value.CachedAt).TotalMinutes > 10)
                _dnsCache.TryRemove(key, out _);
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
        _logger.LogInformation("========================================");
        _logger.LogInformation("SCAN: Debut du scan reseau");
        _logger.LogInformation("========================================");
        
        var session = await _scanSessionService.StartSessionAsync(ScanType.Network);
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            
            var localAddresses = GetLocalIPAddresses().ToList();
            _logger.LogInformation("SCAN: {Count} interface(s) reseau detectee(s)", localAddresses.Count);
            
            foreach (var addr in localAddresses)
            {
                _logger.LogInformation("  - Interface: {Addr}", addr);
            }
            
            if (!localAddresses.Any())
            {
                _logger.LogWarning("SCAN: Aucune interface reseau trouvee!");
                await _scanSessionService.FailSessionAsync(session.Id, "Aucune interface reseau");
                return 0;
            }
            
            var devices = new ConcurrentBag<NetworkDevice>();
            
            foreach (var localAddress in localAddresses)
            {
                var subnet = GetSubnet(localAddress);
                _logger.LogInformation("SCAN: Scan du sous-reseau {Subnet}.0/24", subnet);

                // METHODE 1: Lire directement la table ARP existante
                _logger.LogInformation("SCAN: Lecture de la table ARP existante...");
                var arpDevices = await ReadArpTableAsync();
                _logger.LogInformation("SCAN: {Count} entrees dans la table ARP", arpDevices.Count);
                
                foreach (var (ip, mac) in arpDevices)
                {
                    if (ip.StartsWith(subnet + "."))
                    {
                        var hostname = await ResolveHostnameAsync(ip);
                        var vendor = _ouiLookup.GetVendor(mac);
                        
                        devices.Add(new NetworkDevice
                        {
                            MacAddress = mac.ToUpperInvariant(),
                            IpAddress = ip,
                            Hostname = hostname,
                            Vendor = vendor,
                            Status = DeviceStatus.Online
                        });
                        _logger.LogInformation("  ARP: {Ip} -> {Mac} ({Vendor})", ip, mac, vendor ?? "Inconnu");
                    }
                }

                // METHODE 2: Ping pour découvrir de nouveaux hôtes (peut échouer sans root)
                _logger.LogInformation("SCAN: Tentative de ping sur {Subnet}.0/24...", subnet);
                var ips = Enumerable.Range(1, 254).Select(i => $"{subnet}.{i}").ToList();
                var respondingIps = new ConcurrentBag<string>();
                
                try
                {
                    await Parallel.ForEachAsync(ips, new ParallelOptions { MaxDegreeOfParallelism = 50 }, async (ip, ct) =>
                    {
                        try
                        {
                            using var ping = new Ping();
                            var reply = await ping.SendPingAsync(ip, 500);
                            if (reply.Status == IPStatus.Success)
                            {
                                respondingIps.Add(ip);
                            }
                        }
                        catch { }
                    });
                    
                    _logger.LogInformation("SCAN: {Count} hote(s) repondent au ping", respondingIps.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("SCAN: Ping echoue (permissions?): {Error}", ex.Message);
                }

                // Attendre et relire la table ARP après les pings
                if (respondingIps.Any())
                {
                    await Task.Delay(1000);
                    
                    var newArpDevices = await ReadArpTableAsync();
                    foreach (var (ip, mac) in newArpDevices)
                    {
                        if (ip.StartsWith(subnet + ".") && !devices.Any(d => d.MacAddress == mac.ToUpperInvariant()))
                        {
                            var hostname = await ResolveHostnameAsync(ip);
                            var vendor = _ouiLookup.GetVendor(mac);
                            
                            devices.Add(new NetworkDevice
                            {
                                MacAddress = mac.ToUpperInvariant(),
                                IpAddress = ip,
                                Hostname = hostname,
                                Vendor = vendor,
                                Status = DeviceStatus.Online
                            });
                            _logger.LogInformation("  NEW: {Ip} -> {Mac} ({Vendor})", ip, mac, vendor ?? "Inconnu");
                        }
                    }
                }
            }

            _logger.LogInformation("SCAN: Total {Count} appareils trouves", devices.Count);
            _logger.LogInformation("SCAN: Sauvegarde en base...");

            int savedCount = 0;
            foreach (var device in devices)
            {
                try
                {
                    await deviceRepo.AddOrUpdateAsync(device);
                    savedCount++;
                    _logger.LogInformation("  SAVED: {Mac} ({Ip})", device.MacAddress, device.IpAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "  ERREUR sauvegarde {Mac}", device.MacAddress);
                }
            }

            var summary = $"Scan termine: {savedCount} appareils";
            _logger.LogInformation("========================================");
            _logger.LogInformation("SCAN TERMINE: {Summary}", summary);
            _logger.LogInformation("========================================");
            
            await _scanSessionService.CompleteSessionAsync(session.Id, summary);
            
            return savedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SCAN ERREUR FATALE");
            await _scanSessionService.FailSessionAsync(session.Id, ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Lit la table ARP du système (fonctionne sans permissions root)
    /// </summary>
    private async Task<List<(string Ip, string Mac)>> ReadArpTableAsync()
    {
        var results = new List<(string Ip, string Mac)>();
        
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Méthode 1: /proc/net/arp (la plus fiable sur Linux)
                if (File.Exists("/proc/net/arp"))
                {
                    _logger.LogDebug("Lecture de /proc/net/arp...");
                    var lines = await File.ReadAllLinesAsync("/proc/net/arp");
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            var ip = parts[0];
                            var mac = parts[3].ToUpperInvariant();
                            
                            if (mac != "00:00:00:00:00:00" && !mac.Contains("INCOMPLETE"))
                            {
                                results.Add((ip, mac));
                                _logger.LogDebug("  /proc/net/arp: {Ip} -> {Mac}", ip, mac);
                            }
                        }
                    }
                }
                
                // Méthode 2: commande ip neigh
                if (results.Count == 0)
                {
                    _logger.LogDebug("Tentative avec 'ip neigh'...");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ip",
                        Arguments = "neigh show",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        
                        _logger.LogDebug("ip neigh output: {Output}", output);

                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            // Format: 192.168.1.1 dev eth0 lladdr aa:bb:cc:dd:ee:ff REACHABLE
                            var match = System.Text.RegularExpressions.Regex.Match(
                                line, @"(\d+\.\d+\.\d+\.\d+).*lladdr\s+([0-9a-fA-F:]+)");
                            
                            if (match.Success)
                            {
                                var ip = match.Groups[1].Value;
                                var mac = match.Groups[2].Value.ToUpperInvariant();
                                
                                if (mac != "00:00:00:00:00:00")
                                {
                                    results.Add((ip, mac));
                                    _logger.LogDebug("  ip neigh: {Ip} -> {Mac}", ip, mac);
                                }
                            }
                        }
                    }
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            line, @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]+)");
                        
                        if (match.Success)
                        {
                            var ip = match.Groups[1].Value;
                            var mac = match.Groups[2].Value.Replace("-", ":").ToUpperInvariant();
                            
                            if (mac != "FF:FF:FF:FF:FF:FF" && mac != "00:00:00:00:00:00")
                            {
                                results.Add((ip, mac));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lecture table ARP");
        }
        
        return results;
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
