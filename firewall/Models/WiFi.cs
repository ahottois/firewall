using System.Text.Json.Serialization;

namespace NetworkFirewall.Models;

/// <summary>
/// Standards WiFi supportés
/// </summary>
public enum WiFiStandard
{
    /// <summary>WiFi 4 - 802.11n (2.4 GHz / 5 GHz)</summary>
    WiFi4_N = 4,
    
    /// <summary>WiFi 5 - 802.11ac (5 GHz)</summary>
    WiFi5_AC = 5,
    
    /// <summary>WiFi 6 - 802.11ax (2.4 GHz / 5 GHz)</summary>
    WiFi6_AX = 6,
    
    /// <summary>WiFi 6E - 802.11ax (6 GHz)</summary>
    WiFi6E_AX = 61, // 6E
    
    /// <summary>WiFi 7 - 802.11be (2.4 GHz / 5 GHz / 6 GHz)</summary>
    WiFi7_BE = 7
}

/// <summary>
/// Bandes de fréquence WiFi
/// </summary>
public enum WiFiBand
{
    /// <summary>2.4 GHz - Portée étendue, débit moyen</summary>
    Band_2_4GHz = 24,
    
    /// <summary>5 GHz - Portée moyenne, haut débit</summary>
    Band_5GHz = 50,
    
    /// <summary>6 GHz - Portée réduite, très haut débit (WiFi 6E/7)</summary>
    Band_6GHz = 60
}

/// <summary>
/// Largeur de canal WiFi
/// </summary>
public enum ChannelWidth
{
    /// <summary>20 MHz - Standard</summary>
    Width_20MHz = 20,
    
    /// <summary>40 MHz - Double canal</summary>
    Width_40MHz = 40,
    
    /// <summary>80 MHz - WiFi 5/6</summary>
    Width_80MHz = 80,
    
    /// <summary>160 MHz - WiFi 6/6E/7</summary>
    Width_160MHz = 160,
    
    /// <summary>320 MHz - WiFi 7 uniquement</summary>
    Width_320MHz = 320
}

/// <summary>
/// Mode de sécurité WiFi
/// </summary>
public enum WiFiSecurity
{
    Open = 0,
    WEP = 1,
    WPA_Personal = 2,
    WPA2_Personal = 3,
    WPA3_Personal = 4,
    WPA2_Enterprise = 5,
    WPA3_Enterprise = 6,
    WPA2_WPA3_Mixed = 7
}

/// <summary>
/// Configuration d'une bande WiFi
/// </summary>
public class WiFiBandConfig
{
    public int Id { get; set; }
    public WiFiBand Band { get; set; }
    public bool Enabled { get; set; } = true;
    
    /// <summary>SSID du réseau (peut être différent par bande ou unifié)</summary>
    public string SSID { get; set; } = "NetGuard-WiFi";
    
    /// <summary>Utiliser le SSID global (Band Steering)</summary>
    public bool UseGlobalSSID { get; set; } = true;
    
    /// <summary>Mot de passe WiFi</summary>
    public string Password { get; set; } = string.Empty;
    
    /// <summary>Mode de sécurité</summary>
    public WiFiSecurity Security { get; set; } = WiFiSecurity.WPA3_Personal;
    
    /// <summary>Canal (0 = auto)</summary>
    public int Channel { get; set; } = 0;
    
    /// <summary>Largeur de canal</summary>
    public ChannelWidth ChannelWidth { get; set; } = ChannelWidth.Width_80MHz;
    
    /// <summary>Puissance de transmission (en %)</summary>
    public int TxPower { get; set; } = 100;
    
    /// <summary>Standard WiFi maximum supporté</summary>
    public WiFiStandard MaxStandard { get; set; } = WiFiStandard.WiFi6_AX;
    
    /// <summary>Activer le Band Steering (rediriger vers 5/6 GHz)</summary>
    public bool BandSteering { get; set; } = true;
    
    /// <summary>Activer le Beamforming</summary>
    public bool Beamforming { get; set; } = true;
    
    /// <summary>Activer MU-MIMO</summary>
    public bool MuMimo { get; set; } = true;
    
    /// <summary>Activer OFDMA (WiFi 6+)</summary>
    public bool OFDMA { get; set; } = true;
    
    /// <summary>Activer Target Wake Time (économie d'énergie IoT)</summary>
    public bool TWT { get; set; } = true;
    
