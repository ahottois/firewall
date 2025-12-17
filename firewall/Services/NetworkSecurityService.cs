using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INetworkSecurityService
{
    Task<PortScanResult> ScanDevicePortsAsync(string ipAddress, int[]? portsToScan = null);
    Task<VulnerabilityReport> ScanDeviceVulnerabilitiesAsync(string ipAddress);
    Task<NetworkSecurityReport> GenerateSecurityReportAsync();
    Task<IEnumerable<OpenPort>> GetOpenPortsOnNetworkAsync();
    SecurityScore CalculateNetworkSecurityScore();
}

public class NetworkSecurityService : INetworkSecurityService
{
    private readonly ILogger<NetworkSecurityService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly IThreatIntelligenceService _threatIntelligence;
    private readonly AppSettings _settings;

    // Common ports to scan
    private static readonly int[] CommonPorts = new[]
    {
        21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995,
        1433, 1521, 3306, 3389, 5432, 5900, 5901, 6379, 8080, 8443, 27017
    };

    // Well-known vulnerable services
    private static readonly Dictionary<int, ServiceInfo> KnownServices = new()
    {
        { 21, new ServiceInfo("FTP", "File Transfer Protocol - Often has anonymous access", RiskLevel.Medium) },
        { 22, new ServiceInfo("SSH", "Secure Shell - Target for brute force", RiskLevel.Low) },
        { 23, new ServiceInfo("Telnet", "Unencrypted remote access - HIGH RISK", RiskLevel.Critical) },
        { 25, new ServiceInfo("SMTP", "Email server - Can be used for spam relay", RiskLevel.Medium) },
        { 53, new ServiceInfo("DNS", "DNS Server - Can be used for amplification attacks", RiskLevel.Medium) },
        { 80, new ServiceInfo("HTTP", "Web server - Check for vulnerabilities", RiskLevel.Low) },
        { 110, new ServiceInfo("POP3", "Email retrieval - Unencrypted", RiskLevel.Medium) },
        { 135, new ServiceInfo("RPC", "Windows RPC - Often exploited", RiskLevel.High) },
        { 139, new ServiceInfo("NetBIOS", "Windows file sharing - Security risk", RiskLevel.High) },
        { 143, new ServiceInfo("IMAP", "Email retrieval - Often unencrypted", RiskLevel.Medium) },
        { 443, new ServiceInfo("HTTPS", "Secure web server", RiskLevel.Low) },
        { 445, new ServiceInfo("SMB", "Windows file sharing - WannaCry target", RiskLevel.Critical) },
        { 1433, new ServiceInfo("MSSQL", "Microsoft SQL Server - Brute force target", RiskLevel.High) },
        { 1521, new ServiceInfo("Oracle", "Oracle Database", RiskLevel.High) },
        { 3306, new ServiceInfo("MySQL", "MySQL Database - Often unsecured", RiskLevel.High) },
        { 3389, new ServiceInfo("RDP", "Remote Desktop - Brute force target", RiskLevel.High) },
        { 5432, new ServiceInfo("PostgreSQL", "PostgreSQL Database", RiskLevel.Medium) },
        { 5900, new ServiceInfo("VNC", "Virtual Network Computing - Often unsecured", RiskLevel.High) },
        { 5901, new ServiceInfo("VNC", "VNC Display 1", RiskLevel.High) },
        { 6379, new ServiceInfo("Redis", "Redis Database - Often no auth", RiskLevel.Critical) },
        { 8080, new ServiceInfo("HTTP-Alt", "Alternative HTTP - Often admin panels", RiskLevel.Medium) },
        { 8443, new ServiceInfo("HTTPS-Alt", "Alternative HTTPS", RiskLevel.Low) },
        { 27017, new ServiceInfo("MongoDB", "MongoDB - Often no auth", RiskLevel.Critical) }
    };

    public NetworkSecurityService(
        ILogger<NetworkSecurityService> logger,
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IThreatIntelligenceService threatIntelligence,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _threatIntelligence = threatIntelligence;
        _settings = settings.Value;
    }

