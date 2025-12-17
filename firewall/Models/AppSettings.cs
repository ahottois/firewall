namespace NetworkFirewall.Models;

/// <summary>
/// Configuration de l'application
/// </summary>
public class AppSettings
{
    public int WebPort { get; set; } = 9764;
    public string? NetworkInterface { get; set; }
    public string DatabasePath { get; set; } = "firewall.db";
    public bool EnablePacketCapture { get; set; } = true;
    public bool EnableMitm { get; set; } = false;
    public int AlertRetentionDays { get; set; } = 30;
    public int TrafficLogRetentionDays { get; set; } = 7;
    public List<string> TrustedMacAddresses { get; set; } = new();
    public List<int> SuspiciousPorts { get; set; } = new() { 22, 23, 3389, 445, 135, 139 };
    public int PortScanThreshold { get; set; } = 10;
    public int PortScanTimeWindowSeconds { get; set; } = 60;
}
