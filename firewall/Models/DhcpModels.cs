namespace NetworkFirewall.Models;

public class DhcpConfig
{
    public bool Enabled { get; set; }
    public string RangeStart { get; set; } = "192.168.1.100";
    public string RangeEnd { get; set; } = "192.168.1.200";
    public string SubnetMask { get; set; } = "255.255.255.0";
    public string Gateway { get; set; } = "192.168.1.1";
    public string Dns1 { get; set; } = "8.8.8.8";
    public string Dns2 { get; set; } = "8.8.4.4";
    public int LeaseTimeMinutes { get; set; } = 1440; // 24 hours
}

public class DhcpLease
{
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
}
