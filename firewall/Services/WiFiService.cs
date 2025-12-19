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

public class WiFiService : IWiFiService
{
    private readonly ILogger<WiFiService> _logger;
    private WiFiConfig _config = new();
    private readonly ConcurrentDictionary<string, WiFiClient> _clients = new();
    private readonly ConcurrentDictionary<string, MeshNode> _meshNodes = new();
    
    private const string ConfigPath = "wifi_config.json";
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public WiFiService(ILogger<WiFiService> logger)
    {
        _logger = logger;
        LoadConfig();
        InitializeDefaultBands();
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
        
        _logger.LogInformation("WiFi: Configuration mise à jour");
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

        // Valider la largeur de canal selon la bande
        ValidateChannelWidth(config);
        
        // Calculer le débit max théorique
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
                // 2.4 GHz: max 40 MHz
                if (config.ChannelWidth > ChannelWidth.Width_40MHz)
                    config.ChannelWidth = ChannelWidth.Width_40MHz;
                break;
                
            case WiFiBand.Band_5GHz:
                // 5 GHz: max 160 MHz (avec DFS)
                if (config.ChannelWidth > ChannelWidth.Width_160MHz)
                    config.ChannelWidth = ChannelWidth.Width_160MHz;
                break;
                
            case WiFiBand.Band_6GHz:
                // 6 GHz: jusqu'à 320 MHz (WiFi 7)
                if (config.MaxStandard != WiFiStandard.WiFi7_BE && 
                    config.ChannelWidth > ChannelWidth.Width_160MHz)
                    config.ChannelWidth = ChannelWidth.Width_160MHz;
                break;
        }
    }

    private int CalculateMaxSpeed(WiFiBandConfig config)
    {
        // Calcul simplifié basé sur WiFi 6 2x2 MIMO
        int baseSpeed = config.ChannelWidth switch
        {
            ChannelWidth.Width_20MHz => 287,
            ChannelWidth.Width_40MHz => 574,
            ChannelWidth.Width_80MHz => 1201,
            ChannelWidth.Width_160MHz => 2402,
            ChannelWidth.Width_320MHz => 4804,
            _ => 574
        };

        // Ajustement selon le standard
        double multiplier = config.MaxStandard switch
        {
            WiFiStandard.WiFi4_N => 0.25,
            WiFiStandard.WiFi5_AC => 0.75,
            WiFiStandard.WiFi6_AX => 1.0,
            WiFiStandard.WiFi6E_AX => 1.0,
            WiFiStandard.WiFi7_BE => 1.4, // WiFi 7 avec 4K-QAM
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

            // Parser les clients (format hostapd)
            // Ceci est une implémentation simplifiée
            var lines = result.Split('\n');
            string? currentMac = null;
            
            foreach (var line in lines)
            {
                if (line.Contains(':') && line.Length == 17) // MAC address
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

        // Grouper par standard
        stats.ClientsByStandard = clientList
            .GroupBy(c => c.Standard)
            .ToDictionary(g => g.Key, g => g.Count());

        // Grouper par canal
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

            // Scanner les réseaux sur ce canal
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
                // Simulation pour Windows/dev
                var random = new Random(channel);
                analysis.NetworkCount = random.Next(0, 8);
                analysis.Utilization = random.Next(5, 70);
                analysis.InterferenceScore = random.Next(0, 100);
            }

            analyses.Add(analysis);
        }

        // Marquer les canaux recommandés
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
            WiFiBand.Band_6GHz => Enumerable.Range(1, 59).Select(i => i * 4 + 1).ToList(), // 1, 5, 9, ... 233
            _ => new List<int>()
        };
    }

    private bool IsDFSChannel(WiFiBand band, int channel)
    {
        if (band != WiFiBand.Band_5GHz) return false;
        // Canaux DFS en Europe: 52-64, 100-144
        return (channel >= 52 && channel <= 64) || (channel >= 100 && channel <= 144);
    }

    private async Task<List<WiFiNetwork>> ScanNetworksOnChannelAsync(WiFiBand band, int channel)
    {
        var networks = new List<WiFiNetwork>();
        
        try
        {
            // Utiliser iw pour scanner
            var result = await ExecuteCommandAsync("iw", $"dev wlan0 scan freq {ChannelToFrequency(band, channel)}");
            // Parser les résultats (implémentation simplifiée)
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
        score += networks.Count(n => n.RSSI > -50) * 20; // Réseaux forts = plus d'interférence
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
            // Générer une clé Mesh si non définie
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
        // Mettre à jour le statut des nœuds
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

        // Tenter de connecter le nœud
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

            // Déconnecter le nœud
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
            // Évaluer la qualité du backhaul
            if (node.BackhaulQuality < 50 && node.BackhaulType == MeshBackhaul.WiFi)
            {
                // Suggérer un meilleur parent ou passer en Ethernet si disponible
                var betterParent = FindBetterParentNode(node);
                if (betterParent != null)
                {
                    _logger.LogInformation("WiFi Mesh: Réaffectation de {Node} vers {Parent}", 
                        node.Name, betterParent.Name);
                    node.ParentNodeId = betterParent.Id;
                }
            }
        }

        // Optimiser la sélection des canaux backhaul
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
            // Configurer 802.11s mesh
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
        // Implémentation de la connexion d'un nœud mesh
        // En production, cela utiliserait WPS ou un protocole propriétaire
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
            await ExecuteCommandAsync("systemctl", "reload hostapd");
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
        // Générer la configuration hostapd
        var mainBand = _config.Bands.FirstOrDefault(b => b.Enabled && b.Band == WiFiBand.Band_5GHz)
                    ?? _config.Bands.FirstOrDefault(b => b.Enabled);

        if (mainBand == null) return;

        var config = $@"interface=wlan0
driver=nl80211
ssid={mainBand.SSID}
hw_mode={(mainBand.Band == WiFiBand.Band_2_4GHz ? "g" : "a")}
channel={(mainBand.Channel == 0 ? "acs_survey" : mainBand.Channel.ToString())}
wmm_enabled=1
macaddr_acl=0
auth_algs=1
ignore_broadcast_ssid={((_config.HideSSID ? 1 : 0))}
wpa=2
wpa_passphrase={mainBand.Password}
wpa_key_mgmt={(mainBand.Security >= WiFiSecurity.WPA3_Personal ? "SAE" : "WPA-PSK")}
wpa_pairwise=CCMP
rsn_pairwise=CCMP

# WiFi 6 (802.11ax)
ieee80211ax={(mainBand.MaxStandard >= WiFiStandard.WiFi6_AX ? 1 : 0)}
he_su_beamformer={(mainBand.Beamforming ? 1 : 0)}
he_mu_beamformer={(mainBand.MuMimo ? 1 : 0)}

# Fast Roaming
ieee80211r={(_config.FastRoaming ? 1 : 0)}
ft_over_ds=0
mobility_domain=a1b2
";

        await File.WriteAllTextAsync("/etc/hostapd/hostapd.conf", config);
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
            await process.WaitForExitAsync();

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
                
                // Charger les nœuds Mesh
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
}