    /// <summary>Mode 160 MHz (WiFi 6+)</summary>
    public bool Mode160MHz { get; set; } = false;
    
    /// <summary>Débit théorique maximum (Mbps)</summary>
    public int MaxSpeed { get; set; }
    
    /// <summary>Nombre de clients connectés</summary>
    public int ConnectedClients { get; set; }
    
    /// <summary>Utilisation du canal (%)</summary>
    public int ChannelUtilization { get; set; }
    
    /// <summary>Interférences détectées</summary>
    public int InterferenceLevel { get; set; }
}

/// <summary>
/// Configuration globale WiFi
/// </summary>
public class WiFiConfig
{
    /// <summary>Nom du réseau principal (utilisé si UseGlobalSSID)</summary>
    public string GlobalSSID { get; set; } = "NetGuard-WiFi";
    
    /// <summary>Mot de passe global</summary>
    public string GlobalPassword { get; set; } = string.Empty;
    
    /// <summary>Sécurité globale</summary>
    public WiFiSecurity GlobalSecurity { get; set; } = WiFiSecurity.WPA3_Personal;
    
    /// <summary>Activer le WiFi</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>Masquer le SSID</summary>
    public bool HideSSID { get; set; } = false;
    
    /// <summary>Isolation des clients (AP Isolation)</summary>
    public bool ClientIsolation { get; set; } = false;
    
    /// <summary>Activer le réseau invité</summary>
    public bool GuestNetworkEnabled { get; set; } = false;
    
    /// <summary>SSID du réseau invité</summary>
    public string GuestSSID { get; set; } = "NetGuard-Guest";
    
    /// <summary>Mot de passe invité</summary>
    public string GuestPassword { get; set; } = string.Empty;
    
    /// <summary>Limite de bande passante invité (Mbps, 0 = illimité)</summary>
    public int GuestBandwidthLimit { get; set; } = 50;
    
    /// <summary>Isolation du réseau invité</summary>
    public bool GuestIsolation { get; set; } = true;
    
    /// <summary>Configurations par bande</summary>
    public List<WiFiBandConfig> Bands { get; set; } = new();
    
    /// <summary>Activer le Smart Connect (SSID unifié)</summary>
    public bool SmartConnect { get; set; } = true;
    
    /// <summary>Seuil RSSI pour Band Steering (dBm)</summary>
    public int BandSteeringThreshold { get; set; } = -70;
    
    /// <summary>Activer le Fast Roaming (802.11r)</summary>
    public bool FastRoaming { get; set; } = true;
    
    /// <summary>Activer 802.11k (Neighbor Report)</summary>
    public bool NeighborReport { get; set; } = true;
    
    /// <summary>Activer 802.11v (BSS Transition)</summary>
    public bool BSSTransition { get; set; } = true;
    
    /// <summary>Planification WiFi (heures actives)</summary>
    public WiFiSchedule? Schedule { get; set; }
    
    /// <summary>Configuration Mesh</summary>
    public MeshConfig? Mesh { get; set; }
}

/// <summary>
/// Planification WiFi
/// </summary>
public class WiFiSchedule
{
    public bool Enabled { get; set; } = false;
    
    /// <summary>Jours actifs (0=Dim, 1=Lun, ..., 6=Sam)</summary>
    public List<int> ActiveDays { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6 };
    
    /// <summary>Heure de début (HH:mm)</summary>
    public string StartTime { get; set; } = "06:00";
    
    /// <summary>Heure de fin (HH:mm)</summary>
    public string EndTime { get; set; } = "23:00";
}

/// <summary>
/// Configuration du réseau Mesh
/// </summary>
public class MeshConfig
{
    /// <summary>Activer le mode Mesh</summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>Nom du réseau Mesh</summary>
    public string MeshId { get; set; } = "NetGuard-Mesh";
    
    /// <summary>Clé de chiffrement Mesh</summary>
    public string MeshKey { get; set; } = string.Empty;
    
    /// <summary>Mode du nœud actuel</summary>
    public MeshNodeRole Role { get; set; } = MeshNodeRole.Controller;
    
    /// <summary>Backhaul préféré (Ethernet ou WiFi)</summary>
    public MeshBackhaul PreferredBackhaul { get; set; } = MeshBackhaul.Auto;
    
    /// <summary>Bande dédiée au backhaul WiFi</summary>
    public WiFiBand? DedicatedBackhaulBand { get; set; } = WiFiBand.Band_5GHz;
    
