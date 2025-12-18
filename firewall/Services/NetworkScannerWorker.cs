using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// Service d'arrière-plan pour scanner le réseau périodiquement
/// et notifier les changements via SignalR
/// </summary>
public class NetworkScannerWorker : BackgroundService
{
    private readonly ILogger<NetworkScannerWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceHubNotifier _hubNotifier;
    private readonly IOuiLookupService _ouiLookup;
    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _offlineThreshold = TimeSpan.FromMinutes(2);

    // Cache des derniers états pour détecter les changements
    private readonly ConcurrentDictionary<string, DeviceStatus> _lastKnownStatus = new();
    
    // Cache des hostnames pour éviter des requêtes DNS répétées
    private readonly ConcurrentDictionary<string, (string? Hostname, DateTime CachedAt)> _hostnameCache = new();
    private static readonly TimeSpan HostnameCacheDuration = TimeSpan.FromMinutes(10);

    public NetworkScannerWorker(
        ILogger<NetworkScannerWorker> logger,
        IServiceScopeFactory scopeFactory,
        IDeviceHubNotifier hubNotifier,
        IOuiLookupService ouiLookup)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hubNotifier = hubNotifier;
        _ouiLookup = ouiLookup;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NetworkScannerWorker démarré - intervalle: {Interval}s", _scanInterval.TotalSeconds);