    public async Task<PortScanResult> ScanDevicePortsAsync(string ipAddress, int[]? portsToScan = null)
    {
        var ports = portsToScan ?? CommonPorts;
        var result = new PortScanResult
        {
            IpAddress = ipAddress,
            ScanStartTime = DateTime.UtcNow
        };

        _logger.LogInformation("Starting port scan on {Ip} for {Count} ports", ipAddress, ports.Length);

        var tasks = ports.Select(async port =>
        {
            var openPort = await ScanPortAsync(ipAddress, port);
            if (openPort != null)
            {
                return openPort;
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        result.OpenPorts = results.Where(r => r != null).Cast<OpenPort>().ToList();
        result.ScanEndTime = DateTime.UtcNow;
        result.TotalPortsScanned = ports.Length;

        // Calculate risk
        result.OverallRisk = CalculateDeviceRisk(result.OpenPorts);

        // Send alert if high risk ports found
        var criticalPorts = result.OpenPorts.Where(p => p.RiskLevel == RiskLevel.Critical).ToList();
        if (criticalPorts.Any())
        {
            await SendSecurityAlertAsync(ipAddress, criticalPorts);
        }

        return result;
    }

    private async Task<OpenPort?> ScanPortAsync(string ipAddress, int port, int timeoutMs = 1000)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ipAddress, port);
            var timeoutTask = Task.Delay(timeoutMs);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask && client.Connected)
            {
                var openPort = new OpenPort
                {
                    Port = port,
                    State = PortState.Open,
                    DetectedAt = DateTime.UtcNow
                };

                if (KnownServices.TryGetValue(port, out var service))
                {
                    openPort.Service = service.Name;
                    openPort.Description = service.Description;
                    openPort.RiskLevel = service.Risk;
                }
                else
                {
                    openPort.Service = "Unknown";
                    openPort.RiskLevel = RiskLevel.Low;
                }

                // Try to grab banner
                try
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 500;
                    var buffer = new byte[256];
                    if (stream.DataAvailable)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        openPort.Banner = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    }
                }
                catch { }

