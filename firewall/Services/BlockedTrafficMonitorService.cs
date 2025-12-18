using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// Service d'arrière-plan qui surveille les tentatives de connexion bloquées
/// et génère des logs de sécurité en temps réel
/// </summary>
public class BlockedTrafficMonitorService : BackgroundService
{
    private readonly ILogger<BlockedTrafficMonitorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecurityLogService _securityLogService;
    
    // Cache pour éviter de générer trop de logs pour le même appareil
    private readonly ConcurrentDictionary<string, BlockedTrafficInfo> _blockedTrafficCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(5);

    public BlockedTrafficMonitorService(
        ILogger<BlockedTrafficMonitorService> logger,
        IServiceScopeFactory scopeFactory,
        ISecurityLogService securityLogService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _securityLogService = securityLogService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BlockedTrafficMonitorService démarré");

        // Attendre un peu pour laisser les autres services démarrer
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorBlockedTrafficAsync(stoppingToken);
                CleanupCache();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la surveillance du trafic bloqué");
            }

            await Task.Delay(_monitorInterval, stoppingToken);
        }

        _logger.LogInformation("BlockedTrafficMonitorService arrêté");
    }

    private async Task MonitorBlockedTrafficAsync(CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await MonitorWindowsFirewallAsync(ct);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await MonitorIptablesAsync(ct);
        }
    }

    private async Task MonitorWindowsFirewallAsync(CancellationToken ct)
    {
        try
        {
            // Lire les événements du journal de sécurité Windows
            // Event ID 5157 = The Windows Filtering Platform has blocked a connection
            var psi = new ProcessStartInfo
            {
                FileName = "wevtutil",
                Arguments = "qe Security /q:\"*[System[(EventID=5157)]]\" /c:50 /rd:true /f:text",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (string.IsNullOrEmpty(output)) return;

            // Parser les événements
            await ParseWindowsFirewallEventsAsync(output, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible de lire les événements Windows Firewall");
        }
    }

    private async Task ParseWindowsFirewallEventsAsync(string output, CancellationToken ct)
    {
        // Exemple de parsing simplifié - en production, utiliser un parser XML plus robuste
        var events = output.Split("Event[", StringSplitOptions.RemoveEmptyEntries);

        foreach (var eventData in events)
        {
            if (ct.IsCancellationRequested) break;

            // Extraire les informations de l'événement
            var sourceIpMatch = Regex.Match(eventData, @"Source Address:\s*(\d+\.\d+\.\d+\.\d+)");
            var destIpMatch = Regex.Match(eventData, @"Destination Address:\s*(\d+\.\d+\.\d+\.\d+)");
            var destPortMatch = Regex.Match(eventData, @"Destination Port:\s*(\d+)");
            var protocolMatch = Regex.Match(eventData, @"Protocol:\s*(\d+)");

            if (sourceIpMatch.Success && destIpMatch.Success)
            {
                var sourceIp = sourceIpMatch.Groups[1].Value;
                var destIp = destIpMatch.Groups[1].Value;
                var destPort = destPortMatch.Success ? int.Parse(destPortMatch.Groups[1].Value) : (int?)null;
                var protocol = protocolMatch.Success ? GetProtocolName(int.Parse(protocolMatch.Groups[1].Value)) : "Unknown";

                await ProcessBlockedConnectionAsync(null, sourceIp, destIp, destPort, protocol);
            }
        }
    }

    private async Task MonitorIptablesAsync(CancellationToken ct)
    {
        try
        {
            // Lire les compteurs iptables pour la chaîne WEBGUARD_BLOCK
            var psi = new ProcessStartInfo
            {
                FileName = "iptables",
                Arguments = "-L WEBGUARD_BLOCK -v -n",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (string.IsNullOrEmpty(output)) return;

            await ParseIptablesOutputAsync(output, ct);

            // Aussi lire le journal kernel pour les paquets DROP
            await ReadKernelLogForDropsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible de lire les compteurs iptables");
        }
    }

    private async Task ParseIptablesOutputAsync(string output, CancellationToken ct)
    {
        // Parser la sortie iptables -L -v -n
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(2)) // Skip header lines
        {
            if (ct.IsCancellationRequested) break;

            // Format: pkts bytes target prot opt in out source destination
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) continue;

            if (parts[2] == "DROP" && int.TryParse(parts[0], out var packetCount) && packetCount > 0)
            {
                var protocol = parts[3];
                var sourceIp = parts[7];
                var destIp = parts[8];

                // Extraire le port si présent dans les options
                int? destPort = null;
                var dptMatch = Regex.Match(line, @"dpt:(\d+)");
                if (dptMatch.Success)
                    destPort = int.Parse(dptMatch.Groups[1].Value);

                // Extraire la MAC si présente
                string? sourceMac = null;
                var macMatch = Regex.Match(line, @"MAC\s+([0-9A-Fa-f:]+)");
                if (macMatch.Success)
                    sourceMac = macMatch.Groups[1].Value;

                await ProcessBlockedConnectionAsync(sourceMac, sourceIp, destIp, destPort, protocol, packetCount);
            }
        }
    }

    private async Task ReadKernelLogForDropsAsync(CancellationToken ct)
    {
        try
        {
            // Lire les dernières entrées du journal kernel avec dmesg ou journalctl
            var psi = new ProcessStartInfo
            {
                FileName = "journalctl",
                Arguments = "-k --since '5 seconds ago' --no-pager -o short",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (string.IsNullOrEmpty(output)) return;

            // Chercher les lignes avec [WEBGUARD] ou [UFW BLOCK] ou [iptables]
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("WEBGUARD") || line.Contains("DROP") || line.Contains("BLOCK"))
                {
                    await ParseKernelDropLineAsync(line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible de lire le journal kernel");
        }
    }

    private async Task ParseKernelDropLineAsync(string line)
    {
        // Parser une ligne de log kernel iptables
        // Format typique: ... SRC=192.168.1.10 DST=8.8.8.8 ... DPT=443 ...
        var srcMatch = Regex.Match(line, @"SRC=(\d+\.\d+\.\d+\.\d+)");
        var dstMatch = Regex.Match(line, @"DST=(\d+\.\d+\.\d+\.\d+)");
        var dptMatch = Regex.Match(line, @"DPT=(\d+)");
        var protoMatch = Regex.Match(line, @"PROTO=(\w+)");
        var macMatch = Regex.Match(line, @"MAC=([0-9a-fA-F:]+)");

        if (srcMatch.Success && dstMatch.Success)
        {
            var sourceIp = srcMatch.Groups[1].Value;
            var destIp = dstMatch.Groups[1].Value;
            var destPort = dptMatch.Success ? int.Parse(dptMatch.Groups[1].Value) : (int?)null;
            var protocol = protoMatch.Success ? protoMatch.Groups[1].Value : "Unknown";
            var sourceMac = macMatch.Success ? macMatch.Groups[1].Value : null;

            await ProcessBlockedConnectionAsync(sourceMac, sourceIp, destIp, destPort, protocol);
        }
    }

    private async Task ProcessBlockedConnectionAsync(string? sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, int packetCount = 1)
    {
        // Ignorer les IPs locales/loopback
        if (IsLocalIp(sourceIp) && IsLocalIp(destIp)) return;

        var cacheKey = $"{sourceMac ?? sourceIp}:{destIp}:{destPort}";
        
        // Vérifier le cache pour éviter les duplicatas
        if (_blockedTrafficCache.TryGetValue(cacheKey, out var existingInfo))
        {
            existingInfo.PacketCount += packetCount;
            existingInfo.LastSeen = DateTime.UtcNow;
            
            // Logger seulement si le nombre de paquets a significativement augmenté
            if (existingInfo.PacketCount - existingInfo.LastLoggedCount >= 10)
            {
                existingInfo.LastLoggedCount = existingInfo.PacketCount;
                await LogBlockedConnectionAsync(sourceMac, sourceIp, destIp, destPort, protocol, existingInfo.PacketCount);
            }
        }
        else
        {
            // Nouvelle entrée
            _blockedTrafficCache.TryAdd(cacheKey, new BlockedTrafficInfo
            {
                SourceMac = sourceMac,
                SourceIp = sourceIp,
                DestinationIp = destIp,
                DestinationPort = destPort,
                Protocol = protocol,
                PacketCount = packetCount,
                LastLoggedCount = packetCount,
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });

            await LogBlockedConnectionAsync(sourceMac, sourceIp, destIp, destPort, protocol, packetCount);
        }
    }

    private async Task LogBlockedConnectionAsync(string? sourceMac, string? sourceIp, string? destIp, int? destPort, string? protocol, int packetCount)
    {
        string? deviceName = null;

        // Essayer de trouver le nom de l'appareil
        if (!string.IsNullOrEmpty(sourceMac) || !string.IsNullOrEmpty(sourceIp))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
                
                NetworkDevice? device = null;
                if (!string.IsNullOrEmpty(sourceMac))
                    device = await deviceRepo.GetByMacAddressAsync(sourceMac);
                if (device == null && !string.IsNullOrEmpty(sourceIp))
                    device = await deviceRepo.GetByIpAsync(sourceIp);

                if (device != null)
                {
                    deviceName = device.Description ?? device.Hostname ?? device.Vendor;
                    sourceMac ??= device.MacAddress;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Erreur lors de la recherche de l'appareil");
            }
        }

        await _securityLogService.LogBlockedConnectionAttemptAsync(
            sourceMac ?? "Unknown",
            sourceIp,
            destIp,
            destPort,
            protocol,
            packetCount,
            deviceName);
    }

    private void CleanupCache()
    {
        var now = DateTime.UtcNow;
        var keysToRemove = _blockedTrafficCache
            .Where(kvp => now - kvp.Value.LastSeen > _cacheExpiration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _blockedTrafficCache.TryRemove(key, out _);
        }
    }

    private static bool IsLocalIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        return ip.StartsWith("127.") || ip == "::1" || ip == "0.0.0.0";
    }

    private static string GetProtocolName(int protocolNumber)
    {
        return protocolNumber switch
        {
            1 => "ICMP",
            6 => "TCP",
            17 => "UDP",
            47 => "GRE",
            50 => "ESP",
            51 => "AH",
            58 => "ICMPv6",
            _ => $"Protocol_{protocolNumber}"
        };
    }

    private class BlockedTrafficInfo
    {
        public string? SourceMac { get; set; }
        public string? SourceIp { get; set; }
        public string? DestinationIp { get; set; }
        public int? DestinationPort { get; set; }
        public string? Protocol { get; set; }
        public int PacketCount { get; set; }
        public int LastLoggedCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