        // Attendre un peu au démarrage pour laisser l'application s'initialiser
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformNetworkScanAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du scan réseau");
            }

            await Task.Delay(_scanInterval, stoppingToken);
        }

        _logger.LogInformation("NetworkScannerWorker arrêté");
    }

    private async Task PerformNetworkScanAsync(CancellationToken ct)
    {
        _logger.LogDebug("Début du scan réseau périodique");

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        // Obtenir les appareils existants en base
        var existingDevices = (await deviceRepo.GetAllAsync()).ToDictionary(d => d.MacAddress.ToUpperInvariant());

        // Scanner le réseau
        var discoveredDevices = await ScanLocalNetworkAsync(ct);
        var discoveredMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int scanned = 0;
        int total = discoveredDevices.Count;

        foreach (var discovered in discoveredDevices)
        {
            if (ct.IsCancellationRequested) break;

            var mac = discovered.MacAddress.ToUpperInvariant();
            discoveredMacs.Add(mac);

            if (existingDevices.TryGetValue(mac, out var existing))
            {
                // Appareil existant - vérifier les changements
                var hasChanges = UpdateExistingDevice(existing, discovered);
                
                // Changement de statut vers Online
                var previousStatus = _lastKnownStatus.GetValueOrDefault(mac, existing.Status);
                if (existing.Status != DeviceStatus.Online || previousStatus != DeviceStatus.Online)
                {
                    existing.Status = DeviceStatus.Online;
                    existing.LastSeen = DateTime.UtcNow;
                    _lastKnownStatus[mac] = DeviceStatus.Online;

                    await deviceRepo.AddOrUpdateAsync(existing);
                    await _hubNotifier.NotifyDeviceStatusChanged(existing);
                    _logger.LogDebug("Appareil {Mac} passé en ligne", mac);
                }
                else if (hasChanges)
                {
                    existing.LastSeen = DateTime.UtcNow;
                    await deviceRepo.AddOrUpdateAsync(existing);
                    await _hubNotifier.NotifyDeviceUpdated(existing);
                }
                else
                {
                    existing.LastSeen = DateTime.UtcNow;
                    await deviceRepo.AddOrUpdateAsync(existing);
                }
            }
            else
            {
                // Nouvel appareil découvert
                discovered.Status = DeviceStatus.Online;
                discovered.FirstSeen = DateTime.UtcNow;
                discovered.LastSeen = DateTime.UtcNow;

                var saved = await deviceRepo.AddOrUpdateAsync(discovered);
                _lastKnownStatus[mac] = DeviceStatus.Online;

                await _hubNotifier.NotifyDeviceDiscovered(saved);
                _logger.LogInformation("Nouvel appareil découvert: {Mac} ({Ip})", mac, discovered.IpAddress);
            }

            scanned++;
            if (scanned % 10 == 0)
            {
                await _hubNotifier.NotifyScanProgress(scanned, total, discoveredMacs.Count);
            }
        }

        // Vérifier les appareils qui sont passés hors ligne
        await CheckOfflineDevicesAsync(deviceRepo, discoveredMacs, existingDevices);

        await _hubNotifier.NotifyScanComplete(discoveredMacs.Count);
        _logger.LogDebug("Scan terminé: {Count} appareils actifs", discoveredMacs.Count);
    }

    private static bool UpdateExistingDevice(NetworkDevice existing, NetworkDevice discovered)
    {
        bool hasChanges = false;

        if (existing.IpAddress != discovered.IpAddress && !string.IsNullOrEmpty(discovered.IpAddress))
        {
            existing.IpAddress = discovered.IpAddress;
            hasChanges = true;
        }

        if (!string.IsNullOrEmpty(discovered.Hostname) && existing.Hostname != discovered.Hostname)
        {
            existing.Hostname = discovered.Hostname;
            hasChanges = true;
        }

        if (string.IsNullOrEmpty(existing.Vendor) && !string.IsNullOrEmpty(discovered.Vendor))
        {
            existing.Vendor = discovered.Vendor;
            hasChanges = true;
        }

        return hasChanges;
    }

    private async Task CheckOfflineDevicesAsync(
        IDeviceRepository deviceRepo,
        HashSet<string> onlineMacs,
        Dictionary<string, NetworkDevice> existingDevices)
    {
        var now = DateTime.UtcNow;

        foreach (var (mac, device) in existingDevices)
        {
            if (device.Status == DeviceStatus.Blocked) continue;
            if (onlineMacs.Contains(mac)) continue;

            var previousStatus = _lastKnownStatus.GetValueOrDefault(mac, device.Status);
            if (previousStatus == DeviceStatus.Online && (now - device.LastSeen) > _offlineThreshold)
            {
                device.Status = DeviceStatus.Offline;
                _lastKnownStatus[mac] = DeviceStatus.Offline;

                await deviceRepo.UpdateStatusAsync(device.Id, DeviceStatus.Offline);
                await _hubNotifier.NotifyDeviceStatusChanged(device);

                _logger.LogDebug("Appareil {Mac} passé hors ligne", mac);
            }
        }
    }

    private async Task<List<NetworkDevice>> ScanLocalNetworkAsync(CancellationToken ct)
    {
        var devices = new ConcurrentBag<NetworkDevice>();
        var localAddresses = GetLocalIPAddresses().ToList();

        foreach (var localAddress in localAddresses)
        {
            if (ct.IsCancellationRequested) break;

            var subnet = GetSubnet(localAddress);
            var ips = Enumerable.Range(1, 254).Select(i => $"{subnet}.{i}").ToList();

            await Parallel.ForEachAsync(ips, new ParallelOptions 
            { 
                MaxDegreeOfParallelism = 50,
                CancellationToken = ct 
            }, async (ip, token) =>
            {
                try
                {
                    if (await PingHostAsync(ip, token))
                    {
                        var mac = await GetMacAddressAsync(ip);
                        if (!string.IsNullOrEmpty(mac) && mac != "00:00:00:00:00:00")
                        {
                            var hostname = await GetCachedHostnameAsync(ip);
                            devices.Add(new NetworkDevice
                            {
                                MacAddress = mac.ToUpperInvariant(),
                                IpAddress = ip,
                                Hostname = hostname,
                                Vendor = _ouiLookup.GetVendor(mac)
                            });
                        }
                    }
                }
                catch
                {
                    // Ignorer les erreurs individuelles
                }
            });
        }

        return devices.ToList();
    }

    private async Task<string?> GetCachedHostnameAsync(string ip)
    {
        // Vérifier le cache
        if (_hostnameCache.TryGetValue(ip, out var cached) && 
            DateTime.UtcNow - cached.CachedAt < HostnameCacheDuration)
        {
            return cached.Hostname;
        }

        var hostname = await ResolveHostnameAsync(ip);
        _hostnameCache[ip] = (hostname, DateTime.UtcNow);
        return hostname;
    }

    private static async Task<bool> PingHostAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 500);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> GetMacAddressAsync(string ip)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await GetMacFromArpWindows(ip);
            }
            else if (OperatingSystem.IsLinux())
            {
                return await GetMacFromArpLinux(ip);
            }
        }
        catch
        {
            // Ignorer
        }
        return null;
    }

    private static async Task<string?> GetMacFromArpWindows(string ip)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "arp",
            Arguments = $"-a {ip}",
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
                output, @"([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})");

            if (match.Success)
                return match.Value.Replace("-", ":").ToUpperInvariant();
        }
        return null;
    }

    private static async Task<string?> GetMacFromArpLinux(string ip)
    {
        if (!File.Exists("/proc/net/arp")) return null;

        var lines = await File.ReadAllLinesAsync("/proc/net/arp");
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[0] == ip)
            {
                var mac = parts[3];
                if (mac != "00:00:00:00:00:00")
                    return mac.ToUpperInvariant();
            }
        }
        return null;
    }

    private static async Task<string?> ResolveHostnameAsync(string ip)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var hostEntry = await System.Net.Dns.GetHostEntryAsync(ip, cts.Token);
            if (hostEntry.HostName != ip)
                return hostEntry.HostName;
        }
        catch
        {
            // Ignorer
        }
        return null;
    }

    private static IEnumerable<string> GetLocalIPAddresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    yield return ua.Address.ToString();
                }
            }
        }
    }

    private static string GetSubnet(string ip)
    {
        var parts = ip.Split('.');
        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }
}
