using System.Net;
using System.Net.Sockets;

namespace NetworkFirewall.Models;

#region IPv4 / IPv6 Models

/// <summary>
/// Configuration IP d'une interface
/// </summary>
public class IpConfiguration
{
    public int Id { get; set; }
    public string InterfaceName { get; set; } = string.Empty;
    
    // IPv4
    public bool IPv4Enabled { get; set; } = true;
    public string? IPv4Address { get; set; }
    public string? IPv4SubnetMask { get; set; }
    public string? IPv4Gateway { get; set; }
    public bool IPv4DHCP { get; set; } = true;
    
    // IPv6
    public bool IPv6Enabled { get; set; } = true;
    public string? IPv6Address { get; set; }
    public int IPv6PrefixLength { get; set; } = 64;
    public string? IPv6Gateway { get; set; }
    public bool IPv6SLAAC { get; set; } = true; // Stateless Address Autoconfiguration
    public bool IPv6DHCPv6 { get; set; } = false;
    
    // DNS
    public List<string> DnsServers { get; set; } = new();
    
    // MTU
    public int MTU { get; set; } = 1500;
}

/// <summary>
/// Statistiques IP
/// </summary>
public class IpStatistics
{
    public long PacketsReceived { get; set; }
    public long PacketsSent { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public long PacketsDropped { get; set; }
    public long Errors { get; set; }
    public DateTime LastUpdated { get; set; }
}

#endregion

#region ICMP Models

/// <summary>
/// Résultat d'un ping
/// </summary>
public class PingResult
{
    public string TargetHost { get; set; } = string.Empty;
    public string? ResolvedAddress { get; set; }
    public bool Success { get; set; }
    public long RoundtripTime { get; set; } // ms
    public int TTL { get; set; }
    public int BufferSize { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Statistiques de ping (multiple)
/// </summary>
public class PingStatistics
{
    public string Host { get; set; } = string.Empty;
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public int PacketsLost { get; set; }
    public double PacketLossPercent => PacketsSent > 0 ? (PacketsLost * 100.0 / PacketsSent) : 0;
    public long MinRoundtrip { get; set; }
    public long MaxRoundtrip { get; set; }
    public long AvgRoundtrip { get; set; }
    public List<PingResult> Results { get; set; } = new();
}

/// <summary>
/// Saut dans un traceroute
/// </summary>
public class TracerouteHop
{
    public int HopNumber { get; set; }
    public string? Address { get; set; }
    public string? Hostname { get; set; }
    public long? RoundtripTime1 { get; set; }
    public long? RoundtripTime2 { get; set; }
    public long? RoundtripTime3 { get; set; }
    public bool TimedOut { get; set; }
}

/// <summary>
/// Résultat d'un traceroute
/// </summary>
public class TracerouteResult
{
    public string TargetHost { get; set; } = string.Empty;
    public string? ResolvedAddress { get; set; }
    public bool Completed { get; set; }
    public int MaxHops { get; set; }
    public List<TracerouteHop> Hops { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

#endregion

#region Routing Models

/// <summary>
/// Type de protocole de routage
/// </summary>
public enum RoutingProtocol
{
    Static = 0,
    RIP = 1,      // Routing Information Protocol
    OSPF = 2,     // Open Shortest Path First
    BGP = 3       // Border Gateway Protocol
}

/// <summary>
/// Entrée de la table de routage
/// </summary>
public class RouteEntry
{
    public int Id { get; set; }
    public string Destination { get; set; } = string.Empty;  // ex: 192.168.1.0
    public string SubnetMask { get; set; } = string.Empty;   // ex: 255.255.255.0
    public int PrefixLength { get; set; }                    // ex: 24 (CIDR)
    public string Gateway { get; set; } = string.Empty;      // ex: 192.168.1.1
    public string Interface { get; set; } = string.Empty;    // ex: eth0
    public int Metric { get; set; } = 1;                     // Coût
    public RoutingProtocol Protocol { get; set; } = RoutingProtocol.Static;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int Age { get; set; } // Secondes depuis la dernière mise à jour
}

/// <summary>
/// Configuration RIP
/// </summary>
public class RipConfig
{
    public bool Enabled { get; set; } = false;
    public int Version { get; set; } = 2; // RIPv1 ou RIPv2
    public int UpdateInterval { get; set; } = 30; // secondes
    public int TimeoutInterval { get; set; } = 180; // secondes
    public int GarbageCollectionInterval { get; set; } = 120; // secondes
    public List<string> Networks { get; set; } = new(); // Réseaux à annoncer
    public List<string> PassiveInterfaces { get; set; } = new();
    public bool SplitHorizon { get; set; } = true;
    public bool PoisonReverse { get; set; } = true;
}

/// <summary>
/// Voisin RIP
/// </summary>
public class RipNeighbor
{
    public string Address { get; set; } = string.Empty;
    public DateTime LastUpdate { get; set; }
    public int BadPackets { get; set; }
    public int BadRoutes { get; set; }
}

/// <summary>
/// Configuration OSPF
/// </summary>
public class OspfConfig
{
    public bool Enabled { get; set; } = false;
    public string RouterId { get; set; } = string.Empty;
    public int ProcessId { get; set; } = 1;
    public List<OspfArea> Areas { get; set; } = new();
    public List<OspfNetwork> Networks { get; set; } = new();
    public int HelloInterval { get; set; } = 10; // secondes
    public int DeadInterval { get; set; } = 40; // secondes
    public int RetransmitInterval { get; set; } = 5;
    public bool AutoCost { get; set; } = true;
    public int ReferenceBandwidth { get; set; } = 100; // Mbps
}

/// <summary>
/// Zone OSPF
/// </summary>
public class OspfArea
{
    public int AreaId { get; set; }
    public string AreaIdString => AreaId == 0 ? "0.0.0.0" : $"0.0.0.{AreaId}";
    public bool IsBackbone => AreaId == 0;
    public bool IsStub { get; set; } = false;
    public bool IsNSSA { get; set; } = false; // Not So Stubby Area
    public string? AuthenticationType { get; set; }
}

/// <summary>
/// Réseau OSPF
/// </summary>
public class OspfNetwork
{
    public string Network { get; set; } = string.Empty;
    public string Wildcard { get; set; } = string.Empty; // Inverse du masque
    public int AreaId { get; set; }
}

/// <summary>
/// Voisin OSPF
/// </summary>
public class OspfNeighbor
{
    public string RouterId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public OspfNeighborState State { get; set; }
    public int Priority { get; set; }
    public string Interface { get; set; } = string.Empty;
    public TimeSpan DeadTime { get; set; }
}

public enum OspfNeighborState
{
    Down, Attempt, Init, TwoWay, ExStart, Exchange, Loading, Full
}

/// <summary>
/// Configuration BGP
/// </summary>
public class BgpConfig
{
    public bool Enabled { get; set; } = false;
    public int LocalAS { get; set; } // Autonomous System Number
    public string RouterId { get; set; } = string.Empty;
    public List<BgpNeighbor> Neighbors { get; set; } = new();
    public List<BgpNetwork> Networks { get; set; } = new();
    public int KeepaliveInterval { get; set; } = 60;
    public int HoldTime { get; set; } = 180;
}

/// <summary>
/// Voisin BGP
/// </summary>
public class BgpNeighbor
{
    public string Address { get; set; } = string.Empty;
    public int RemoteAS { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public BgpNeighborState State { get; set; }
    public TimeSpan Uptime { get; set; }
    public int PrefixesReceived { get; set; }
    public int PrefixesSent { get; set; }
    public string? Password { get; set; } // MD5
}

public enum BgpNeighborState
{
    Idle, Connect, Active, OpenSent, OpenConfirm, Established
}

/// <summary>
/// Réseau BGP à annoncer
/// </summary>
public class BgpNetwork
{
    public string Prefix { get; set; } = string.Empty;
    public int PrefixLength { get; set; }
}

#endregion

#region NAT / PAT Models

/// <summary>
/// Type de NAT
/// </summary>
public enum NatType
{
    SNAT = 0,      // Source NAT (masquerade)
    DNAT = 1,      // Destination NAT (port forwarding)
    FullNAT = 2,   // Both source and destination
    PAT = 3        // Port Address Translation
}

/// <summary>
/// Règle NAT
/// </summary>
public class NatRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public NatType Type { get; set; }
    
    // Critères de correspondance
    public string? SourceAddress { get; set; }
    public string? SourcePort { get; set; }
    public string? DestinationAddress { get; set; }
    public string? DestinationPort { get; set; }
    public string Protocol { get; set; } = "tcp"; // tcp, udp, all
    public string? InInterface { get; set; }
    public string? OutInterface { get; set; }
    
    // Translation
    public string? TranslatedAddress { get; set; }
    public string? TranslatedPort { get; set; }
    
    // Masquerade (pour SNAT dynamique)
    public bool Masquerade { get; set; } = false;
    
    // Statistiques
    public long PacketsMatched { get; set; }
    public long BytesMatched { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Entrée de la table NAT (connexion active)
/// </summary>
public class NatConnection
{
    public string Protocol { get; set; } = string.Empty;
    public string OriginalSource { get; set; } = string.Empty;
    public int OriginalSourcePort { get; set; }
    public string OriginalDestination { get; set; } = string.Empty;
    public int OriginalDestinationPort { get; set; }
    public string TranslatedSource { get; set; } = string.Empty;
    public int TranslatedSourcePort { get; set; }
    public string TranslatedDestination { get; set; } = string.Empty;
    public int TranslatedDestinationPort { get; set; }
    public string State { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; }
}

/// <summary>
/// Configuration NAT globale
/// </summary>
public class NatConfig
{
    public bool Enabled { get; set; } = true;
    public bool MasqueradeEnabled { get; set; } = true;
    public string? WanInterface { get; set; }
    public List<string> LanInterfaces { get; set; } = new();
    public List<NatRule> Rules { get; set; } = new();
}

#endregion

#region SSH Models

/// <summary>
/// Configuration SSH
/// </summary>
public class SshConfig
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 22;
    public bool PasswordAuthentication { get; set; } = true;
    public bool PubkeyAuthentication { get; set; } = true;
    public bool RootLogin { get; set; } = false;
    public int MaxAuthTries { get; set; } = 3;
    public int LoginGraceTime { get; set; } = 60; // secondes
    public int ClientAliveInterval { get; set; } = 300;
    public int ClientAliveCountMax { get; set; } = 3;
    public List<string> AllowedUsers { get; set; } = new();
    public List<string> AllowedIPs { get; set; } = new();
    public List<string> DeniedIPs { get; set; } = new();
    public string? Banner { get; set; }
}

/// <summary>
/// Session SSH active
/// </summary>
public class SshSession
{
    public string User { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public DateTime ConnectedAt { get; set; }
    public string? Terminal { get; set; }
    public int Pid { get; set; }
}

/// <summary>
/// Clé SSH autorisée
/// </summary>
public class SshAuthorizedKey
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string KeyType { get; set; } = string.Empty; // ssh-rsa, ssh-ed25519, etc.
    public string? Fingerprint { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsed { get; set; }
}

#endregion

#region NTP Models

/// <summary>
/// Configuration NTP
/// </summary>
public class NtpConfig
{
    public bool Enabled { get; set; } = true;
    public bool IsServer { get; set; } = false; // Servir de serveur NTP
    public List<NtpServer> Servers { get; set; } = new();
    public string? Timezone { get; set; }
    public int PollInterval { get; set; } = 64; // secondes
    public int MinPollInterval { get; set; } = 6; // 2^6 = 64 sec
    public int MaxPollInterval { get; set; } = 10; // 2^10 = 1024 sec
}

/// <summary>
/// Serveur NTP
/// </summary>
public class NtpServer
{
    public string Address { get; set; } = string.Empty;
    public bool Prefer { get; set; } = false;
    public bool IBurst { get; set; } = true;
    public NtpServerStatus Status { get; set; }
    public int Stratum { get; set; }
    public double Offset { get; set; } // millisecondes
    public double Delay { get; set; }  // millisecondes
    public double Jitter { get; set; } // millisecondes
    public DateTime? LastSync { get; set; }
}

public enum NtpServerStatus
{
    Unknown, Unreachable, Syncing, Synchronized, Selected
}

/// <summary>
/// Statut de synchronisation NTP
/// </summary>
public class NtpStatus
{
    public bool Synchronized { get; set; }
    public string? CurrentServer { get; set; }
    public int Stratum { get; set; }
    public double Offset { get; set; }
    public double RootDelay { get; set; }
    public double RootDispersion { get; set; }
    public DateTime? LastSync { get; set; }
    public DateTime SystemTime { get; set; }
    public string? Timezone { get; set; }
}

#endregion

#region SNMP Models

/// <summary>
/// Version SNMP
/// </summary>
public enum SnmpVersion
{
    V1 = 1,
    V2c = 2,
    V3 = 3
}

/// <summary>
/// Configuration SNMP
/// </summary>
public class SnmpConfig
{
    public bool Enabled { get; set; } = false;
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public int Port { get; set; } = 161;
    public int TrapPort { get; set; } = 162;
    
    // V1/V2c
    public string? ReadCommunity { get; set; } = "public";
    public string? WriteCommunity { get; set; }
    
    // V3
    public List<SnmpUser> Users { get; set; } = new();
    
    // Contact et localisation
    public string? SysContact { get; set; }
    public string? SysLocation { get; set; }
    public string? SysName { get; set; }
    
    // ACL
    public List<string> AllowedHosts { get; set; } = new();
    
    // Traps
    public List<SnmpTrapReceiver> TrapReceivers { get; set; } = new();
}

/// <summary>
/// Utilisateur SNMP v3
/// </summary>
public class SnmpUser
{
    public string Username { get; set; } = string.Empty;
    public SnmpSecurityLevel SecurityLevel { get; set; }
    public SnmpAuthProtocol AuthProtocol { get; set; }
    public string? AuthPassword { get; set; }
    public SnmpPrivProtocol PrivProtocol { get; set; }
    public string? PrivPassword { get; set; }
}

public enum SnmpSecurityLevel
{
    NoAuthNoPriv = 0,
    AuthNoPriv = 1,
    AuthPriv = 2
}

public enum SnmpAuthProtocol
{
    None = 0,
    MD5 = 1,
    SHA = 2,
    SHA256 = 3,
    SHA512 = 4
}

public enum SnmpPrivProtocol
{
    None = 0,
    DES = 1,
    AES128 = 2,
    AES192 = 3,
    AES256 = 4
}

/// <summary>
/// Récepteur de traps SNMP
/// </summary>
public class SnmpTrapReceiver
{
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 162;
    public string? Community { get; set; }
    public SnmpVersion Version { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Statistiques SNMP
/// </summary>
public class SnmpStatistics
{
    public long PacketsReceived { get; set; }
    public long PacketsSent { get; set; }
    public long GetRequests { get; set; }
    public long GetNextRequests { get; set; }
    public long SetRequests { get; set; }
    public long TrapsGenerated { get; set; }
    public long BadVersions { get; set; }
    public long BadCommunity { get; set; }
    public long ParseErrors { get; set; }
}

#endregion

#region DTOs

public class PingRequest
{
    public string Host { get; set; } = string.Empty;
    public int Count { get; set; } = 4;
    public int Timeout { get; set; } = 1000; // ms
    public int TTL { get; set; } = 64;
    public int BufferSize { get; set; } = 32;
}

public class TracerouteRequest
{
    public string Host { get; set; } = string.Empty;
    public int MaxHops { get; set; } = 30;
    public int Timeout { get; set; } = 3000;
}

public class RouteEntryDto
{
    public string Destination { get; set; } = string.Empty;
    public int PrefixLength { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
    public int Metric { get; set; } = 1;
}

public class NatRuleDto
{
    public string Name { get; set; } = string.Empty;
    public NatType Type { get; set; }
    public string Protocol { get; set; } = "tcp";
    public string? SourceAddress { get; set; }
    public string? SourcePort { get; set; }
    public string? DestinationAddress { get; set; }
    public string? DestinationPort { get; set; }
    public string? TranslatedAddress { get; set; }
    public string? TranslatedPort { get; set; }
    public string? InInterface { get; set; }
    public string? OutInterface { get; set; }
    public bool Masquerade { get; set; }
}

#endregion
