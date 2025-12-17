namespace NetworkFirewall.Models;

public class DnsLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ClientIp { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string QueryType { get; set; } = "A";
    public DnsAction Action { get; set; }
    public string? BlocklistCategory { get; set; }
    public long ResponseTimeMs { get; set; }
}

public enum DnsAction
{
    Allowed,
    Blocked,
    Error
}
