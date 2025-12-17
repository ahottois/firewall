namespace NetworkFirewall.Models;

/// <summary>
/// Configuration de l'application
/// </summary>
public class AppSettings
{
    public int WebPort { get; set; } = 5000;
    public string DatabasePath { get; set; } = "firewall.db";
    public string NetworkInterface { get; set; } = "";
    public bool EnablePacketCapture { get; set; } = true;
    public bool EnableThreatFeeds { get; set; } = true;
    public int AlertRetentionDays { get; set; } = 30;
    public int TrafficLogRetentionDays { get; set; } = 7;
    public List<int> SuspiciousPorts { get; set; } = new() { 21, 22, 23, 3389, 445, 135, 139 };
    
    // Security Settings
    public string AbuseIpDbApiKey { get; set; } = string.Empty;
    public bool EnableAutoSecurityScan { get; set; } = false;
    public int PortScanTimeWindowSeconds { get; set; } = 60;
    public int PortScanThreshold { get; set; } = 20;

    public DnsSettings Dns { get; set; } = new();
    public RouterSettings Router { get; set; } = new();
}

public class RouterSettings
{
    public bool EnableIpForwarding { get; set; } = false;
    public List<PortMappingRule> PortMappings { get; set; } = new();
}

public class PortMappingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int ListenPort { get; set; }
    public string TargetIp { get; set; } = string.Empty;
    public int TargetPort { get; set; }
    public string Protocol { get; set; } = "TCP"; // TCP, UDP
    public bool Enabled { get; set; } = true;
}

public class DnsSettings
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 53;
    public string UpstreamDns { get; set; } = "8.8.8.8";
    public List<BlocklistSource> Blocklists { get; set; } = new()
    {
        new() { Name = "Ads & Trackers", Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", Category = "Ads" },
        new() { Name = "Malware", Url = "https://urlhaus.abuse.ch/downloads/hostfile/", Category = "Malware" },
        new() { Name = "Adult", Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/porn/hosts", Category = "Adult" }
    };
}

public class BlocklistSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public bool Enabled { get; set; } = true;
}
