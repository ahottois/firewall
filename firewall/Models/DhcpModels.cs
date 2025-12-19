namespace NetworkFirewall.Models;

/// <summary>
/// Configuration du serveur DHCP
/// </summary>
public class DhcpConfig
{
    public bool Enabled { get; set; }
    public string RangeStart { get; set; } = "192.168.1.100";
    public string RangeEnd { get; set; } = "192.168.1.200";
    public string SubnetMask { get; set; } = "255.255.255.0";
    public string Gateway { get; set; } = "192.168.1.1";
    public string Dns1 { get; set; } = "8.8.8.8";
    public string Dns2 { get; set; } = "8.8.4.4";
    public int LeaseTimeMinutes { get; set; } = 1440; // 24 heures
    public string ServerIdentifier { get; set; } = ""; // IP du serveur DHCP
    public string DomainName { get; set; } = "local";
    public string NetworkInterface { get; set; } = ""; // Interface réseau à utiliser
    public List<DhcpStaticReservation> StaticReservations { get; set; } = new();
    
    // Options avancées (Intermédiaire)
    public string Dns3 { get; set; } = "";
    public string NtpServer1 { get; set; } = "";
    public string NtpServer2 { get; set; } = "";
    public int RenewalTimeMinutes { get; set; } = 720; // T1 = 50% du bail
    public int RebindingTimeMinutes { get; set; } = 1260; // T2 = 87.5% du bail
    public bool AuthoritativeMode { get; set; } = true;
    public bool RapidCommit { get; set; } = false;
    
    // Options expert
    public string BroadcastAddress { get; set; } = "";
    public int MaxLeaseTimeMinutes { get; set; } = 10080; // 7 jours max
    public int MinLeaseTimeMinutes { get; set; } = 60; // 1 heure min
    public string BootFileName { get; set; } = "";
    public string TftpServerName { get; set; } = "";
    public string NextServerIp { get; set; } = ""; // Pour PXE boot
    public bool AllowUnknownClients { get; set; } = true;
    public bool ConflictDetection { get; set; } = true;
    public int ConflictDetectionAttempts { get; set; } = 2;
    public int OfferDelayMs { get; set; } = 0;
    public bool LogAllPackets { get; set; } = false;
    public List<string> DenyMacList { get; set; } = new();
    public List<string> AllowMacList { get; set; } = new();
    public List<DhcpCustomOption> CustomOptions { get; set; } = new();
}

/// <summary>
/// Option DHCP personnalisée
/// </summary>
public class DhcpCustomOption
{
    public int OptionCode { get; set; }
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Niveau de configuration DHCP
/// </summary>
public enum DhcpConfigLevel
{
    Easy,
    Intermediate,
    Expert
}

/// <summary>
/// Configuration DHCP niveau Facile - Pour débutants
/// </summary>
public class DhcpConfigEasy
{
    public bool Enabled { get; set; }
    public string RangeStart { get; set; } = "192.168.1.100";
    public string RangeEnd { get; set; } = "192.168.1.200";
    public string Gateway { get; set; } = "192.168.1.1";
    public string Dns1 { get; set; } = "8.8.8.8";
    public string Dns2 { get; set; } = "8.8.4.4";
}

/// <summary>
/// Configuration DHCP niveau Intermédiaire
/// </summary>
public class DhcpConfigIntermediate : DhcpConfigEasy
{
    public string SubnetMask { get; set; } = "255.255.255.0";
    public int LeaseTimeMinutes { get; set; } = 1440;
    public string DomainName { get; set; } = "local";
    public string NetworkInterface { get; set; } = "";
    public string NtpServer1 { get; set; } = "";
    public bool AuthoritativeMode { get; set; } = true;
    public List<DhcpStaticReservation> StaticReservations { get; set; } = new();
}

/// <summary>
/// Métadonnées d'aide pour les paramètres DHCP
/// </summary>
public class DhcpSettingHelp
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Example { get; set; } = "";
    public string Warning { get; set; } = "";
    public DhcpConfigLevel Level { get; set; }
}

/// <summary>
/// Réponse de configuration avec aide
/// </summary>
public class DhcpConfigResponse
{
    public DhcpConfig Config { get; set; } = new();
    public DhcpConfigLevel Level { get; set; }
    public List<DhcpSettingHelp> Help { get; set; } = new();
}