                return openPort;
            }
        }
        catch { }

        return null;
    }

    public async Task<VulnerabilityReport> ScanDeviceVulnerabilitiesAsync(string ipAddress)
    {
        var report = new VulnerabilityReport
        {
            IpAddress = ipAddress,
            ScanTime = DateTime.UtcNow,
            Vulnerabilities = new List<Vulnerability>()
        };

        // Scan ports
        var portScan = await ScanDevicePortsAsync(ipAddress);
        report.OpenPorts = portScan.OpenPorts;

        // Check for specific vulnerabilities
        foreach (var port in portScan.OpenPorts)
        {
            var vulns = CheckPortVulnerabilities(port);
            report.Vulnerabilities.AddRange(vulns);
        }

        // Check IP reputation
        var threatInfo = await _threatIntelligence.CheckIpReputationAsync(ipAddress);
        if (threatInfo != null)
        {
            report.Vulnerabilities.Add(new Vulnerability
            {
                Name = "Suspicious IP Reputation",
                Description = threatInfo.Description,
                Severity = VulnerabilitySeverity.High,
                Recommendation = "Investigate this device's activity"
            });
        }

        // Calculate overall score
        report.SecurityScore = CalculateVulnerabilityScore(report);

        return report;
    }

    private List<Vulnerability> CheckPortVulnerabilities(OpenPort port)
    {
        var vulnerabilities = new List<Vulnerability>();

        switch (port.Port)
        {
            case 23: // Telnet
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "Telnet Enabled",
                    Description = "Telnet transmits data in cleartext, including passwords",
                    Severity = VulnerabilitySeverity.Critical,
                    Recommendation = "Disable Telnet and use SSH instead",
                    CveId = "CVE-Generic-Telnet"
                });
                break;

            case 445: // SMB
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "SMB Exposed",
                    Description = "SMB port exposed - vulnerable to EternalBlue and similar exploits",
                    Severity = VulnerabilitySeverity.High,
                    Recommendation = "Ensure SMB is patched and not exposed to internet",
                    CveId = "CVE-2017-0144"
                });
                break;

            case 3389: // RDP
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "RDP Exposed",
                    Description = "Remote Desktop exposed - target for brute force and BlueKill",
                    Severity = VulnerabilitySeverity.High,
                    Recommendation = "Use VPN or restrict RDP access, enable NLA",
                    CveId = "CVE-2019-0708"
                });
                break;

            case 6379: // Redis
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "Redis Exposed",
                    Description = "Redis often runs without authentication",
                    Severity = VulnerabilitySeverity.Critical,
                    Recommendation = "Enable Redis authentication and bind to localhost"
                });
                break;

            case 27017: // MongoDB
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "MongoDB Exposed",
                    Description = "MongoDB often runs without authentication",
                    Severity = VulnerabilitySeverity.Critical,
                    Recommendation = "Enable MongoDB authentication and restrict access"
                });
                break;

            case 5900:
            case 5901: // VNC
                vulnerabilities.Add(new Vulnerability
                {
                    Name = "VNC Exposed",
                    Description = "VNC may have weak or no password protection",
                    Severity = VulnerabilitySeverity.High,
                    Recommendation = "Use VPN or SSH tunnel for VNC access"
                });
                break;
        }

        return vulnerabilities;
    }

    public async Task<NetworkSecurityReport> GenerateSecurityReportAsync()
    {
        var report = new NetworkSecurityReport
        {
            GeneratedAt = DateTime.UtcNow,
            DeviceReports = new List<DeviceSecurityReport>()
        };

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var devices = await deviceRepo.GetAllAsync();

        foreach (var device in devices.Where(d => !string.IsNullOrEmpty(d.IpAddress)))
        {
            try
            {
                var vulnReport = await ScanDeviceVulnerabilitiesAsync(device.IpAddress!);
                report.DeviceReports.Add(new DeviceSecurityReport
                {
                    DeviceId = device.Id,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    DeviceName = device.Description ?? device.Vendor ?? "Unknown",
                    VulnerabilityReport = vulnReport
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning device {Ip}", device.IpAddress);
            }
        }

        report.OverallScore = CalculateNetworkSecurityScore();
        report.ThreatStats = _threatIntelligence.GetThreatStats();

        return report;
    }

    public async Task<IEnumerable<OpenPort>> GetOpenPortsOnNetworkAsync()
    {
        var allPorts = new List<OpenPort>();

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var devices = await deviceRepo.GetOnlineDevicesAsync();

        foreach (var device in devices.Where(d => !string.IsNullOrEmpty(d.IpAddress)))
        {
            var scanResult = await ScanDevicePortsAsync(device.IpAddress!);
            foreach (var port in scanResult.OpenPorts)
            {
                port.DeviceIp = device.IpAddress;
                port.DeviceMac = device.MacAddress;
                allPorts.Add(port);
            }
        }

        return allPorts.OrderByDescending(p => p.RiskLevel);
    }

    public SecurityScore CalculateNetworkSecurityScore()
    {
        var score = new SecurityScore { MaxScore = 100 };
        var deductions = 0;

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

        // Check for unknown devices
        var unknownDevices = deviceRepo.GetUnknownDevicesAsync().Result;
        deductions += unknownDevices.Count() * 5; // -5 per unknown device
        if (unknownDevices.Any())
            score.Issues.Add($"{unknownDevices.Count()} unknown devices on network");

        // Check unresolved alerts
        var unresolvedAlerts = alertRepo.GetUnreadAsync().Result;
        var criticalAlerts = unresolvedAlerts.Count(a => a.Severity >= AlertSeverity.High);
        deductions += criticalAlerts * 10; // -10 per critical alert
        if (criticalAlerts > 0)
            score.Issues.Add($"{criticalAlerts} unresolved high-severity alerts");

        // Threat intelligence
        var threatStats = _threatIntelligence.GetThreatStats();
        deductions += (int)(threatStats.ThreatsDetected * 2);
        if (threatStats.ThreatsDetected > 0)
            score.Issues.Add($"{threatStats.ThreatsDetected} threats detected");

        // Calculate final score
        score.CurrentScore = Math.Max(0, 100 - deductions);
        score.Grade = score.CurrentScore switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };

        return score;
    }

    private RiskLevel CalculateDeviceRisk(List<OpenPort> openPorts)
    {
        if (openPorts.Any(p => p.RiskLevel == RiskLevel.Critical))
            return RiskLevel.Critical;
        if (openPorts.Any(p => p.RiskLevel == RiskLevel.High))
            return RiskLevel.High;
        if (openPorts.Any(p => p.RiskLevel == RiskLevel.Medium))
            return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private int CalculateVulnerabilityScore(VulnerabilityReport report)
    {
        var score = 100;
        foreach (var vuln in report.Vulnerabilities)
        {
            score -= vuln.Severity switch
            {
                VulnerabilitySeverity.Critical => 25,
                VulnerabilitySeverity.High => 15,
                VulnerabilitySeverity.Medium => 10,
                VulnerabilitySeverity.Low => 5,
                _ => 0
            };
        }
        return Math.Max(0, score);
    }

    private async Task SendSecurityAlertAsync(string ipAddress, List<OpenPort> criticalPorts)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

            var portsStr = string.Join(", ", criticalPorts.Select(p => $"{p.Port}/{p.Service}"));
            var alert = new NetworkAlert
            {
                Type = AlertType.SecurityVulnerability,
                Severity = AlertSeverity.Critical,
                Title = "Critical Security Risk Detected",
                Message = $"Device {ipAddress} has critical ports exposed: {portsStr}",
                SourceIp = ipAddress
            };

            await alertRepo.AddAsync(alert);
            await _notificationService.SendAlertAsync(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending security alert");
        }
    }
}

