using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

/// <summary>
/// ????? Arrr! API de monitoring réseau - Pour surveiller les sept mers numériques!
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly ILogger<MonitoringController> _logger;
    private readonly INetworkMonitoringService _monitoringService;
    private readonly ITrafficLoggingService _trafficLogging;
    private readonly IBandwidthMonitorService _bandwidthMonitor;

    public MonitoringController(
        ILogger<MonitoringController> logger,
        INetworkMonitoringService monitoringService,
        ITrafficLoggingService trafficLogging,
        IBandwidthMonitorService bandwidthMonitor)
    {
        _logger = logger;
        _monitoringService = monitoringService;
        _trafficLogging = trafficLogging;
        _bandwidthMonitor = bandwidthMonitor;
    }

    /// <summary>
    /// ????? Get overall network health status
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetNetworkHealth()
    {
        var health = _monitoringService.GetNetworkHealth();
        return Ok(health);
    }

    /// <summary>
    /// ????? Get live network metrics (packets/sec, bytes/sec, etc.)
    /// </summary>
    [HttpGet("live")]
    public IActionResult GetLiveMetrics()
    {
        var metrics = _monitoringService.GetLiveMetrics();
        var trafficStats = _trafficLogging.GetRealTimeStats();
        
        return Ok(new
        {
            Live = metrics,
            Traffic = trafficStats,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// ????? Get active network connections
    /// </summary>
    [HttpGet("connections")]
    public IActionResult GetActiveConnections()
    {
        var connections = _monitoringService.GetActiveConnections();
        return Ok(connections);
    }

    /// <summary>
    /// ????? Get protocol breakdown statistics
    /// </summary>
    [HttpGet("protocols")]
    public IActionResult GetProtocolStats()
    {
        var protocols = _monitoringService.GetProtocolBreakdown();
        var total = protocols.Sum(p => p.PacketCount);
        
        var withPercentages = protocols.Select(p => new
        {
            p.Protocol,
            p.PacketCount,
            p.ByteCount,
            p.LastSeen,
            Percentage = total > 0 ? Math.Round((double)p.PacketCount / total * 100, 2) : 0
        });
        
        return Ok(withPercentages);
    }

    /// <summary>
    /// ????? Get hourly traffic statistics
    /// </summary>
    [HttpGet("hourly")]
    public IActionResult GetHourlyTraffic([FromQuery] int hours = 24)
    {
        var traffic = _monitoringService.GetHourlyTraffic(Math.Min(hours, 168)); // Max 1 week
        return Ok(traffic);
    }

    /// <summary>
    /// ????? Get top network talkers (bandwidth hogs)
    /// </summary>
    [HttpGet("top-talkers")]
    public IActionResult GetTopTalkers([FromQuery] int count = 10)
    {
        var talkers = _monitoringService.GetTopTalkers(Math.Min(count, 100));
        return Ok(talkers);
    }

    /// <summary>
    /// ????? Get suspicious network activities
    /// </summary>
    [HttpGet("suspicious")]
    public IActionResult GetSuspiciousActivities([FromQuery] int count = 50)
    {
        var activities = _monitoringService.GetSuspiciousActivities(Math.Min(count, 500));
        return Ok(activities);
    }

    /// <summary>
    /// ????? Get geo traffic statistics (internal vs external)
    /// </summary>
    [HttpGet("geo")]
    public IActionResult GetGeoStats()
    {
        var geo = _monitoringService.GetGeoTrafficStats();
        return Ok(geo);
    }

    /// <summary>
    /// ????? Get complete monitoring dashboard data
    /// </summary>
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        var health = _monitoringService.GetNetworkHealth();
        var liveMetrics = _monitoringService.GetLiveMetrics();
        var protocols = _monitoringService.GetProtocolBreakdown().Take(5);
        var topTalkers = _monitoringService.GetTopTalkers(5);
        var suspicious = _monitoringService.GetSuspiciousActivities(10);
        var geo = _monitoringService.GetGeoTrafficStats();
        var bandwidth = _bandwidthMonitor.GetNetworkSummary();

        return Ok(new MonitoringDashboard
        {
            Health = health,
            LiveMetrics = liveMetrics,
            TopProtocols = protocols.ToList(),
            TopTalkers = topTalkers.ToList(),
            RecentSuspicious = suspicious.ToList(),
            GeoStats = geo,
            Bandwidth = bandwidth,
            GeneratedAt = DateTime.UtcNow,
            PirateMessage = GetPirateMessage(health.Score)
        });
    }

    /// <summary>
    /// ????? Get traffic heatmap data for visualization
    /// </summary>
    [HttpGet("heatmap")]
    public IActionResult GetTrafficHeatmap()
    {
        var hourly = _monitoringService.GetHourlyTraffic(24);
        
        var heatmap = hourly.Select(h => new
        {
            Hour = h.Hour,
            Date = h.Date.ToString("yyyy-MM-dd"),
            Intensity = CalculateIntensity(h.ByteCount),
            Packets = h.PacketCount,
            Bytes = h.ByteCount,
            Devices = h.DeviceCount
        });

        return Ok(heatmap);
    }

    /// <summary>
    /// ????? Export monitoring data as JSON
    /// </summary>
    [HttpGet("export")]
    public IActionResult ExportData()
    {
        var data = new
        {
            ExportTime = DateTime.UtcNow,
            Health = _monitoringService.GetNetworkHealth(),
            Connections = _monitoringService.GetActiveConnections(),
            Protocols = _monitoringService.GetProtocolBreakdown(),
            HourlyTraffic = _monitoringService.GetHourlyTraffic(24),
            TopTalkers = _monitoringService.GetTopTalkers(20),
            SuspiciousActivities = _monitoringService.GetSuspiciousActivities(100),
            GeoStats = _monitoringService.GetGeoTrafficStats()
        };

        return Ok(data);
    }

    private static string GetPirateMessage(int score)
    {
        return score switch
        {
            >= 90 => "????? Arrr! The seas be calm, me hearty! Smooth sailing ahead!",
            >= 70 => "? Ahoy! Minor squalls detected, but nothing this crew can't handle!",
            >= 50 => "?? Batten down the hatches! Rough waters be approaching!",
            >= 30 => "?? Shiver me timbers! A storm be brewin' on the horizon!",
            _ => "?? All hands on deck! We be under attack, ye scallywags!"
        };
    }

    private static int CalculateIntensity(long bytes)
    {
        // Scale to 0-100 for heatmap
        return bytes switch
        {
            0 => 0,
            < 1_000_000 => 20,      // < 1 MB
            < 10_000_000 => 40,     // < 10 MB
            < 100_000_000 => 60,    // < 100 MB
            < 1_000_000_000 => 80,  // < 1 GB
            _ => 100
        };
    }
}

public class MonitoringDashboard
{
    public NetworkHealthStatus Health { get; set; } = new();
    public LiveNetworkMetrics LiveMetrics { get; set; } = new();
    public List<ProtocolStats> TopProtocols { get; set; } = new();
    public List<TopTalker> TopTalkers { get; set; } = new();
    public List<SuspiciousActivity> RecentSuspicious { get; set; } = new();
    public GeoTrafficStats GeoStats { get; set; } = new();
    public NetworkBandwidthSummary Bandwidth { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public string PirateMessage { get; set; } = string.Empty;
}