/// <summary>
/// Aide complète pour tous les paramètres DHCP
/// </summary>
public static class DhcpHelpProvider
{
    public static List<DhcpSettingHelp> GetHelp(DhcpConfigLevel level)
    {
        var help = new List<DhcpSettingHelp>();
        
        // === NIVEAU FACILE ===
        help.Add(new DhcpSettingHelp
        {
            Name = "enabled",
            Description = "Active ou désactive le serveur DHCP. Quand activé, votre serveur distribuera automatiquement les adresses IP aux appareils du réseau.",
            Example = "true ou false",
            Warning = "?? Désactivez le DHCP de votre box/routeur avant d'activer celui-ci pour éviter les conflits !",
            Level = DhcpConfigLevel.Easy
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "rangeStart",
            Description = "Première adresse IP à distribuer aux appareils. Les appareils recevront une IP entre cette valeur et rangeEnd.",
            Example = "192.168.1.100",
            Warning = "Doit être dans le même sous-réseau que votre passerelle",
            Level = DhcpConfigLevel.Easy
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "rangeEnd",
            Description = "Dernière adresse IP à distribuer. Définit combien d'appareils peuvent être connectés simultanément.",
            Example = "192.168.1.200 (permet 101 appareils)",
            Warning = "",
            Level = DhcpConfigLevel.Easy
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "gateway",
            Description = "Adresse IP de votre box/routeur Internet. Les appareils utiliseront cette adresse pour accéder à Internet.",
            Example = "192.168.1.1 (box) ou 192.168.1.254",
            Warning = "Vérifiez l'adresse sur votre box ou avec 'ip route' sous Linux",
            Level = DhcpConfigLevel.Easy
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "dns1",
            Description = "Serveur DNS principal pour la résolution des noms de domaine (convertit google.com en adresse IP).",
            Example = "8.8.8.8 (Google), 1.1.1.1 (Cloudflare), 9.9.9.9 (Quad9)",
            Warning = "",
            Level = DhcpConfigLevel.Easy
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "dns2",
            Description = "Serveur DNS de secours, utilisé si le DNS principal ne répond pas.",
            Example = "8.8.4.4 (Google), 1.0.0.1 (Cloudflare)",
            Warning = "",
            Level = DhcpConfigLevel.Easy
        });
        
        if (level == DhcpConfigLevel.Easy)
            return help;
        
        // === NIVEAU INTERMÉDIAIRE ===
        help.Add(new DhcpSettingHelp
        {
            Name = "subnetMask",
            Description = "Masque de sous-réseau définissant la taille de votre réseau local.",
            Example = "255.255.255.0 (/24 = 254 hôtes), 255.255.0.0 (/16 = 65534 hôtes)",
            Warning = "Doit correspondre à la configuration de votre routeur",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "leaseTimeMinutes",
            Description = "Durée du bail en minutes. Après expiration, l'appareil doit renouveler son IP.",
            Example = "1440 (24h), 480 (8h pour WiFi public), 10080 (7 jours pour bureau)",
            Warning = "Baux courts = plus de trafic DHCP mais IPs libérées plus vite",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "domainName",
            Description = "Nom de domaine local pour la résolution DNS interne.",
            Example = "home.local, lan, monreseau.local",
            Warning = "Évitez .local si vous avez des appareils Apple (conflit mDNS)",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "networkInterface",
            Description = "Interface réseau sur laquelle écouter (laissez vide pour auto-détection).",
            Example = "eth0, enp3s0, wlan0",
            Warning = "Utilisez 'ip link' pour lister vos interfaces",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "ntpServer1",
            Description = "Serveur de temps (NTP) pour synchroniser l'horloge des appareils.",
            Example = "pool.ntp.org, time.google.com, fr.pool.ntp.org",
            Warning = "",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "authoritativeMode",
            Description = "En mode autoritaire, le serveur répond NAK aux requêtes pour des IPs hors de sa plage.",
            Example = "true (recommandé si seul serveur DHCP)",
            Warning = "Désactivez si plusieurs serveurs DHCP coexistent",
            Level = DhcpConfigLevel.Intermediate
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "staticReservations",
            Description = "Attribuez toujours la même IP à certains appareils (serveurs, imprimantes...).",
            Example = "MAC: AA:BB:CC:DD:EE:FF ? IP: 192.168.1.50",
            Warning = "L'IP réservée doit être dans la plage DHCP",
            Level = DhcpConfigLevel.Intermediate
        });
        
        if (level == DhcpConfigLevel.Intermediate)
            return help;
        
        // === NIVEAU EXPERT ===
        help.Add(new DhcpSettingHelp
        {
            Name = "renewalTimeMinutes",
            Description = "Temps T1 - Moment où le client tente de renouveler son bail (généralement 50% du bail).",
            Example = "720 (pour un bail de 24h)",
            Warning = "T1 < T2 < LeaseTime obligatoire",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "rebindingTimeMinutes",
            Description = "Temps T2 - Si T1 échoue, le client tente de contacter n'importe quel serveur DHCP (87.5% du bail).",
            Example = "1260 (pour un bail de 24h)",
            Warning = "",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "broadcastAddress",
            Description = "Adresse de broadcast du réseau (calculée automatiquement si vide).",
            Example = "192.168.1.255",
            Warning = "Ne modifiez que si vous savez ce que vous faites",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "maxLeaseTimeMinutes",
            Description = "Durée maximale de bail qu'un client peut demander.",
            Example = "10080 (7 jours)",
            Warning = "",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "minLeaseTimeMinutes",
            Description = "Durée minimale de bail accordée.",
            Example = "60 (1 heure)",
            Warning = "",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "bootFileName",
            Description = "Nom du fichier de boot pour le démarrage PXE/réseau.",
            Example = "pxelinux.0, bootx64.efi",
            Warning = "Nécessite un serveur TFTP configuré",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "tftpServerName",
            Description = "Nom d'hôte du serveur TFTP pour le boot réseau.",
            Example = "pxeserver.local",
            Warning = "",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "nextServerIp",
            Description = "Adresse IP du serveur de boot suivant (option siaddr pour PXE).",
            Example = "192.168.1.10",
            Warning = "Pour le boot réseau PXE/UEFI",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "allowUnknownClients",
            Description = "Autoriser les clients non enregistrés à obtenir une IP.",
            Example = "true (ouvert) ou false (liste blanche uniquement)",
            Warning = "false = seules les MAC dans allowMacList ou staticReservations reçoivent une IP",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "conflictDetection",
            Description = "Vérifie si l'IP est libre avant de l'attribuer (ping ICMP).",
            Example = "true (recommandé)",
            Warning = "Ajoute un délai mais évite les conflits d'IP",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "conflictDetectionAttempts",
            Description = "Nombre de tentatives de ping pour détecter un conflit.",
            Example = "2",
            Warning = "",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "offerDelayMs",
            Description = "Délai en millisecondes avant d'envoyer une offre DHCP.",
            Example = "0 (instantané), 500 (priorité basse)",
            Warning = "Utile pour donner priorité à un autre serveur DHCP",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "logAllPackets",
            Description = "Journaliser tous les paquets DHCP reçus et envoyés.",
            Example = "false (production), true (debug)",
            Warning = "Génère beaucoup de logs, à utiliser pour le debug uniquement",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "denyMacList",
            Description = "Liste noire des adresses MAC qui ne recevront jamais d'IP.",
            Example = "[\"AA:BB:CC:DD:EE:FF\", \"11:22:33:44:55:66\"]",
            Warning = "Prioritaire sur allowMacList",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "allowMacList",
            Description = "Liste blanche des adresses MAC autorisées (si allowUnknownClients=false).",
            Example = "[\"AA:BB:CC:DD:EE:FF\"]",
            Warning = "Ignoré si allowUnknownClients=true",
            Level = DhcpConfigLevel.Expert
        });
        
        help.Add(new DhcpSettingHelp
        {
            Name = "customOptions",
            Description = "Options DHCP personnalisées (codes 1-254).",
            Example = "Code 66 = TFTP Server, Code 67 = Boot File",
            Warning = "Pour utilisateurs avancés uniquement",
            Level = DhcpConfigLevel.Expert
        });
        
        return help;
    }
}