// Models
public record ServiceInfo(string Name, string Description, RiskLevel Risk);

public class PortScanResult
{
    public string IpAddress { get; set; } = string.Empty;
    public List<OpenPort> OpenPorts { get; set; } = new();
    public DateTime ScanStartTime { get; set; }
    public DateTime ScanEndTime { get; set; }
    public int TotalPortsScanned { get; set; }
    public RiskLevel OverallRisk { get; set; }
}

public class OpenPort
{
    public int Port { get; set; }
    public PortState State { get; set; }
    public string Service { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Banner { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public DateTime DetectedAt { get; set; }
    public string? DeviceIp { get; set; }
    public string? DeviceMac { get; set; }
}

public class VulnerabilityReport
{
    public string IpAddress { get; set; } = string.Empty;
    public DateTime ScanTime { get; set; }
    public List<OpenPort> OpenPorts { get; set; } = new();
    public List<Vulnerability> Vulnerabilities { get; set; } = new();
    public int SecurityScore { get; set; }
}

public class Vulnerability
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public VulnerabilitySeverity Severity { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string? CveId { get; set; }
}

public class NetworkSecurityReport
{
    public DateTime GeneratedAt { get; set; }
    public List<DeviceSecurityReport> DeviceReports { get; set; } = new();
    public SecurityScore OverallScore { get; set; } = new();
    public ThreatStats ThreatStats { get; set; } = new();
}

public class DeviceSecurityReport
{
    public int DeviceId { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public VulnerabilityReport VulnerabilityReport { get; set; } = new();
}

public class SecurityScore
{
    public int CurrentScore { get; set; }
    public int MaxScore { get; set; }
    public string Grade { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}

public enum PortState { Open, Closed, Filtered }
public enum RiskLevel { None, Low, Medium, High, Critical }
public enum VulnerabilitySeverity { Info, Low, Medium, High, Critical }
