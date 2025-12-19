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

    // Reseaux a ignorer (Docker, virtualisation, etc.)
    private static readonly string[] IgnoredSubnets = new[]
    {
        "172.17.",  // Docker bridge par defaut
        "172.18.",  // Docker networks
        "172.19.",  // Docker networks
        "172.20.",  // Docker networks
        "172.21.",  // Docker networks
        "172.22.",  // Docker networks
        "172.23.",  // Docker networks
        "172.24.",  // Docker networks
        "172.25.",  // Docker networks
        "172.26.",  // Docker networks
        "172.27.",  // Docker networks
        "172.28.",  // Docker networks
        "172.29.",  // Docker networks
        "172.30.",  // Docker networks
        "172.31.",  // Docker networks
        "169.254.", // Link-local (APIPA)
        "127.",     // Loopback
    };

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

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

    /// <summary>
    /// Verifie si une adresse MAC est localement administree (randomisee).
    /// Le bit U/L (bit 1 du premier octet) est a 1 pour les adresses locales.
    /// Cela inclut les MAC aleatoires d'iOS, Android, Windows, et les conteneurs Docker.
    /// </summary>
    private static bool IsLocallyAdministeredMac(string mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 2)
            return false;

        // Normaliser la MAC (enlever : ou -)
        var cleanMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (cleanMac.Length < 2)
            return false;

        // Le premier octet en hexa
        if (!int.TryParse(cleanMac.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int firstByte))
            return false;

        // Bit 1 (U/L) = 1 signifie localement administree
        // Le 2eme caractere hexa sera 2, 6, A ou E si le bit est set
        return (firstByte & 0x02) != 0;
    }

    /// <summary>
    /// Verifie si une adresse MAC est multicast (broadcast inclus).
    /// Le bit I/G (bit 0 du premier octet) est a 1 pour les multicast.
    /// </summary>
    private static bool IsMulticastMac(string mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 2)
            return false;

        var cleanMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (cleanMac.Length < 2)
            return false;

        if (!int.TryParse(cleanMac.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int firstByte))
            return false;

        // Bit 0 (I/G) = 1 signifie multicast/broadcast
        return (firstByte & 0x01) != 0;
    }

    /// <summary>
    /// Verifie si une IP appartient a un reseau a ignorer (Docker, link-local, etc.)
    /// </summary>
    private static bool IsIgnoredSubnet(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
            return false;

        foreach (var subnet in IgnoredSubnets)
        {
            if (ip.StartsWith(subnet))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verifie si un appareil doit etre ignore (faux positif)
    /// </summary>
    private bool ShouldIgnoreDevice(string mac, string? ip)
    {
        // Ignorer les MAC invalides
        if (string.IsNullOrEmpty(mac) ||
            mac == "00:00:00:00:00:00" ||
            mac == "FF:FF:FF:FF:FF:FF")
        {
            return true;
        }

        // Ignorer les MAC multicast
        if (IsMulticastMac(mac))
        {
            _logger.LogDebug("Ignoring multicast MAC: {Mac}", mac);
            return true;
        }

        // Ignorer les reseaux Docker/virtualisation
        if (IsIgnoredSubnet(ip))
        {
            _logger.LogDebug("Ignoring device on ignored subnet: {Mac} ({Ip})", mac, ip);
            return true;
        }

        // Ignorer les MAC localement administrees (randomisees) sur les reseaux Docker
        // Mais les garder sur le reseau principal (smartphones avec MAC random)
        if (IsLocallyAdministeredMac(mac) && IsIgnoredSubnet(ip))
        {
            _logger.LogDebug("Ignoring locally administered MAC on Docker network: {Mac}", mac);
            return true;
        }

        return false;
    }

    public async Task<string?> ResolveHostnameAsync(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return null;

        // Ne pas resoudre les IPs des reseaux ignores
        if (IsIgnoredSubnet(ipAddress))
            return null;

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
        var mac = packet.SourceMac?.ToUpperInvariant();
        
        // Filtrer les faux positifs
        if (ShouldIgnoreDevice(mac!, packet.SourceIp))
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (_recentlySeenMacs.TryGetValue(mac!, out var lastSeen) &&
            (now - lastSeen).TotalSeconds < 30)
        {
            return;
        }

        _recentlySeenMacs[mac!] = now;
        
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

            var existingDevice = await deviceRepo.GetByMacAddressAsync(mac!);
            var isNew = existingDevice == null;

            var hostname = !string.IsNullOrEmpty(packet.SourceIp) 
                ? await ResolveHostnameAsync(packet.SourceIp) 
                : null;

            var device = new NetworkDevice
            {
                MacAddress = mac!,
                IpAddress = packet.SourceIp,
                Hostname = hostname,
                Vendor = _ouiLookup.GetVendor(mac!),
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
            
            // Utiliser un dictionnaire pour eviter les doublons par MAC
            var devicesByMac = new Dictionary<string, NetworkDevice>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var localAddress in localAddresses)
            {
                // Ignorer les interfaces sur les reseaux Docker
                if (IsIgnoredSubnet(localAddress))
                {
                    _logger.LogInformation("SCAN: Ignoring Docker/virtual interface: {Addr}", localAddress);
                    continue;
                }

                var subnet = GetSubnet(localAddress);
                _logger.LogInformation("SCAN: Scan du sous-reseau {Subnet}.0/24", subnet);

                // METHODE 1: Lire directement la table ARP existante
                _logger.LogInformation("SCAN: Lecture de la table ARP existante...");
                var arpDevices = await ReadArpTableAsync();
                _logger.LogInformation("SCAN: {Count} entrees dans la table ARP", arpDevices.Count);
                
                int filteredCount = 0;
                foreach (var (ip, mac) in arpDevices)
                {
                    var normalizedMac = mac.ToUpperInvariant();
                    
                    // Filtrer les faux positifs
                    if (ShouldIgnoreDevice(normalizedMac, ip))
                    {
                        filteredCount++;
                        continue;
                    }

                    if (ip.StartsWith(subnet + "."))
                    {
                        // Verifier si on a deja cet appareil
                        if (devicesByMac.ContainsKey(normalizedMac))
                        {
                            // Mettre a jour l'IP si necessaire
                            devicesByMac[normalizedMac].IpAddress = ip;
                            continue;
                        }

                        var hostname = await ResolveHostnameAsync(ip);
                        var vendor = _ouiLookup.GetVendor(normalizedMac);
                        
                        devicesByMac[normalizedMac] = new NetworkDevice
                        {
                            MacAddress = normalizedMac,
                            IpAddress = ip,
                            Hostname = hostname,
                            Vendor = vendor,
                            Status = DeviceStatus.Online
                        };
                        _logger.LogInformation("  ARP: {Ip} -> {Mac} ({Vendor})", ip, normalizedMac, vendor ?? "Inconnu");
                    }
                }

                if (filteredCount > 0)
                {
                    _logger.LogInformation("SCAN: {Count} entrees ARP filtrees (Docker/MAC random)", filteredCount);
                }

                // METHODE 2: Ping pour decouvrir de nouveaux hotes (peut echouer sans root)
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

                // Attendre et relire la table ARP apres les pings
                if (respondingIps.Any())
                {
                    await Task.Delay(1000);
                    
                    var newArpDevices = await ReadArpTableAsync();
                    foreach (var (ip, mac) in newArpDevices)
                    {
                        var normalizedMac = mac.ToUpperInvariant();
                        
                        // Filtrer les faux positifs
                        if (ShouldIgnoreDevice(normalizedMac, ip))
                            continue;

                        // Verifier si on a deja cet appareil
                        if (devicesByMac.ContainsKey(normalizedMac))
                            continue;

                        if (ip.StartsWith(subnet + "."))
                        {
                            var hostname = await ResolveHostnameAsync(ip);
                            var vendor = _ouiLookup.GetVendor(normalizedMac);
                            
                            devicesByMac[normalizedMac] = new NetworkDevice
                            {
                                MacAddress = normalizedMac,
                                IpAddress = ip,
                                Hostname = hostname,
                                Vendor = vendor,
                                Status = DeviceStatus.Online
                            };
                            _logger.LogInformation("  NEW: {Ip} -> {Mac} ({Vendor})", ip, normalizedMac, vendor ?? "Inconnu");
                        }
                    }
                }
            }

            _logger.LogInformation("SCAN: Total {Count} appareils uniques trouves (apres filtrage)", devicesByMac.Count);
            _logger.LogInformation("SCAN: Sauvegarde en base...");

            int savedCount = 0;
            foreach (var device in devicesByMac.Values)
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

            var summary = $"Scan termine: {savedCount} appareils uniques";
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
    /// Lit la table ARP du systeme (fonctionne sans permissions root)
    /// </summary>
    private async Task<List<(string Ip, string Mac)>> ReadArpTableAsync()
    {
        var results = new List<(string Ip, string Mac)>();
        
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Methode 1: /proc/net/arp (la plus fiable sur Linux)
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
                
                // Methode 2: commande ip neigh
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

    private IEnumerable<string> GetLocalIPAddresses()
    {
        var addresses = new List<string>();
        
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            
            // Ignorer les interfaces Docker (docker0, br-xxx, veth-xxx)
            var name = ni.Name.ToLowerInvariant();
            if (name.StartsWith("docker") || 
                name.StartsWith("br-") || 
                name.StartsWith("veth") ||
                name.StartsWith("virbr"))
            {
                _logger.LogDebug("Ignoring virtual interface: {Name}", ni.Name);
                continue;
            }

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var ip = ua.Address.ToString();
                    
                    // Ne pas ajouter les IPs des reseaux ignores
                    if (!IsIgnoredSubnet(ip))
                    {
                        addresses.Add(ip);
                    }
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
