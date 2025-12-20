using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IWiFiService
{
    WiFiConfig GetConfig();
    Task UpdateConfigAsync(WiFiConfigDto config);
    Task<WiFiBandConfig> GetBandConfigAsync(WiFiBand band);
    Task UpdateBandConfigAsync(WiFiBand band, WiFiBandConfigDto config);
    Task<IEnumerable<WiFiClient>> GetClientsAsync();
    Task<WiFiStats> GetStatsAsync();
    Task<IEnumerable<ChannelAnalysis>> ScanChannelsAsync(WiFiBand band);
    Task<int> GetRecommendedChannelAsync(WiFiBand band);
    
    // Gestion du point d'accès
    Task<bool> StartAccessPointAsync();
    Task<bool> StopAccessPointAsync();
    Task<WiFiServiceStatus> GetServiceStatusAsync();
    Task<string> GetSetupInstructionsAsync();
    
    // Installation automatique
    Task<WiFiInstallationResult> InstallHostapdAsync();
    Task<WiFiInstallationResult> ConfigureAccessPointAsync();
    
    // Mesh
    MeshConfig? GetMeshConfig();
    Task UpdateMeshConfigAsync(MeshConfig config);
    Task<IEnumerable<MeshNode>> GetMeshNodesAsync();
    Task<MeshNode?> AddMeshNodeAsync(AddMeshNodeRequest request);
    Task RemoveMeshNodeAsync(string nodeId);
    Task OptimizeMeshAsync();
    
    // Actions
    Task RestartWiFiAsync();
    Task<bool> TestConnectionAsync();
}

public class WiFiServiceStatus
{
    public bool IsHostapdInstalled { get; set; }
    public bool IsHostapdRunning { get; set; }
    public bool HasWirelessInterface { get; set; }
    public string? WirelessInterface { get; set; }
    public string? CurrentSSID { get; set; }
    public int ConnectedClients { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> SetupSteps { get; set; } = new();
}

public class WiFiInstallationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> Steps { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string? Logs { get; set; }
}

public class WiFiService : IWiFiService
{
    private readonly ILogger<WiFiService> _logger;
    private WiFiConfig _config = new();
    private readonly ConcurrentDictionary<string, WiFiClient> _clients = new();
    private readonly ConcurrentDictionary<string, MeshNode> _meshNodes = new();
    