/// <summary>
/// Bail DHCP actif
/// </summary>
public class DhcpLease
{
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public DateTime LeaseStart { get; set; }
    public DateTime Expiration { get; set; }
    public DhcpLeaseState State { get; set; } = DhcpLeaseState.Active;
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// Réservation statique DHCP (IP fixe pour une MAC)
/// </summary>
public class DhcpStaticReservation
{
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// État d'un bail DHCP
/// </summary>
public enum DhcpLeaseState
{
    Offered,    // IP proposée mais pas encore confirmée
    Active,     // Bail actif
    Expired,    // Bail expiré
    Released    // Libéré par le client
}

/// <summary>
/// Types de messages DHCP (Option 53)
/// </summary>
public enum DhcpMessageType : byte
{
    Discover = 1,
    Offer = 2,
    Request = 3,
    Decline = 4,
    Ack = 5,
    Nak = 6,
    Release = 7,
    Inform = 8
}

/// <summary>
/// Options DHCP standard (RFC 2132)
/// </summary>
public enum DhcpOption : byte
{
    Pad = 0,
    SubnetMask = 1,
    Router = 3,
    DnsServer = 6,
    Hostname = 12,
    DomainName = 15,
    BroadcastAddress = 28,
    RequestedIpAddress = 50,
    LeaseTime = 51,
    MessageType = 53,
    ServerIdentifier = 54,
    ParameterRequestList = 55,
    RenewalTime = 58,
    RebindingTime = 59,
    ClientIdentifier = 61,
    End = 255
}

/// <summary>
/// Paquet DHCP complet (RFC 2131)
/// </summary>
public class DhcpPacket
{
    // Champs fixes du paquet DHCP
    public byte Op { get; set; }           // 1 = BOOTREQUEST, 2 = BOOTREPLY
    public byte HType { get; set; } = 1;   // Hardware type (1 = Ethernet)
    public byte HLen { get; set; } = 6;    // Hardware address length (6 pour MAC)
    public byte Hops { get; set; }         // Relay hops
    public uint Xid { get; set; }          // Transaction ID
    public ushort Secs { get; set; }       // Seconds elapsed
    public ushort Flags { get; set; }      // Flags (0x8000 = broadcast)
    public uint CiAddr { get; set; }       // Client IP address
    public uint YiAddr { get; set; }       // Your (client) IP address
    public uint SiAddr { get; set; }       // Server IP address
    public uint GiAddr { get; set; }       // Gateway IP address
    public byte[] ChAddr { get; set; } = new byte[16];  // Client hardware address
    public byte[] SName { get; set; } = new byte[64];   // Server host name
    public byte[] File { get; set; } = new byte[128];   // Boot file name
    
