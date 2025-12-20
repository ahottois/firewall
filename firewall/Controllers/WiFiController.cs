using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WiFiController : ControllerBase
{
    private readonly IWiFiService _wifiService;
    private readonly ILogger<WiFiController> _logger;

    public WiFiController(IWiFiService wifiService, ILogger<WiFiController> logger)
    {
        _wifiService = wifiService;
        _logger = logger;
    }

    // ==========================================
    // STATUT ET GESTION DU SERVICE
    // ==========================================

    /// <summary>
    /// Obtenir le statut détaillé du service WiFi
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<WiFiServiceStatus>> GetServiceStatus()
    {
        var status = await _wifiService.GetServiceStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// Démarrer le point d'accès WiFi
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult> StartAccessPoint()
    {
        try
        {
            var success = await _wifiService.StartAccessPointAsync();
            if (success)
            {
                return Ok(new { message = "Point d'accès WiFi démarré avec succès" });
            }
            else
            {
                var status = await _wifiService.GetServiceStatusAsync();
                return BadRequest(new { 
                    message = "Impossible de démarrer le point d'accès WiFi",
                    steps = status.SetupSteps,
                    error = status.ErrorMessage
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur démarrage point d'accès WiFi");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Arrêter le point d'accès WiFi
    /// </summary>
    [HttpPost("stop")]
    public async Task<ActionResult> StopAccessPoint()
    {
        try
        {
            var success = await _wifiService.StopAccessPointAsync();
            if (success)
            {
                return Ok(new { message = "Point d'accès WiFi arrêté" });
            }
            return BadRequest(new { message = "Impossible d'arrêter le point d'accès WiFi" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur arrêt point d'accès WiFi");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Obtenir les instructions de configuration complètes
    /// </summary>
    [HttpGet("setup-instructions")]
    public async Task<ActionResult> GetSetupInstructions()
    {
        var instructions = await _wifiService.GetSetupInstructionsAsync();
        return Ok(new { instructions });
    }

    /// <summary>
    /// Installer automatiquement hostapd et les dépendances
    /// </summary>
    [HttpPost("install")]
    public async Task<ActionResult<WiFiInstallationResult>> InstallHostapd()
    {
        try
        {
            _logger.LogInformation("WiFi: Démarrage de l'installation automatique");
            var result = await _wifiService.InstallHostapdAsync();
            
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur installation WiFi");
            return BadRequest(new WiFiInstallationResult 
            { 
                Success = false, 
                Message = ex.Message,
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// Configurer automatiquement le point d'accès WiFi
    /// </summary>
    [HttpPost("configure")]
    public async Task<ActionResult<WiFiInstallationResult>> ConfigureAccessPoint()
    {
        try
        {
            _logger.LogInformation("WiFi: Démarrage de la configuration automatique");
            var result = await _wifiService.ConfigureAccessPointAsync();
            
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur configuration WiFi");
            return BadRequest(new WiFiInstallationResult 
            { 
                Success = false, 
                Message = ex.Message,
                Errors = new List<string> { ex.Message }
            });
        }
    }

    // ==========================================
    // CONFIGURATION GLOBALE
    // ==========================================

    /// <summary>
    /// Obtenir la configuration WiFi complète
    /// </summary>
    [HttpGet("config")]
    public ActionResult<WiFiConfig> GetConfig()
    {
        var config = _wifiService.GetConfig();
        
        // Ne pas retourner les mots de passe en clair
        var safeConfig = new
        {
            config.GlobalSSID,
            GlobalPassword = !string.IsNullOrEmpty(config.GlobalPassword) ? "********" : "",
            config.GlobalSecurity,
            config.Enabled,
            config.HideSSID,
            config.ClientIsolation,
            config.GuestNetworkEnabled,
            config.GuestSSID,
            GuestPassword = !string.IsNullOrEmpty(config.GuestPassword) ? "********" : "",
            config.GuestBandwidthLimit,
            config.GuestIsolation,
            config.SmartConnect,
            config.BandSteeringThreshold,
            config.FastRoaming,
            config.NeighborReport,
            config.BSSTransition,
            config.Schedule,
            Bands = config.Bands.Select(b => new
            {
                b.Id,
                b.Band,
                b.Enabled,
                b.SSID,
                b.UseGlobalSSID,
                Password = !string.IsNullOrEmpty(b.Password) ? "********" : "",
                b.Security,
                b.Channel,
                b.ChannelWidth,
                b.TxPower,
                b.MaxStandard,
                b.BandSteering,
                b.Beamforming,
                b.MuMimo,
                b.OFDMA,
                b.TWT,
                b.Mode160MHz,
                b.MaxSpeed,
                b.ConnectedClients,
                b.ChannelUtilization,
                b.InterferenceLevel
            }),
            MeshEnabled = config.Mesh?.Enabled ?? false
        };

        return Ok(safeConfig);
    }

    /// <summary>
    /// Mettre à jour la configuration WiFi globale
    /// </summary>
    [HttpPost("config")]
    public async Task<ActionResult> UpdateConfig([FromBody] WiFiConfigDto dto)
    {
        try
        {
            await _wifiService.UpdateConfigAsync(dto);
            return Ok(new { message = "Configuration WiFi mise à jour" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur mise à jour config WiFi");
            return BadRequest(new { message = ex.Message });
        }
    }

    // ==========================================
    // CONFIGURATION PAR BANDE
    // ==========================================

    /// <summary>
    /// Obtenir la configuration d'une bande spécifique
    /// </summary>
    [HttpGet("bands/{band}")]
    public async Task<ActionResult<WiFiBandConfig>> GetBandConfig(WiFiBand band)
    {
        try
        {
            var config = await _wifiService.GetBandConfigAsync(band);
            return Ok(config);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Bande {band} non trouvée" });
        }
    }

    /// <summary>
    /// Mettre à jour la configuration d'une bande
    /// </summary>
    [HttpPut("bands/{band}")]
    public async Task<ActionResult> UpdateBandConfig(WiFiBand band, [FromBody] WiFiBandConfigDto dto)
    {
        try
        {
            dto.Band = band;
            await _wifiService.UpdateBandConfigAsync(band, dto);
            return Ok(new { message = $"Configuration de la bande {band} mise à jour" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Bande {band} non trouvée" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Obtenir les informations sur les bandes disponibles
    /// </summary>
    [HttpGet("bands/info")]
    public ActionResult GetBandsInfo()
    {
        var info = new List<object>
        {
            new
            {
                Band = WiFiBand.Band_2_4GHz,
                Name = "2.4 GHz",
                Description = "Portée étendue, idéal pour IoT et appareils anciens",
                MaxSpeed = "574 Mbps (WiFi 6)",
                Channels = new[] { 1, 6, 11 },
                Interference = "Élevée (micro-ondes, Bluetooth, voisins)",
                Range = "Long",
                Penetration = "Bonne",
                SupportedStandards = new[] { "WiFi 4 (N)", "WiFi 6 (AX)", "WiFi 7 (BE)" }
            },
            new
            {
                Band = WiFiBand.Band_5GHz,
                Name = "5 GHz",
                Description = "Haut débit, moins d'interférences",
                MaxSpeed = "4.8 Gbps (WiFi 6 160MHz)",
                Channels = new[] { 36, 40, 44, 48, 149, 153, 157, 161, 165 },
                Interference = "Faible",
                Range = "Moyen",
                Penetration = "Moyenne",
                SupportedStandards = new[] { "WiFi 4 (N)", "WiFi 5 (AC)", "WiFi 6 (AX)", "WiFi 7 (BE)" }
            },
            new
            {
                Band = WiFiBand.Band_6GHz,
                Name = "6 GHz",
                Description = "Très haut débit, réservé WiFi 6E/7",
                MaxSpeed = "9.6 Gbps (WiFi 6E 160MHz)",
                Channels = new[] { 1, 5, 9, 13, 17, 21, 25, 29 },
                Interference = "Très faible",
                Range = "Court",
                Penetration = "Limitée",
                SupportedStandards = new[] { "WiFi 6E (AX)", "WiFi 7 (BE)" },
                Requirements = "WPA3 obligatoire"
            }
        };

        return Ok(info);
    }

    /// <summary>
    /// Obtenir les options de sécurité disponibles
    /// </summary>
    [HttpGet("security/options")]
    public ActionResult GetSecurityOptions()
    {
        var options = new[]
        {
            new { Value = WiFiSecurity.Open, Name = "Ouvert (non sécurisé)", Recommended = false, Note = "Aucun chiffrement" },
            new { Value = WiFiSecurity.WPA2_Personal, Name = "WPA2-Personal", Recommended = false, Note = "Compatibilité maximale" },
            new { Value = WiFiSecurity.WPA3_Personal, Name = "WPA3-Personal", Recommended = true, Note = "Sécurité optimale" },
            new { Value = WiFiSecurity.WPA2_WPA3_Mixed, Name = "WPA2/WPA3 Mixte", Recommended = true, Note = "Équilibre sécurité/compatibilité" },
            new { Value = WiFiSecurity.WPA2_Enterprise, Name = "WPA2-Enterprise", Recommended = false, Note = "Nécessite serveur RADIUS" },
            new { Value = WiFiSecurity.WPA3_Enterprise, Name = "WPA3-Enterprise", Recommended = false, Note = "Nécessite serveur RADIUS" }
        };

        return Ok(options);
    }

    // ==========================================
    // CLIENTS
    // ==========================================

    /// <summary>
    /// Obtenir la liste des clients WiFi connectés
    /// </summary>
    [HttpGet("clients")]
    public async Task<ActionResult<IEnumerable<WiFiClient>>> GetClients()
    {
        var clients = await _wifiService.GetClientsAsync();
        return Ok(clients);
    }

    /// <summary>
    /// Obtenir les statistiques WiFi
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<WiFiStats>> GetStats()
    {
        var stats = await _wifiService.GetStatsAsync();
        return Ok(stats);
    }

    // ==========================================
    // ANALYSE DES CANAUX
    // ==========================================

    /// <summary>
    /// Scanner les canaux d'une bande
    /// </summary>
    [HttpGet("channels/scan/{band}")]
    public async Task<ActionResult<IEnumerable<ChannelAnalysis>>> ScanChannels(WiFiBand band)
    {
        var analysis = await _wifiService.ScanChannelsAsync(band);
        return Ok(analysis);
    }

    /// <summary>
    /// Obtenir le canal recommandé pour une bande
    /// </summary>
    [HttpGet("channels/recommended/{band}")]
    public async Task<ActionResult> GetRecommendedChannel(WiFiBand band)
    {
        var channel = await _wifiService.GetRecommendedChannelAsync(band);
        return Ok(new { band, recommendedChannel = channel });
    }

    /// <summary>
    /// Obtenir la liste des canaux disponibles par bande
    /// </summary>
    [HttpGet("channels/available")]
    public ActionResult GetAvailableChannels()
    {
        var channels = new
        {
            Band_2_4GHz = new[]
            {
                new { Channel = 1, Frequency = 2412, Overlapping = false },
                new { Channel = 2, Frequency = 2417, Overlapping = true },
                new { Channel = 3, Frequency = 2422, Overlapping = true },
                new { Channel = 4, Frequency = 2427, Overlapping = true },
                new { Channel = 5, Frequency = 2432, Overlapping = true },
                new { Channel = 6, Frequency = 2437, Overlapping = false },
                new { Channel = 7, Frequency = 2442, Overlapping = true },
                new { Channel = 8, Frequency = 2447, Overlapping = true },
                new { Channel = 9, Frequency = 2452, Overlapping = true },
                new { Channel = 10, Frequency = 2457, Overlapping = true },
                new { Channel = 11, Frequency = 2462, Overlapping = false }
            },
            Band_5GHz = new[]
            {
                // UNII-1 (Indoor)
                new { Channel = 36, Frequency = 5180, DFS = false, Power = "High" },
                new { Channel = 40, Frequency = 5200, DFS = false, Power = "High" },
                new { Channel = 44, Frequency = 5220, DFS = false, Power = "High" },
                new { Channel = 48, Frequency = 5240, DFS = false, Power = "High" },
                // UNII-2A (DFS)
                new { Channel = 52, Frequency = 5260, DFS = true, Power = "Medium" },
                new { Channel = 56, Frequency = 5280, DFS = true, Power = "Medium" },
                new { Channel = 60, Frequency = 5300, DFS = true, Power = "Medium" },
                new { Channel = 64, Frequency = 5320, DFS = true, Power = "Medium" },
                // UNII-2C (DFS)
                new { Channel = 100, Frequency = 5500, DFS = true, Power = "Medium" },
                new { Channel = 104, Frequency = 5520, DFS = true, Power = "Medium" },
                new { Channel = 108, Frequency = 5540, DFS = true, Power = "Medium" },
                new { Channel = 112, Frequency = 5560, DFS = true, Power = "Medium" },
                new { Channel = 116, Frequency = 5580, DFS = true, Power = "Medium" },
                new { Channel = 120, Frequency = 5600, DFS = true, Power = "Medium" },
                new { Channel = 124, Frequency = 5620, DFS = true, Power = "Medium" },
                new { Channel = 128, Frequency = 5640, DFS = true, Power = "Medium" },
                new { Channel = 132, Frequency = 5660, DFS = true, Power = "Medium" },
                new { Channel = 136, Frequency = 5680, DFS = true, Power = "Medium" },
                new { Channel = 140, Frequency = 5700, DFS = true, Power = "Medium" },
                new { Channel = 144, Frequency = 5720, DFS = true, Power = "Medium" },
                // UNII-3
                new { Channel = 149, Frequency = 5745, DFS = false, Power = "High" },
                new { Channel = 153, Frequency = 5765, DFS = false, Power = "High" },
                new { Channel = 157, Frequency = 5785, DFS = false, Power = "High" },
                new { Channel = 161, Frequency = 5805, DFS = false, Power = "High" },
                new { Channel = 165, Frequency = 5825, DFS = false, Power = "High" }
            },
            Band_6GHz = Enumerable.Range(1, 59).Select(i => new
            {
                Channel = i * 4 + 1,
                Frequency = 5950 + (i * 20),
                Note = "WiFi 6E/7 uniquement"
            }).ToArray()
        };

        return Ok(channels);
    }

    // ==========================================
    // MESH
    // ==========================================

    /// <summary>
    /// Obtenir la configuration Mesh
    /// </summary>
    [HttpGet("mesh/config")]
    public ActionResult GetMeshConfig()
    {
        var config = _wifiService.GetMeshConfig();
        if (config == null)
        {
            return Ok(new MeshConfig { Enabled = false });
        }

        // Ne pas retourner la clé Mesh
        return Ok(new
        {
            config.Enabled,
            config.MeshId,
            MeshKey = !string.IsNullOrEmpty(config.MeshKey) ? "********" : "",
            config.Role,
            config.PreferredBackhaul,
            config.DedicatedBackhaulBand,
            config.AllowDaisyChain,
            config.MaxHops,
            config.AutoOptimization
        });
    }

    /// <summary>
    /// Mettre à jour la configuration Mesh
    /// </summary>
    [HttpPost("mesh/config")]
    public async Task<ActionResult> UpdateMeshConfig([FromBody] MeshConfig config)
    {
        try
        {
            await _wifiService.UpdateMeshConfigAsync(config);
            return Ok(new { message = "Configuration Mesh mise à jour" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Obtenir la liste des nœuds Mesh
    /// </summary>
    [HttpGet("mesh/nodes")]
    public async Task<ActionResult<IEnumerable<MeshNode>>> GetMeshNodes()
    {
        var nodes = await _wifiService.GetMeshNodesAsync();
        return Ok(nodes);
    }

    /// <summary>
    /// Ajouter un nœud Mesh
    /// </summary>
    [HttpPost("mesh/nodes")]
    public async Task<ActionResult<MeshNode>> AddMeshNode([FromBody] AddMeshNodeRequest request)
    {
        var node = await _wifiService.AddMeshNodeAsync(request);
        if (node == null)
        {
            return BadRequest(new { message = "Impossible d'ajouter le nœud" });
        }
        return CreatedAtAction(nameof(GetMeshNodes), new { id = node.Id }, node);
    }

    /// <summary>
    /// Supprimer un nœud Mesh
    /// </summary>
    [HttpDelete("mesh/nodes/{nodeId}")]
    public async Task<ActionResult> RemoveMeshNode(string nodeId)
    {
        await _wifiService.RemoveMeshNodeAsync(nodeId);
        return Ok(new { message = "Nœud supprimé" });
    }

    /// <summary>
    /// Optimiser le réseau Mesh
    /// </summary>
    [HttpPost("mesh/optimize")]
    public async Task<ActionResult> OptimizeMesh()
    {
        await _wifiService.OptimizeMeshAsync();
        return Ok(new { message = "Optimisation Mesh lancée" });
    }

    /// <summary>
    /// Obtenir la topologie Mesh
    /// </summary>
    [HttpGet("mesh/topology")]
    public async Task<ActionResult> GetMeshTopology()
    {
        var nodes = await _wifiService.GetMeshNodesAsync();
        var nodeList = nodes.ToList();

        var topology = new
        {
            Controller = nodeList.FirstOrDefault(n => n.Role == MeshNodeRole.Controller),
            Agents = nodeList.Where(n => n.Role == MeshNodeRole.Agent).Select(n => new
            {
                n.Id,
                n.Name,
                n.Location,
                n.Status,
                n.BackhaulType,
                n.BackhaulSpeed,
                n.BackhaulQuality,
                n.ParentNodeId,
                n.HopCount,
                n.ConnectedClients
            }),
            Links = nodeList
                .Where(n => n.ParentNodeId != null)
                .Select(n => new
                {
                    From = n.ParentNodeId,
                    To = n.Id,
                    Quality = n.BackhaulQuality,
                    Speed = n.BackhaulSpeed,
                    Type = n.BackhaulType
                })
        };

        return Ok(topology);
    }

    // ==========================================
    // ACTIONS
    // ==========================================

    /// <summary>
    /// Redémarrer le WiFi
    /// </summary>
    [HttpPost("restart")]
    public async Task<ActionResult> RestartWiFi()
    {
        await _wifiService.RestartWiFiAsync();
        return Ok(new { message = "WiFi redémarré" });
    }

    /// <summary>
    /// Tester la connexion Internet
    /// </summary>
    [HttpGet("test")]
    public async Task<ActionResult> TestConnection()
    {
        var success = await _wifiService.TestConnectionAsync();
        return Ok(new { connected = success });
    }

    /// <summary>
    /// Obtenir le résumé du système WiFi
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary()
    {
        var config = _wifiService.GetConfig();
        var stats = await _wifiService.GetStatsAsync();
        var meshConfig = _wifiService.GetMeshConfig();
        var status = await _wifiService.GetServiceStatusAsync();

        return Ok(new
        {
            Enabled = config.Enabled,
            Running = status.IsHostapdRunning,
            SSID = config.GlobalSSID,
            Security = config.GlobalSecurity.ToString(),
            SmartConnect = config.SmartConnect,
            GuestEnabled = config.GuestNetworkEnabled,
            WirelessInterface = status.WirelessInterface,
            HasWirelessInterface = status.HasWirelessInterface,
            IsHostapdInstalled = status.IsHostapdInstalled,
            SetupRequired = status.SetupSteps.Any(),
            SetupSteps = status.SetupSteps,
            Bands = config.Bands.Select(b => new
            {
                b.Band,
                b.Enabled,
                Standard = b.MaxStandard.ToString(),
                b.Channel,
                Width = b.ChannelWidth.ToString(),
                b.MaxSpeed,
                b.ConnectedClients
            }),
            Stats = new
            {
                stats.TotalClients,
                stats.Clients2_4GHz,
                stats.Clients5GHz,
                stats.Clients6GHz,
                TxMB = stats.TotalTxBytes / 1024 / 1024,
                RxMB = stats.TotalRxBytes / 1024 / 1024
            },
            Mesh = meshConfig != null ? new
            {
                meshConfig.Enabled,
                Nodes = stats.MeshNodes,
                Online = stats.MeshNodesOnline
            } : null
        });
    }
}
