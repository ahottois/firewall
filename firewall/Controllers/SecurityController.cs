using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecurityController : ControllerBase
{
    private readonly ILogger<SecurityController> _logger;
    private readonly IThreatIntelligenceService _threatIntelligence;
    private readonly INetworkSecurityService _securityService;
    private readonly IBandwidthMonitorService _bandwidthMonitor;

    public SecurityController(
        ILogger<SecurityController> logger,
        IThreatIntelligenceService threatIntelligence,
        INetworkSecurityService securityService,
        IBandwidthMonitorService bandwidthMonitor)
    {
        _logger = logger;
        _threatIntelligence = threatIntelligence;
        _securityService = securityService;
        _bandwidthMonitor = bandwidthMonitor;
    }

    // Threat Intelligence
    [HttpGet("threats/stats")]
    public IActionResult GetThreatStats()
    {
        var stats = _threatIntelligence.GetThreatStats();
        return Ok(stats);
    }

    [HttpGet("threats/recent")]
    public IActionResult GetRecentThreats([FromQuery] int count = 50)
    {
        var threats = _threatIntelligence.GetRecentThreats(count);
        return Ok(threats);
    }

    [HttpGet("threats/check-ip/{ip}")]
    public async Task<IActionResult> CheckIpReputation(string ip)
    {
        var result = await _threatIntelligence.CheckIpReputationAsync(ip);
        return Ok(new { 
            IsMalicious = result != null,
            ThreatInfo = result
        });
    }

    [HttpPost("threats/update-feeds")]
    public async Task<IActionResult> UpdateThreatFeeds()
    {
        await _threatIntelligence.UpdateThreatFeedsAsync();
        return Ok(new { Success = true, Message = "Threat feeds updated" });
    }

    // Security Scanning
    [HttpGet("score")]
    public IActionResult GetSecurityScore()
    {
        var score = _securityService.CalculateNetworkSecurityScore();
        return Ok(score);
    }

    [HttpPost("scan/device/{ip}")]
    public async Task<IActionResult> ScanDevice(string ip)
    {
        _logger.LogInformation("Starting security scan for {Ip}", ip);
        var result = await _securityService.ScanDevicePortsAsync(ip);
        return Ok(result);
    }

    [HttpPost("scan/vulnerabilities/{ip}")]
    public async Task<IActionResult> ScanVulnerabilities(string ip)
    {
        _logger.LogInformation("Starting vulnerability scan for {Ip}", ip);
        var result = await _securityService.ScanDeviceVulnerabilitiesAsync(ip);
        return Ok(result);
    }

    [HttpGet("scan/open-ports")]
    public async Task<IActionResult> GetOpenPortsOnNetwork()
    {
        var ports = await _securityService.GetOpenPortsOnNetworkAsync();
        return Ok(ports);
    }

    [HttpPost("report/generate")]
    public async Task<IActionResult> GenerateSecurityReport()
    {
        _logger.LogInformation("Generating security report...");
        var report = await _securityService.GenerateSecurityReportAsync();
        return Ok(report);
    }

    // Bandwidth Monitoring
    [HttpGet("bandwidth/summary")]
    public IActionResult GetBandwidthSummary()
    {
        var summary = _bandwidthMonitor.GetNetworkSummary();
        return Ok(summary);
    }

    [HttpGet("bandwidth/top-consumers")]
    public IActionResult GetTopConsumers([FromQuery] int count = 10)
    {
        var consumers = _bandwidthMonitor.GetTopConsumers(count);
        return Ok(consumers);
    }

    [HttpGet("bandwidth/device/{mac}")]
    public IActionResult GetDeviceBandwidth(string mac)
    {
        var bandwidth = _bandwidthMonitor.GetDeviceBandwidth(mac.ToUpperInvariant().Replace("-", ":"));
        return Ok(bandwidth);
    }

    [HttpGet("bandwidth/all")]
    public IActionResult GetAllDevicesBandwidth()
    {
        var all = _bandwidthMonitor.GetAllDevicesBandwidth();
        return Ok(all);
    }

    // Dashboard Summary
    [HttpGet("dashboard")]
    public IActionResult GetSecurityDashboard()
    {
        var score = _securityService.CalculateNetworkSecurityScore();
        var threatStats = _threatIntelligence.GetThreatStats();
        var bandwidthSummary = _bandwidthMonitor.GetNetworkSummary();
        var recentThreats = _threatIntelligence.GetRecentThreats(10);

        return Ok(new SecurityDashboard
        {
            SecurityScore = score,
            ThreatStats = threatStats,
            BandwidthSummary = bandwidthSummary,
            RecentThreats = recentThreats.ToList(),
            GeneratedAt = DateTime.UtcNow
        });
    }
}

public class SecurityDashboard
{
    public SecurityScore SecurityScore { get; set; } = new();
    public ThreatStats ThreatStats { get; set; } = new();
    public NetworkBandwidthSummary BandwidthSummary { get; set; } = new();
    public List<ThreatEvent> RecentThreats { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