    // Magic cookie DHCP (99.130.83.99)
    public static readonly byte[] MagicCookie = { 99, 130, 83, 99 };
    
    // Options DHCP (après le magic cookie)
    public Dictionary<DhcpOption, byte[]> Options { get; set; } = new();
    
    /// <summary>
    /// Obtenir l'adresse MAC du client
    /// </summary>
    public string GetClientMac()
    {
        return string.Join(":", ChAddr.Take(HLen).Select(b => b.ToString("X2")));
    }
    
    /// <summary>
    /// Obtenir le type de message DHCP
    /// </summary>
    public DhcpMessageType? GetMessageType()
    {
        if (Options.TryGetValue(DhcpOption.MessageType, out var value) && value.Length > 0)
            return (DhcpMessageType)value[0];
        return null;
    }
    
    /// <summary>
    /// Obtenir l'IP demandée (option 50)
    /// </summary>
    public uint? GetRequestedIp()
    {
        if (Options.TryGetValue(DhcpOption.RequestedIpAddress, out var value) && value.Length == 4)
            return BitConverter.ToUInt32(value.Reverse().ToArray(), 0);
        return null;
    }
    
    /// <summary>
    /// Obtenir le Server Identifier (option 54)
    /// </summary>
    public uint? GetServerIdentifier()
    {
        if (Options.TryGetValue(DhcpOption.ServerIdentifier, out var value) && value.Length == 4)
            return BitConverter.ToUInt32(value.Reverse().ToArray(), 0);
        return null;
    }
    
    /// <summary>
    /// Obtenir le hostname du client (option 12)
    /// </summary>
    public string? GetHostname()
    {
        if (Options.TryGetValue(DhcpOption.Hostname, out var value))
            return System.Text.Encoding.ASCII.GetString(value).TrimEnd('\0');
        return null;
    }
    
    /// <summary>
    /// Convertir une IP uint en string
    /// </summary>
    public static string IpToString(uint ip)
    {
        return $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }
    
    /// <summary>
    /// Convertir une IP string en uint
    /// </summary>
    public static uint StringToIp(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) throw new ArgumentException("Invalid IP format");
        return ((uint)byte.Parse(parts[0]) << 24) |
               ((uint)byte.Parse(parts[1]) << 16) |
               ((uint)byte.Parse(parts[2]) << 8) |
               (uint)byte.Parse(parts[3]);
    }
    
    /// <summary>
    /// Convertir IP uint en bytes (network order)
    /// </summary>
    public static byte[] IpToBytes(uint ip)
    {
        return new byte[]
        {
            (byte)((ip >> 24) & 0xFF),
            (byte)((ip >> 16) & 0xFF),
            (byte)((ip >> 8) & 0xFF),
            (byte)(ip & 0xFF)
        };
    }
}