    private const string ConfigPath = "wifi_config.json";
    private const string HostapdConfigPath = "/etc/hostapd/hostapd.conf";
    private const string HostapdDefaultPath = "/etc/default/hostapd";
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public WiFiService(ILogger<WiFiService> logger)
    {
        _logger = logger;
        LoadConfig();
        InitializeDefaultBands();
        
        // Vérifier l'état au démarrage
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            var status = await GetServiceStatusAsync();
            if (status.HasWirelessInterface && !status.IsHostapdRunning && _config.Enabled)
            {
                _logger.LogWarning("WiFi: Interface sans fil détectée mais hostapd n'est pas en cours d'exécution");
            }
        });
    }

    private void InitializeDefaultBands()
    {
        if (_config.Bands.Count == 0)
        {
            _config.Bands = new List<WiFiBandConfig>
            {
                new WiFiBandConfig
                {
                    Id = 1,
                    Band = WiFiBand.Band_2_4GHz,
                    Enabled = true,
                    SSID = _config.GlobalSSID,
                    UseGlobalSSID = true,
                    Security = WiFiSecurity.WPA3_Personal,
                    Channel = 0, // Auto
                    ChannelWidth = ChannelWidth.Width_40MHz,
                    MaxStandard = WiFiStandard.WiFi6_AX,
                    TxPower = 100,
                    BandSteering = true,
                    Beamforming = true,
                    MuMimo = true,
                    OFDMA = true,
                    TWT = true,
                    MaxSpeed = 574 // WiFi 6 2x2 40MHz
                },
                new WiFiBandConfig
                {
                    Id = 2,
                    Band = WiFiBand.Band_5GHz,
                    Enabled = true,
                    SSID = _config.GlobalSSID,
                    UseGlobalSSID = true,
                    Security = WiFiSecurity.WPA3_Personal,
                    Channel = 0, // Auto
                    ChannelWidth = ChannelWidth.Width_80MHz,
                    MaxStandard = WiFiStandard.WiFi6_AX,
                    TxPower = 100,
                    BandSteering = true,
                    Beamforming = true,
                    MuMimo = true,
                    OFDMA = true,
                    TWT = true,
                    Mode160MHz = false,
                    MaxSpeed = 2402 // WiFi 6 2x2 80MHz
                },
                new WiFiBandConfig
                {
                    Id = 3,
                    Band = WiFiBand.Band_6GHz,
                    Enabled = false, // Désactivé par défaut (WiFi 6E/7)
                    SSID = _config.GlobalSSID,
                    UseGlobalSSID = true,
                    Security = WiFiSecurity.WPA3_Personal, // WPA3 obligatoire sur 6GHz
                    Channel = 0,
                    ChannelWidth = ChannelWidth.Width_160MHz,
                    MaxStandard = WiFiStandard.WiFi6E_AX,
                    TxPower = 100,
                    BandSteering = false, // Pas de steering vers 6GHz par défaut
                    Beamforming = true,
                    MuMimo = true,
                    OFDMA = true,
                    TWT = true,
                    Mode160MHz = true,
                    MaxSpeed = 4804 // WiFi 6E 2x2 160MHz
                }
            };
            SaveConfig();
        }
    }

    public WiFiConfig GetConfig() => _config;

    // ==========================================
    // GESTION DU POINT D'ACCÈS
    // ==========================================

    public async Task<WiFiServiceStatus> GetServiceStatusAsync()
    {
        var status = new WiFiServiceStatus();

        if (!IsLinux)
        {
            status.ErrorMessage = "La création de point d'accès WiFi n'est supportée que sur Linux";
            status.SetupSteps.Add("Déployez WebGuard sur un système Linux avec une carte WiFi compatible");
            return status;
        }

        try
        {
            // Vérifier si hostapd est installé
            var hostapdCheck = await ExecuteCommandAsync("which", "hostapd");
            status.IsHostapdInstalled = !string.IsNullOrWhiteSpace(hostapdCheck);

            if (!status.IsHostapdInstalled)
            {
                status.SetupSteps.Add("Installer hostapd: sudo apt install hostapd");
            }

            // Vérifier si hostapd est en cours d'exécution
            var serviceStatus = await ExecuteCommandAsync("systemctl", "is-active hostapd");
            status.IsHostapdRunning = serviceStatus.Trim() == "active";

            // Trouver l'interface sans fil
            var interfaces = await GetWirelessInterfacesAsync();
            status.HasWirelessInterface = interfaces.Any();
            status.WirelessInterface = interfaces.FirstOrDefault();

            if (!status.HasWirelessInterface)
            {
                status.SetupSteps.Add("Aucune interface sans fil détectée. Vérifiez que votre carte WiFi est compatible.");
                status.SetupSteps.Add("Commande pour vérifier: iw dev");
            }
            else
            {
                // Vérifier si l'interface supporte le mode AP
                var phyInfo = await ExecuteCommandAsync("iw", $"phy phy0 info");
                if (!phyInfo.Contains("* AP"))
                {
                    status.SetupSteps.Add($"L'interface {status.WirelessInterface} ne supporte pas le mode AP (point d'accès)");
                }
            }

            // Récupérer le SSID actuel si hostapd est actif
            if (status.IsHostapdRunning)
            {
                status.CurrentSSID = _config.GlobalSSID;
                
                // Compter les clients
                var clients = await GetClientsAsync();
                status.ConnectedClients = clients.Count();
            }

            // Instructions de configuration si nécessaire
            if (!status.IsHostapdInstalled)
            {
                status.SetupSteps.Add("1. Installer hostapd: sudo apt update && sudo apt install hostapd");
                status.SetupSteps.Add("2. Démasquer le service: sudo systemctl unmask hostapd");
                status.SetupSteps.Add("3. Activer le service: sudo systemctl enable hostapd");
            }
            else if (!status.IsHostapdRunning && status.HasWirelessInterface)
            {
                status.SetupSteps.Add("hostapd est installé mais pas en cours d'exécution.");
                status.SetupSteps.Add("Cliquez sur 'Démarrer le point d'accès' pour l'activer.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur vérification status");
            status.ErrorMessage = ex.Message;
        }

        return status;
    }

    public async Task<string> GetSetupInstructionsAsync()
    {
        var status = await GetServiceStatusAsync();
        
        var instructions = @"
# Configuration du Point d'Accès WiFi

## Prérequis
- Système Linux (Raspberry Pi, Ubuntu, Debian, etc.)
- Carte WiFi supportant le mode AP (Access Point)
- Connexion Internet via Ethernet (recommandé)

## Installation

### 1. Installer les paquets nécessaires
```bash
sudo apt update
sudo apt install hostapd dnsmasq iptables-persistent
```

### 2. Configurer l'interface réseau
Éditez `/etc/dhcpcd.conf` et ajoutez:
```
interface wlan0
    static ip_address=192.168.4.1/24
    nohook wpa_supplicant
```

### 3. Configurer le serveur DHCP
Éditez `/etc/dnsmasq.conf`:
```
interface=wlan0
dhcp-range=192.168.4.2,192.168.4.20,255.255.255.0,24h
```

### 4. Activer le routage IP
```bash
sudo sysctl -w net.ipv4.ip_forward=1
echo 'net.ipv4.ip_forward=1' | sudo tee -a /etc/sysctl.conf
```

### 5. Configurer le NAT (pour partager Internet)
```bash
sudo iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
sudo netfilter-persistent save
```

### 6. Démarrer les services
```bash
sudo systemctl unmask hostapd
sudo systemctl enable hostapd dnsmasq
sudo systemctl start hostapd dnsmasq
```

## Configuration via WebGuard
Une fois ces étapes terminées, vous pourrez configurer le SSID et le mot de passe directement depuis cette interface.
";

        if (status.SetupSteps.Any())
        {
            instructions += "\n\n## État actuel\n";
            foreach (var step in status.SetupSteps)
            {
                instructions += $"- {step}\n";
            }
        }

        return instructions;
    }

    private async Task<List<string>> GetWirelessInterfacesAsync()
    {
        var interfaces = new List<string>();
        
        try
        {
            // Utiliser iw pour lister les interfaces sans fil
            var result = await ExecuteCommandAsync("iw", "dev");
            
            var lines = result.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Interface"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        interfaces.Add(parts[1]);
                    }
                }
            }

            // Fallback: chercher dans /sys/class/net
            if (!interfaces.Any() && Directory.Exists("/sys/class/net"))
            {
                foreach (var dir in Directory.GetDirectories("/sys/class/net"))
                {
                    var wirelessDir = Path.Combine(dir, "wireless");
                    if (Directory.Exists(wirelessDir))
                    {
                        interfaces.Add(Path.GetFileName(dir));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WiFi: Erreur détection interfaces");
        }

        return interfaces;
    }

    public async Task<bool> StartAccessPointAsync()
    {
        if (!IsLinux)
        {
            _logger.LogWarning("WiFi: StartAccessPoint n'est supporté que sur Linux");
            return false;
        }

        try
        {
            var status = await GetServiceStatusAsync();
            
            if (!status.IsHostapdInstalled)
            {
                _logger.LogError("WiFi: hostapd n'est pas installé");
                return false;
            }

            if (!status.HasWirelessInterface)
            {
                _logger.LogError("WiFi: Aucune interface sans fil disponible");
                return false;
            }

            // Générer la configuration hostapd
            await GenerateHostapdConfigAsync();

            // Configurer le fichier /etc/default/hostapd
            await ConfigureHostapdDefaultAsync();

            // Arrêter wpa_supplicant si actif (conflit avec hostapd)
            await ExecuteCommandAsync("systemctl", "stop wpa_supplicant");

            // Démarrer hostapd
            var result = await ExecuteCommandAsync("systemctl", "start hostapd");
            await Task.Delay(2000);

            // Vérifier que le service est bien démarré
            var checkStatus = await ExecuteCommandAsync("systemctl", "is-active hostapd");
            var success = checkStatus.Trim() == "active";

            if (success)
            {
                _config.Enabled = true;
                SaveConfig();
                _logger.LogInformation("WiFi: Point d'accès démarré avec SSID: {SSID}", _config.GlobalSSID);
            }
            else
            {
                // Récupérer les logs d'erreur
                var logs = await ExecuteCommandAsync("journalctl", "-u hostapd -n 20 --no-pager");
                _logger.LogError("WiFi: Échec démarrage hostapd. Logs:\n{Logs}", logs);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur démarrage point d'accès");
            return false;
        }
    }

    public async Task<bool> StopAccessPointAsync()
    {
        if (!IsLinux) return false;

        try
        {
            await ExecuteCommandAsync("systemctl", "stop hostapd");
            _config.Enabled = false;
            SaveConfig();
            _logger.LogInformation("WiFi: Point d'accès arrêté");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur arrêt point d'accès");
            return false;
        }
    }

    private async Task ConfigureHostapdDefaultAsync()
    {
        var content = $"DAEMON_CONF=\"{HostapdConfigPath}\"";
        
        try
        {
            await File.WriteAllTextAsync(HostapdDefaultPath, content);
            _logger.LogDebug("WiFi: Fichier /etc/default/hostapd configuré");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Impossible d'écrire dans {Path}", HostapdDefaultPath);
        }
    }

    public async Task UpdateConfigAsync(WiFiConfigDto dto)
    {
        _config.GlobalSSID = dto.GlobalSSID;
        if (!string.IsNullOrEmpty(dto.GlobalPassword))
            _config.GlobalPassword = dto.GlobalPassword;
        _config.GlobalSecurity = dto.GlobalSecurity;
        _config.Enabled = dto.Enabled;
        _config.HideSSID = dto.HideSSID;
        _config.SmartConnect = dto.SmartConnect;
        _config.FastRoaming = dto.FastRoaming;
        _config.GuestNetworkEnabled = dto.GuestNetworkEnabled;
        
        if (!string.IsNullOrEmpty(dto.GuestSSID))
            _config.GuestSSID = dto.GuestSSID;
        if (!string.IsNullOrEmpty(dto.GuestPassword))
            _config.GuestPassword = dto.GuestPassword;
        _config.GuestBandwidthLimit = dto.GuestBandwidthLimit;

        // Mettre à jour le SSID sur toutes les bandes si SmartConnect
        if (_config.SmartConnect)
        {
            foreach (var band in _config.Bands.Where(b => b.UseGlobalSSID))
            {
                band.SSID = _config.GlobalSSID;
                band.Password = _config.GlobalPassword;
                band.Security = _config.GlobalSecurity;
            }
        }

        SaveConfig();
        await ApplyConfigAsync();
        
        _logger.LogInformation("WiFi: Configuration mise à jour - SSID: {SSID}", _config.GlobalSSID);
    }

    public Task<WiFiBandConfig> GetBandConfigAsync(WiFiBand band)
    {
        var config = _config.Bands.FirstOrDefault(b => b.Band == band);
        if (config == null)
        {
            throw new KeyNotFoundException($"Bande {band} non trouvée");
        }
        return Task.FromResult(config);
    }

    public async Task UpdateBandConfigAsync(WiFiBand band, WiFiBandConfigDto dto)
    {
        var config = _config.Bands.FirstOrDefault(b => b.Band == band);
        if (config == null)
        {
            throw new KeyNotFoundException($"Bande {band} non trouvée");
        }

        config.Enabled = dto.Enabled;
        config.UseGlobalSSID = dto.UseGlobalSSID;
        
        if (!dto.UseGlobalSSID)
        {
            config.SSID = dto.SSID ?? _config.GlobalSSID;
            if (!string.IsNullOrEmpty(dto.Password))
                config.Password = dto.Password;
            config.Security = dto.Security;
        }
        else
        {
            config.SSID = _config.GlobalSSID;
            config.Password = _config.GlobalPassword;
            config.Security = _config.GlobalSecurity;
        }

        config.Channel = dto.Channel;
        config.ChannelWidth = dto.ChannelWidth;
        config.TxPower = Math.Clamp(dto.TxPower, 10, 100);
        config.BandSteering = dto.BandSteering;
        config.Beamforming = dto.Beamforming;
        config.MuMimo = dto.MuMimo;
        config.OFDMA = dto.OFDMA;

        ValidateChannelWidth(config);
        config.MaxSpeed = CalculateMaxSpeed(config);

        SaveConfig();
        await ApplyBandConfigAsync(config);
        
        _logger.LogInformation("WiFi: Configuration de la bande {Band} mise à jour", band);
    }

    private void ValidateChannelWidth(WiFiBandConfig config)
    {
        switch (config.Band)
        {
            case WiFiBand.Band_2_4GHz:
                if (config.ChannelWidth > ChannelWidth.Width_40MHz)
                    config.ChannelWidth = ChannelWidth.Width_40MHz;
                break;
                
            case WiFiBand.Band_5GHz:
                if (config.ChannelWidth > ChannelWidth.Width_160MHz)
                    config.ChannelWidth = ChannelWidth.Width_160MHz;
                break;
                
            case WiFiBand.Band_6GHz:
                if (config.MaxStandard != WiFiStandard.WiFi7_BE && 
                    config.ChannelWidth > ChannelWidth.Width_160MHz)
                    config.ChannelWidth = ChannelWidth.Width_160MHz;
                break;
        }
    }

    private int CalculateMaxSpeed(WiFiBandConfig config)
    {
        int baseSpeed = config.ChannelWidth switch
        {
            ChannelWidth.Width_20MHz => 287,
            ChannelWidth.Width_40MHz => 574,
            ChannelWidth.Width_80MHz => 1201,
            ChannelWidth.Width_160MHz => 2402,
            ChannelWidth.Width_320MHz => 4804,
            _ => 574
        };

        double multiplier = config.MaxStandard switch
        {
            WiFiStandard.WiFi4_N => 0.25,
            WiFiStandard.WiFi5_AC => 0.75,
            WiFiStandard.WiFi6_AX => 1.0,
            WiFiStandard.WiFi6E_AX => 1.0,
            WiFiStandard.WiFi7_BE => 1.4,
            _ => 1.0
        };

        return (int)(baseSpeed * multiplier);
    }

    public async Task<IEnumerable<WiFiClient>> GetClientsAsync()
    {
        if (IsLinux)
        {
            await RefreshClientsFromHostapdAsync();
        }
        return _clients.Values.OrderByDescending(c => c.RSSI);
    }

    private async Task RefreshClientsFromHostapdAsync()
    {
        try
        {
            // Utiliser hostapd_cli pour lister les clients
            var result = await ExecuteCommandAsync("hostapd_cli", "all_sta");
            if (string.IsNullOrEmpty(result)) return;

            var lines = result.Split('\n');
            string? currentMac = null;
            
            foreach (var line in lines)
            {
                if (line.Contains(':') && line.Length == 17)
                {
                    currentMac = line.Trim().ToUpper();
                    if (!_clients.ContainsKey(currentMac))
                    {
                        _clients[currentMac] = new WiFiClient { MacAddress = currentMac };
                    }
                }
                else if (currentMac != null && _clients.TryGetValue(currentMac, out var client))
                {
                    ParseClientInfo(client, line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WiFi: Erreur lecture clients hostapd");
        }
    }

    private void ParseClientInfo(WiFiClient client, string line)
    {
        var parts = line.Split('=');
        if (parts.Length != 2) return;

        var key = parts[0].Trim();
        var value = parts[1].Trim();

        switch (key)
        {
            case "signal":
                if (int.TryParse(value, out var rssi))
                    client.RSSI = rssi;
                break;
            case "rx_bytes":
                if (long.TryParse(value, out var rx))
                    client.RxBytes = rx;
                break;
            case "tx_bytes":
                if (long.TryParse(value, out var tx))
                    client.TxBytes = tx;
                break;
            case "connected_time":
                if (long.TryParse(value, out var time))
                    client.ConnectionTime = time;
                break;
        }
    }

    public async Task<WiFiStats> GetStatsAsync()
    {
        var clients = await GetClientsAsync();
        var clientList = clients.ToList();

        var stats = new WiFiStats
        {
            TotalClients = clientList.Count,
            Clients2_4GHz = clientList.Count(c => c.Band == WiFiBand.Band_2_4GHz),
            Clients5GHz = clientList.Count(c => c.Band == WiFiBand.Band_5GHz),
            Clients6GHz = clientList.Count(c => c.Band == WiFiBand.Band_6GHz),
            TotalTxBytes = clientList.Sum(c => c.TxBytes),
            TotalRxBytes = clientList.Sum(c => c.RxBytes),
            AverageRSSI = clientList.Any() ? (int)clientList.Average(c => c.RSSI) : 0,
            AverageTxRate = clientList.Any() ? (int)clientList.Average(c => c.TxRate) : 0,
            AverageRxRate = clientList.Any() ? (int)clientList.Average(c => c.RxRate) : 0,
            MeshNodes = _meshNodes.Count,
            MeshNodesOnline = _meshNodes.Values.Count(n => n.Status == MeshNodeStatus.Online)
        };

        stats.ClientsByStandard = clientList
            .GroupBy(c => c.Standard)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.ClientsByChannel = clientList
            .GroupBy(c => c.Channel)
            .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    public async Task<IEnumerable<ChannelAnalysis>> ScanChannelsAsync(WiFiBand band)
    {
        var analyses = new List<ChannelAnalysis>();
        var channels = GetChannelsForBand(band);

        foreach (var channel in channels)
        {
            var analysis = new ChannelAnalysis
            {
                Band = band,
                Channel = channel,
                IsDFS = IsDFSChannel(band, channel),
                Networks = new List<WiFiNetwork>()
            };

            if (IsLinux)
            {
                var networks = await ScanNetworksOnChannelAsync(band, channel);
                analysis.Networks.AddRange(networks);
                analysis.NetworkCount = networks.Count;
                analysis.Utilization = Math.Min(100, networks.Count * 15);
                analysis.InterferenceScore = CalculateInterferenceScore(networks);
            }
            else
            {
                var random = new Random(channel);
                analysis.NetworkCount = random.Next(0, 8);
                analysis.Utilization = random.Next(5, 70);
                analysis.InterferenceScore = random.Next(0, 100);
            }

            analyses.Add(analysis);
        }

        var minInterference = analyses.Min(a => a.InterferenceScore);
        foreach (var a in analyses.Where(a => a.InterferenceScore <= minInterference + 10 && !a.IsDFS))
        {
            a.IsRecommended = true;
        }

        return analyses;
    }

    private List<int> GetChannelsForBand(WiFiBand band)
    {
        return band switch
        {
            WiFiBand.Band_2_4GHz => new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            WiFiBand.Band_5GHz => new List<int> { 36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144, 149, 153, 157, 161, 165 },
            WiFiBand.Band_6GHz => Enumerable.Range(1, 59).Select(i => i * 4 + 1).ToList(),
            _ => new List<int>()
        };
    }

    private bool IsDFSChannel(WiFiBand band, int channel)
    {
        if (band != WiFiBand.Band_5GHz) return false;
        return (channel >= 52 && channel <= 64) || (channel >= 100 && channel <= 144);
    }

    private async Task<List<WiFiNetwork>> ScanNetworksOnChannelAsync(WiFiBand band, int channel)
    {
        var networks = new List<WiFiNetwork>();
        
        try
        {
            var result = await ExecuteCommandAsync("iw", $"dev wlan0 scan freq {ChannelToFrequency(band, channel)}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WiFi: Erreur scan canal {Channel}", channel);
        }

        return networks;
    }

    private int ChannelToFrequency(WiFiBand band, int channel)
    {
        return band switch
        {
            WiFiBand.Band_2_4GHz => 2407 + (channel * 5),
            WiFiBand.Band_5GHz => channel <= 48 ? 5000 + (channel * 5) : 5000 + (channel * 5),
            WiFiBand.Band_6GHz => 5950 + (channel * 5),
            _ => 0
        };
    }

    private int CalculateInterferenceScore(List<WiFiNetwork> networks)
    {
        if (!networks.Any()) return 0;
        
        var score = networks.Count * 10;
        score += networks.Count(n => n.RSSI > -50) * 20;
        score += networks.Count(n => n.ChannelWidth >= ChannelWidth.Width_80MHz) * 15;
        
        return Math.Min(100, score);
    }

    public async Task<int> GetRecommendedChannelAsync(WiFiBand band)
    {
        var analyses = await ScanChannelsAsync(band);
        var recommended = analyses
            .Where(a => a.IsRecommended)
            .OrderBy(a => a.InterferenceScore)
            .FirstOrDefault();

        return recommended?.Channel ?? (band == WiFiBand.Band_2_4GHz ? 6 : 36);
    }

    // ==========================================
    // MESH
    // ==========================================

    public MeshConfig? GetMeshConfig() => _config.Mesh;

    public async Task UpdateMeshConfigAsync(MeshConfig config)
    {
        _config.Mesh = config;
        
        if (config.Enabled && string.IsNullOrEmpty(config.MeshKey))
        {
            config.MeshKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }

        SaveConfig();
        
        if (config.Enabled)
        {
            await EnableMeshAsync();
        }
        else
        {
            await DisableMeshAsync();
        }
        
        _logger.LogInformation("WiFi: Configuration Mesh mise à jour (Enabled: {Enabled})", config.Enabled);
    }

    public Task<IEnumerable<MeshNode>> GetMeshNodesAsync()
    {
        foreach (var node in _meshNodes.Values)
        {
            if (node.LastSeen < DateTime.UtcNow.AddMinutes(-2))
            {
                node.Status = MeshNodeStatus.Offline;
            }
        }

        return Task.FromResult(_meshNodes.Values.AsEnumerable());
    }

    public async Task<MeshNode?> AddMeshNodeAsync(AddMeshNodeRequest request)
    {
        if (string.IsNullOrEmpty(request.MacAddress))
            return null;

        var normalizedMac = request.MacAddress.ToUpper().Replace("-", ":");
        
        var node = new MeshNode
        {
            Id = Guid.NewGuid().ToString(),
            MacAddress = normalizedMac,
            Name = request.Name,
            Location = request.Location,
            Role = MeshNodeRole.Agent,
            Status = MeshNodeStatus.Connecting,
            LastSeen = DateTime.UtcNow
        };

        _meshNodes[node.Id] = node;
        
        if (_config.Mesh != null)
        {
            _config.Mesh.Nodes.Add(node);
            SaveConfig();
        }

        await TryConnectMeshNodeAsync(node);

        _logger.LogInformation("WiFi Mesh: Nœud ajouté {Name} ({Mac})", node.Name, node.MacAddress);
        return node;
    }

    public async Task RemoveMeshNodeAsync(string nodeId)
    {
        if (_meshNodes.TryRemove(nodeId, out var node))
        {
            if (_config.Mesh != null)
            {
                _config.Mesh.Nodes.RemoveAll(n => n.Id == nodeId);
                SaveConfig();
            }

            await DisconnectMeshNodeAsync(node);
            
            _logger.LogInformation("WiFi Mesh: Nœud supprimé {Name}", node.Name);
        }
    }

    public async Task OptimizeMeshAsync()
    {
        if (_config.Mesh == null || !_config.Mesh.Enabled) return;

        _logger.LogInformation("WiFi Mesh: Optimisation en cours...");

        foreach (var node in _meshNodes.Values.Where(n => n.Status == MeshNodeStatus.Online))
        {
            if (node.BackhaulQuality < 50 && node.BackhaulType == MeshBackhaul.WiFi)
            {
                var betterParent = FindBetterParentNode(node);
                if (betterParent != null)
                {
                    _logger.LogInformation("WiFi Mesh: Réaffectation de {Node} vers {Parent}", 
                        node.Name, betterParent.Name);
                    node.ParentNodeId = betterParent.Id;
                }
            }
        }

        if (_config.Mesh.DedicatedBackhaulBand.HasValue)
        {
            var optimalChannel = await GetRecommendedChannelAsync(_config.Mesh.DedicatedBackhaulBand.Value);
            _logger.LogInformation("WiFi Mesh: Canal backhaul optimal: {Channel}", optimalChannel);
        }
    }

    private MeshNode? FindBetterParentNode(MeshNode node)
    {
        var potentialParents = _meshNodes.Values
            .Where(n => n.Id != node.Id && 
                       n.Status == MeshNodeStatus.Online &&
                       n.HopCount < node.HopCount)
            .OrderBy(n => n.HopCount)
            .ThenByDescending(n => n.BackhaulQuality);

        return potentialParents.FirstOrDefault();
    }

    private async Task EnableMeshAsync()
    {
        if (!IsLinux) return;

        try
        {
            await ExecuteCommandAsync("iw", $"dev wlan0 interface add mesh0 type mp");
            await ExecuteCommandAsync("iw", $"dev mesh0 mesh join {_config.Mesh?.MeshId}");
            _logger.LogInformation("WiFi Mesh: Activé");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi Mesh: Erreur activation");
        }
    }

    private async Task DisableMeshAsync()
    {
        if (!IsLinux) return;

        try
        {
            await ExecuteCommandAsync("iw", "dev mesh0 del");
            _logger.LogInformation("WiFi Mesh: Désactivé");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WiFi Mesh: Erreur désactivation");
        }
    }

    private Task TryConnectMeshNodeAsync(MeshNode node)
    {
        return Task.CompletedTask;
    }

    private Task DisconnectMeshNodeAsync(MeshNode node)
    {
        return Task.CompletedTask;
    }

    // ==========================================
    // ACTIONS
    // ==========================================

    public async Task RestartWiFiAsync()
    {
        _logger.LogInformation("WiFi: Redémarrage...");

        if (IsLinux)
        {
            await ExecuteCommandAsync("systemctl", "restart hostapd");
            await Task.Delay(2000);
        }

        _logger.LogInformation("WiFi: Redémarré");
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 3000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyConfigAsync()
    {
        if (!IsLinux) return;

        try
        {
            await GenerateHostapdConfigAsync();
            
            // Vérifier si hostapd est actif avant de recharger
            var status = await ExecuteCommandAsync("systemctl", "is-active hostapd");
            if (status.Trim() == "active")
            {
                await ExecuteCommandAsync("systemctl", "reload hostapd");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur application config");
        }
    }

    private async Task ApplyBandConfigAsync(WiFiBandConfig config)
    {
        if (!IsLinux) return;
        await ApplyConfigAsync();
    }

    private async Task GenerateHostapdConfigAsync()
    {
        var mainBand = _config.Bands.FirstOrDefault(b => b.Enabled && b.Band == WiFiBand.Band_5GHz)
                    ?? _config.Bands.FirstOrDefault(b => b.Enabled);

        if (mainBand == null) return;

        // Trouver l'interface sans fil
        var interfaces = await GetWirelessInterfacesAsync();
        var wifiInterface = interfaces.FirstOrDefault() ?? "wlan0";

        var hwMode = mainBand.Band == WiFiBand.Band_2_4GHz ? "g" : "a";
        var channel = mainBand.Channel == 0 ? "1" : mainBand.Channel.ToString(); // Utiliser canal 1 par défaut au lieu de acs_survey
        
        // Sécurité WPA
        var wpaKeyMgmt = mainBand.Security >= WiFiSecurity.WPA3_Personal ? "SAE" : "WPA-PSK";
        var wpaPairwise = "CCMP";
        var ieeeFlags = mainBand.Security >= WiFiSecurity.WPA3_Personal ? "ieee80211w=2" : "";

        var config = $@"# Configuration générée par WebGuard
interface={wifiInterface}
driver=nl80211
ssid={_config.GlobalSSID}
hw_mode={hwMode}
channel={channel}
wmm_enabled=1
macaddr_acl=0
auth_algs=1
ignore_broadcast_ssid={(_config.HideSSID ? 1 : 0)}

# Sécurité WPA
wpa=2
wpa_passphrase={_config.GlobalPassword}
wpa_key_mgmt={wpaKeyMgmt}
wpa_pairwise={wpaPairwise}
rsn_pairwise=CCMP
{ieeeFlags}

# WiFi N (802.11n)
ieee80211n=1
ht_capab=[HT40+][SHORT-GI-20][SHORT-GI-40]

# WiFi AC (802.11ac) - pour 5GHz
{(mainBand.Band != WiFiBand.Band_2_4GHz ? "ieee80211ac=1" : "")}

# WiFi 6 (802.11ax)
{(mainBand.MaxStandard >= WiFiStandard.WiFi6_AX ? $@"ieee80211ax=1
he_su_beamformer={(mainBand.Beamforming ? 1 : 0)}
he_mu_beamformer={(mainBand.MuMimo ? 1 : 0)}" : "")}

# Fast Roaming (802.11r)
{(_config.FastRoaming ? @"ieee80211r=1
ft_over_ds=0
mobility_domain=a1b2" : "")}

# Régulation pays (important!)
country_code=FR
ieee80211d=1
";

        try
        {
            // Créer le répertoire si nécessaire
            var dir = Path.GetDirectoryName(HostapdConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(HostapdConfigPath, config);
            _logger.LogInformation("WiFi: Configuration hostapd générée pour interface {Interface}", wifiInterface);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Impossible d'écrire la configuration hostapd");
        }
    }

    private async Task<string> ExecuteCommandAsync(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
            {
                _logger.LogDebug("WiFi: Erreur commande {Command}: {Error}", command, error);
            }

            return output;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WiFi: Erreur exécution {Command}", command);
            return string.Empty;
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<WiFiConfig>(json) ?? new WiFiConfig();
                
                if (_config.Mesh?.Nodes != null)
                {
                    foreach (var node in _config.Mesh.Nodes)
                    {
                        _meshNodes[node.Id] = node;
                    }
                }
                
                _logger.LogInformation("WiFi: Configuration chargée");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur chargement config");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur sauvegarde config");
        }
    }

    // ==========================================
    // INSTALLATION AUTOMATIQUE
    // ==========================================

    public async Task<WiFiInstallationResult> InstallHostapdAsync()
    {
        var result = new WiFiInstallationResult();
        var logs = new List<string>();

        if (!IsLinux)
        {
            result.Success = false;
            result.Message = "L'installation automatique n'est supportée que sur Linux";
            return result;
        }

        try
        {
            // Étape 1: Vérifier si hostapd est déjà installé
            logs.Add("?? Vérification de hostapd...");
            var hostapdCheck = await ExecuteCommandAsync("which", "hostapd");
            
            if (!string.IsNullOrWhiteSpace(hostapdCheck))
            {
                logs.Add("? hostapd est déjà installé");
                result.Steps.Add("hostapd déjà installé");
            }
            else
            {
                // Installer hostapd
                logs.Add("?? Mise à jour des paquets...");
                result.Steps.Add("Mise à jour apt");
                var updateResult = await ExecuteCommandAsync("apt-get", "update -y");
                logs.Add(updateResult);

                logs.Add("?? Installation de hostapd et dnsmasq...");
                result.Steps.Add("Installation hostapd + dnsmasq");
                var installResult = await ExecuteCommandAsync("apt-get", "install -y hostapd dnsmasq");
                logs.Add(installResult);

                // Vérifier l'installation
                hostapdCheck = await ExecuteCommandAsync("which", "hostapd");
                if (string.IsNullOrWhiteSpace(hostapdCheck))
                {
                    result.Success = false;
                    result.Message = "Échec de l'installation de hostapd";
                    result.Errors.Add("hostapd n'a pas été installé correctement");
                    result.Logs = string.Join("\n", logs);
                    return result;
                }
                logs.Add("? hostapd installé avec succès");
            }

            // Étape 2: Démasquer et activer le service hostapd
            logs.Add("?? Configuration du service hostapd...");
            result.Steps.Add("Démasquage du service");
            await ExecuteCommandAsync("systemctl", "unmask hostapd");
            
            logs.Add("?? Activation du démarrage automatique...");
            result.Steps.Add("Activation du service");
            await ExecuteCommandAsync("systemctl", "enable hostapd");
            await ExecuteCommandAsync("systemctl", "enable dnsmasq");
            logs.Add("? Services activés");

            // Étape 3: Configurer le routage IP
            logs.Add("?? Configuration du routage IP...");
            result.Steps.Add("Activation IP forwarding");
            await ExecuteCommandAsync("sysctl", "-w net.ipv4.ip_forward=1");
            
            // Rendre permanent
            var sysctlContent = await File.ReadAllTextAsync("/etc/sysctl.conf").ConfigureAwait(false);
            if (!sysctlContent.Contains("net.ipv4.ip_forward=1"))
            {
                await File.AppendAllTextAsync("/etc/sysctl.conf", "\nnet.ipv4.ip_forward=1\n");
                logs.Add("? IP forwarding activé de manière permanente");
            }

            // Étape 4: Vérifier l'interface sans fil
            logs.Add("?? Détection de l'interface sans fil...");
            result.Steps.Add("Détection interface WiFi");
            var interfaces = await GetWirelessInterfacesAsync();
            
            if (!interfaces.Any())
            {
                logs.Add("?? Aucune interface sans fil détectée");
                result.Errors.Add("Aucune interface sans fil trouvée. Vérifiez votre carte WiFi.");
            }
            else
            {
                var wifiInterface = interfaces.First();
                logs.Add($"? Interface trouvée: {wifiInterface}");
                result.Steps.Add($"Interface détectée: {wifiInterface}");
            }

            result.Success = true;
            result.Message = "Installation terminée avec succès. Vous pouvez maintenant configurer le point d'accès.";
            result.Logs = string.Join("\n", logs);

            _logger.LogInformation("WiFi: Installation automatique terminée avec succès");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Erreur lors de l'installation: {ex.Message}";
            result.Errors.Add(ex.Message);
            result.Logs = string.Join("\n", logs);
            _logger.LogError(ex, "WiFi: Erreur installation automatique");
        }

        return result;
    }

    public async Task<WiFiInstallationResult> ConfigureAccessPointAsync()
    {
        var result = new WiFiInstallationResult();
        var logs = new List<string>();

        if (!IsLinux)
        {
            result.Success = false;
            result.Message = "La configuration automatique n'est supportée que sur Linux";
            return result;
        }

        try
        {
            // Vérifier que hostapd est installé
            var hostapdCheck = await ExecuteCommandAsync("which", "hostapd");
            if (string.IsNullOrWhiteSpace(hostapdCheck))
            {
                result.Success = false;
                result.Message = "hostapd n'est pas installé. Cliquez d'abord sur 'Installer les prérequis'.";
                return result;
            }

            // Trouver l'interface sans fil
            var interfaces = await GetWirelessInterfacesAsync();
            if (!interfaces.Any())
            {
                result.Success = false;
                result.Message = "Aucune interface sans fil détectée";
                return result;
            }

            var wifiInterface = interfaces.First();
            logs.Add($"?? Configuration de l'interface {wifiInterface}...");
            result.Steps.Add($"Interface: {wifiInterface}");

            // Étape 1: Configurer l'adresse IP statique pour l'interface WiFi
            logs.Add("?? Configuration de l'adresse IP statique...");
            result.Steps.Add("Configuration IP statique");
            
            var dhcpcdConfig = $@"
# Configuration du point d'accès WiFi - WebGuard
interface {wifiInterface}
    static ip_address=192.168.4.1/24
    nohook wpa_supplicant
";
            await AppendToFileIfNotExistsAsync("/etc/dhcpcd.conf", dhcpcdConfig, "192.168.4.1");
            logs.Add("? IP statique configurée: 192.168.4.1");

            // Étape 2: Configurer dnsmasq pour le DHCP
            logs.Add("?? Configuration du serveur DHCP (dnsmasq)...");
            result.Steps.Add("Configuration DHCP");
            
            var dnsmasqConfig = $@"# Configuration du point d'accès WiFi - WebGuard
interface={wifiInterface}
dhcp-range=192.168.4.2,192.168.4.50,255.255.255.0,24h
address=/gw/192.168.4.1
";
            // Sauvegarder et remplacer la config dnsmasq
            if (File.Exists("/etc/dnsmasq.conf"))
            {
                File.Copy("/etc/dnsmasq.conf", "/etc/dnsmasq.conf.backup", true);
            }
            await File.WriteAllTextAsync("/etc/dnsmasq.conf", dnsmasqConfig);
            logs.Add("? DHCP configuré (192.168.4.2 - 192.168.4.50)");

            // Étape 3: Configurer hostapd
            logs.Add("?? Génération de la configuration hostapd...");
            result.Steps.Add("Configuration hostapd");
            await GenerateHostapdConfigAsync();
            await ConfigureHostapdDefaultAsync();
            logs.Add($"? SSID: {_config.GlobalSSID}");

            // Étape 4: Configurer le NAT (partage Internet)
            logs.Add("?? Configuration du NAT...");
            result.Steps.Add("Configuration NAT");
            
            // Trouver l'interface WAN (eth0 ou similaire)
            var wanInterface = await FindWanInterfaceAsync();
            if (!string.IsNullOrEmpty(wanInterface))
            {
                // Configurer iptables
                await ExecuteCommandAsync("iptables", $"-t nat -A POSTROUTING -o {wanInterface} -j MASQUERADE");
                await ExecuteCommandAsync("iptables", $"-A FORWARD -i {wanInterface} -o {wifiInterface} -m state --state RELATED,ESTABLISHED -j ACCEPT");
                await ExecuteCommandAsync("iptables", $"-A FORWARD -i {wifiInterface} -o {wanInterface} -j ACCEPT");
                
                // Sauvegarder les règles
                await ExecuteCommandAsync("sh", "-c 'iptables-save > /etc/iptables.ipv4.nat'");
                logs.Add($"? NAT configuré: {wifiInterface} -> {wanInterface}");
            }
            else
            {
                logs.Add("?? Interface WAN non trouvée. Le NAT devra être configuré manuellement.");
            }

            // Étape 5: Arrêter wpa_supplicant et démarrer les services
            logs.Add("?? Démarrage des services...");
            result.Steps.Add("Démarrage des services");
            
            await ExecuteCommandAsync("systemctl", "stop wpa_supplicant");
            await ExecuteCommandAsync("systemctl", "restart dhcpcd");
            await Task.Delay(1000);
            await ExecuteCommandAsync("systemctl", "restart dnsmasq");
            await Task.Delay(1000);
            await ExecuteCommandAsync("systemctl", "restart hostapd");
            await Task.Delay(2000);

            // Vérifier que hostapd est bien démarré
            var serviceStatus = await ExecuteCommandAsync("systemctl", "is-active hostapd");
            if (serviceStatus.Trim() == "active")
            {
                logs.Add("? Point d'accès WiFi démarré avec succès!");
                result.Success = true;
                result.Message = $"Point d'accès configuré et actif!\n\nSSID: {_config.GlobalSSID}\nIP: 192.168.4.1\nPlage DHCP: 192.168.4.2-50";
                
                _config.Enabled = true;
                SaveConfig();
            }
            else
            {
                // Récupérer les logs d'erreur
                var errorLogs = await ExecuteCommandAsync("journalctl", "-u hostapd -n 30 --no-pager");
                logs.Add("? Échec du démarrage de hostapd:");
                logs.Add(errorLogs);
                result.Success = false;
                result.Message = "Échec du démarrage du point d'accès. Consultez les logs pour plus de détails.";
                result.Errors.Add(errorLogs);
            }

            result.Logs = string.Join("\n", logs);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Erreur lors de la configuration: {ex.Message}";
            result.Errors.Add(ex.Message);
            result.Logs = string.Join("\n", logs);
            _logger.LogError(ex, "WiFi: Erreur configuration automatique");
        }

        return result;
    }

    private async Task AppendToFileIfNotExistsAsync(string path, string content, string checkString)
    {
        try
        {
            var existingContent = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
            if (!existingContent.Contains(checkString))
            {
                await File.AppendAllTextAsync(path, content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi: Erreur écriture fichier {Path}", path);
        }
    }

    private async Task<string?> FindWanInterfaceAsync()
    {
        try
        {
            // Chercher l'interface avec une route par défaut
            var routeOutput = await ExecuteCommandAsync("ip", "route show default");
            if (!string.IsNullOrEmpty(routeOutput))
            {
                // Format: "default via X.X.X.X dev eth0 ..."
                var parts = routeOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var devIndex = Array.IndexOf(parts, "dev");
                if (devIndex >= 0 && devIndex + 1 < parts.Length)
                {
                    var wanIface = parts[devIndex + 1];
                    // Ne pas retourner l'interface WiFi
                    var wifiInterfaces = await GetWirelessInterfacesAsync();
                    if (!wifiInterfaces.Contains(wanIface))
                    {
                        return wanIface;
                    }
                }
            }

            // Fallback: chercher eth0, enp*, ens*
            var interfaces = Directory.GetDirectories("/sys/class/net")
                .Select(Path.GetFileName)
                .Where(n => n != null && (n.StartsWith("eth") || n.StartsWith("enp") || n.StartsWith("ens")))
                .FirstOrDefault();

            return interfaces;
        }
        catch
        {
            return "eth0"; // Fallback
        }
    }

    private static async Task FileCopyAsync(string source, string dest, bool overwrite)
    {
        if (File.Exists(source))
        {
            var content = await File.ReadAllBytesAsync(source);
            await File.WriteAllBytesAsync(dest, content);
        }
    }
}