    /// <summary>Activer le Daisy Chain (chaînage des nœuds)</summary>
    public bool AllowDaisyChain { get; set; } = true;
    
    /// <summary>Nombre maximum de sauts</summary>
    public int MaxHops { get; set; } = 2;
    
    /// <summary>Activer l'auto-optimisation</summary>
    public bool AutoOptimization { get; set; } = true;
    
    /// <summary>Nœuds du réseau Mesh</summary>
    public List<MeshNode> Nodes { get; set; } = new();
}

/// <summary>
/// Rôle d'un nœud Mesh
/// </summary>
public enum MeshNodeRole
{
    /// <summary>Contrôleur principal (connecté à Internet)</summary>
    Controller = 0,
    
    /// <summary>Satellite/Agent</summary>
    Agent = 1,
    
    /// <summary>Répéteur (mode dégradé)</summary>
    Repeater = 2
}

/// <summary>
/// Type de backhaul Mesh
/// </summary>
public enum MeshBackhaul
{
    Auto = 0,
    Ethernet = 1,
    WiFi = 2,
    Powerline = 3
}

/// <summary>
/// Statut d'un nœud Mesh
/// </summary>
public enum MeshNodeStatus
{
    Offline = 0,
    Online = 1,
    Connecting = 2,
    Updating = 3,
    Error = 4
}

/// <summary>
/// Nœud du réseau Mesh
/// </summary>
public class MeshNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>Nom du nœud</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Adresse MAC</summary>
    public string MacAddress { get; set; } = string.Empty;
    
    /// <summary>Adresse IP</summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>Rôle du nœud</summary>
    public MeshNodeRole Role { get; set; }
    
    /// <summary>Statut actuel</summary>
    public MeshNodeStatus Status { get; set; }
    
    /// <summary>Type de backhaul utilisé</summary>
    public MeshBackhaul BackhaulType { get; set; }
    
    /// <summary>Vitesse du backhaul (Mbps)</summary>
    public int BackhaulSpeed { get; set; }
    
    /// <summary>Qualité de la connexion backhaul (%)</summary>
    public int BackhaulQuality { get; set; }
    
    /// <summary>Nœud parent (pour les agents)</summary>
    public string? ParentNodeId { get; set; }
    
    /// <summary>Nombre de sauts jusqu'au contrôleur</summary>
    public int HopCount { get; set; }
    
    /// <summary>Emplacement/Pièce</summary>
    public string Location { get; set; } = string.Empty;
    
    /// <summary>Modèle du matériel</summary>
    public string Model { get; set; } = string.Empty;
    
    /// <summary>Version du firmware</summary>
    public string FirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>Clients connectés à ce nœud</summary>
    public int ConnectedClients { get; set; }
    
    /// <summary>Bandes actives</summary>
    public List<WiFiBand> ActiveBands { get; set; } = new();
    
    /// <summary>Dernière mise à jour</summary>
    public DateTime LastSeen { get; set; }
    
    /// <summary>Uptime en secondes</summary>
    public long Uptime { get; set; }
    
    /// <summary>Charge CPU (%)</summary>
    public int CpuUsage { get; set; }
    
    /// <summary>Utilisation mémoire (%)</summary>
    public int MemoryUsage { get; set; }
}

/// <summary>
/// Client WiFi connecté
/// </summary>
public class WiFiClient
{
    public int Id { get; set; }
    
    /// <summary>Adresse MAC du client</summary>
    public string MacAddress { get; set; } = string.Empty;
    
    /// <summary>Adresse IP</summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>Nom d'hôte</summary>
    public string Hostname { get; set; } = string.Empty;
    
    /// <summary>Fabricant</summary>
    public string Vendor { get; set; } = string.Empty;
    
    /// <summary>Bande de connexion</summary>
    public WiFiBand Band { get; set; }
    
    /// <summary>Standard WiFi utilisé</summary>
    public WiFiStandard Standard { get; set; }
    
    /// <summary>Canal</summary>
    public int Channel { get; set; }
    
    /// <summary>Largeur de canal</summary>
    public ChannelWidth ChannelWidth { get; set; }
    
    /// <summary>Force du signal (dBm)</summary>
    public int RSSI { get; set; }
    
    /// <summary>Rapport signal/bruit (dB)</summary>
    public int SNR { get; set; }
    
    /// <summary>Débit de liaison TX (Mbps)</summary>
    public int TxRate { get; set; }
    
