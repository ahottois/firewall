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
            int totalResponding = 0;
            
            foreach (var localAddress in localAddresses)
            {
                var subnet = GetSubnet(localAddress);
                var ips = Enumerable.Range(1, 254).Select(i => $"{subnet}.{i}").ToList();
                _logger.LogInformation("SCAN: Ping du sous-reseau {Subnet}.0/24 (254 IPs)", subnet);

                var respondingIps = new ConcurrentBag<string>();
                
                await Parallel.ForEachAsync(ips, new ParallelOptions { MaxDegreeOfParallelism = 100 }, async (ip, ct) =>
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            respondingIps.Add(ip);
                            _logger.LogDebug("PING OK: {Ip}", ip);
                        }
                    }
                    catch { }
                });

                totalResponding += respondingIps.Count;
                _logger.LogInformation("SCAN: {Count} hote(s) repondent au ping sur {Subnet}.0/24", respondingIps.Count, subnet);

                if (respondingIps.Any())
                {
                    _logger.LogInformation("SCAN: Attente 500ms pour table ARP...");
                    await Task.Delay(500);
                    
                    _logger.LogInformation("SCAN: Recuperation des adresses MAC...");
                }

                foreach (var ip in respondingIps)
                {
                    try
                    {
                        var mac = await GetMacFromArpTableAsync(ip);
                        _logger.LogInformation("  ARP: {Ip} -> MAC={Mac}", ip, mac ?? "NULL");
                        
                        if (!string.IsNullOrEmpty(mac) && mac != "00:00:00:00:00:00")
                        {
                            var hostname = await ResolveHostnameAsync(ip);
                            var vendor = _ouiLookup.GetVendor(mac);
                            
                            var device = new NetworkDevice
                            {
                                MacAddress = mac.ToUpperInvariant(),
                                IpAddress = ip,
                                Hostname = hostname,
                                Vendor = vendor,
                                Status = DeviceStatus.Online
                            };
                            
                            devices.Add(device);
                            _logger.LogInformation("  DEVICE: {Mac} ({Ip}) - {Vendor}", mac, ip, vendor ?? "Inconnu");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("  ERREUR ARP pour {Ip}: {Error}", ip, ex.Message);
                    }
                }
            }

            _logger.LogInformation("SCAN: {Total} hotes ont repondu, {Devices} ont une MAC valide", totalResponding, devices.Count);
            _logger.LogInformation("SCAN: Sauvegarde des appareils en base...");

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

            var summary = $"Scan termine: {savedCount} appareils sauvegardes";
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

    private static async Task<string?> GetMacFromArpTableAsync(string ip)
    {
        try
        {
            if (OperatingSystem.IsWindows())
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
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith(ip + " ") || trimmedLine.Contains(" " + ip + " "))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(
                                line, @"([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})");

                            if (match.Success)
                            {
                                var mac = match.Value.Replace("-", ":").ToUpperInvariant();
                                if (mac != "FF:FF:FF:FF:FF:FF" && mac != "00:00:00:00:00:00")
                                {
                                    return mac;
                                }
                            }
                        }
                    }
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                // Methode 1: Lire /proc/net/arp
                if (File.Exists("/proc/net/arp"))
                {
                    var lines = await File.ReadAllLinesAsync("/proc/net/arp");
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 && parts[0] == ip)
                        {
                            var mac = parts[3].ToUpperInvariant();
                            if (mac != "00:00:00:00:00:00")
                                return mac;
                        }
                    }
                }

                // Methode 2: Commande arp
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-n {ip}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var match = System.Text.RegularExpressions.Regex.Match(
                        output, @"([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}");

                    if (match.Success)
                    {
                        var mac = match.Value.ToUpperInvariant();
                        if (mac != "00:00:00:00:00:00")
                            return mac;
                    }
                }
            }
        }
        catch { }

        return null;
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
