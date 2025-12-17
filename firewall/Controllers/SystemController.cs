using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IPacketCaptureService _packetCapture;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly INotificationService _notificationService;

    public SystemController(
        IPacketCaptureService packetCapture,
        IDeviceDiscoveryService deviceDiscovery,
        INotificationService notificationService)
    {
        _packetCapture = packetCapture;
        _deviceDiscovery = deviceDiscovery;
        _notificationService = notificationService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var notifStats = _notificationService.GetStats();
        
        return Ok(new
        {
            IsCapturing = _packetCapture.IsCapturing,
            CurrentInterface = _packetCapture.CurrentInterface,
            ServerTime = DateTime.UtcNow,
            Version = "1.0.0",
            Notifications = new
            {
                notifStats.TotalAlerts,
                notifStats.SentAlerts,
                notifStats.SuppressedAlerts,
                notifStats.ActiveCooldowns,
                SuppressionRate = notifStats.TotalAlerts > 0 
                    ? Math.Round((double)notifStats.SuppressedAlerts / notifStats.TotalAlerts * 100, 1) 
                    : 0
            }
        });
    }

    [HttpGet("interfaces")]
    public IActionResult GetInterfaces()
    {
        var interfaces = _packetCapture.GetAvailableInterfaces();
        return Ok(interfaces);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanNetwork()
    {
        await _deviceDiscovery.ScanNetworkAsync();
        return Ok(new { Message = "Network scan initiated" });
    }

    [HttpGet("notifications/stats")]
    public IActionResult GetNotificationStats()
    {
        var stats = _notificationService.GetStats();
        return Ok(stats);
    }

    [HttpPost("notifications/clear")]
    public IActionResult ClearNotifications()
    {
        _notificationService.ClearNotifications();
        return Ok(new { Message = "Notifications and cooldowns cleared" });
    }
}
