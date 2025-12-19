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