    /// <summary>Débit de liaison RX (Mbps)</summary>
    public int RxRate { get; set; }
    
    /// <summary>Données envoyées (bytes)</summary>
    public long TxBytes { get; set; }
    
    /// <summary>Données reçues (bytes)</summary>
    public long RxBytes { get; set; }
    
    /// <summary>Nœud Mesh de connexion</summary>
    public string? MeshNodeId { get; set; }
    
    /// <summary>Durée de connexion (secondes)</summary>
    public long ConnectionTime { get; set; }
    
    /// <summary>Dernière activité</summary>
    public DateTime LastActivity { get; set; }
    
    /// <summary>Capacités du client</summary>
    public WiFiClientCapabilities Capabilities { get; set; } = new();
}

/// <summary>
/// Capacités d'un client WiFi
/// </summary>
public class WiFiClientCapabilities
{
    public bool Supports5GHz { get; set; }
    public bool Supports6GHz { get; set; }
    public bool SupportsWiFi6 { get; set; }
    public bool SupportsWiFi7 { get; set; }
    public bool SupportsMuMimo { get; set; }
    public bool SupportsOFDMA { get; set; }
    public bool Supports160MHz { get; set; }
    public bool Supports320MHz { get; set; }
    public int MaxSpatialStreams { get; set; }
}

/// <summary>
/// Réseau WiFi détecté (scan)
/// </summary>
public class WiFiNetwork
{
    public string SSID { get; set; } = string.Empty;
    public string BSSID { get; set; } = string.Empty;
    public WiFiBand Band { get; set; }
    public int Channel { get; set; }
    public ChannelWidth ChannelWidth { get; set; }
    public int RSSI { get; set; }
    public WiFiSecurity Security { get; set; }
    public WiFiStandard Standard { get; set; }
    public bool IsHidden { get; set; }
}

/// <summary>
/// Statistiques WiFi
/// </summary>
public class WiFiStats
{
    public int TotalClients { get; set; }
    public int Clients2_4GHz { get; set; }
    public int Clients5GHz { get; set; }
    public int Clients6GHz { get; set; }
    
    public long TotalTxBytes { get; set; }
    public long TotalRxBytes { get; set; }
    
    public int AverageRSSI { get; set; }
    public int AverageTxRate { get; set; }
    public int AverageRxRate { get; set; }
    
    public Dictionary<WiFiStandard, int> ClientsByStandard { get; set; } = new();
    public Dictionary<int, int> ClientsByChannel { get; set; } = new();
    
    public int MeshNodes { get; set; }
    public int MeshNodesOnline { get; set; }
}

/// <summary>
/// Analyse de canal WiFi
/// </summary>
public class ChannelAnalysis
{
    public WiFiBand Band { get; set; }
    public int Channel { get; set; }
    public int Utilization { get; set; }
    public int NetworkCount { get; set; }
    public int InterferenceScore { get; set; }
    public bool IsRecommended { get; set; }
    public bool IsDFS { get; set; } // Dynamic Frequency Selection (radar)
    public List<WiFiNetwork> Networks { get; set; } = new();
}

/// <summary>
/// DTOs pour l'API
/// </summary>
public class WiFiConfigDto
{
    public string GlobalSSID { get; set; } = string.Empty;
    public string? GlobalPassword { get; set; }
    public WiFiSecurity GlobalSecurity { get; set; }
    public bool Enabled { get; set; }
    public bool HideSSID { get; set; }
    public bool SmartConnect { get; set; }
    public bool FastRoaming { get; set; }
    public bool GuestNetworkEnabled { get; set; }
    public string? GuestSSID { get; set; }
    public string? GuestPassword { get; set; }
    public int GuestBandwidthLimit { get; set; }
}

public class WiFiBandConfigDto
{
    public WiFiBand Band { get; set; }
    public bool Enabled { get; set; }
    public string? SSID { get; set; }
    public bool UseGlobalSSID { get; set; }
    public string? Password { get; set; }
    public WiFiSecurity Security { get; set; }
    public int Channel { get; set; }
    public ChannelWidth ChannelWidth { get; set; }
    public int TxPower { get; set; }
    public bool BandSteering { get; set; }
    public bool Beamforming { get; set; }
    public bool MuMimo { get; set; }
    public bool OFDMA { get; set; }
}

public class MeshNodeDto
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public MeshNodeRole Role { get; set; }
}

public class AddMeshNodeRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
