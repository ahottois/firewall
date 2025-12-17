using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController(
    IAlertRepository alertRepository,
    INotificationService notificationService,
    IDeviceDiscoveryService deviceDiscovery,
    IAnomalyDetectionService anomalyDetection) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetRecent([FromQuery] int count = 50)
    {
        var alerts = await alertRepository.GetRecentAsync(count);
        return Ok(alerts);
    }

    [HttpGet("unread")]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetUnread()
    {
        var alerts = await alertRepository.GetUnreadAsync();
        return Ok(alerts);
    }

    [HttpGet("unread/count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var count = await alertRepository.GetUnreadCountAsync();
        return Ok(count);
    }

    [HttpGet("device/{deviceId}")]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetByDevice(int deviceId)
    {
        var alerts = await alertRepository.GetByDeviceAsync(deviceId);
        return Ok(alerts);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var result = await alertRepository.MarkAsReadAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await alertRepository.MarkAllAsReadAsync();
        return Ok();
    }

    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(int id)
    {
        var result = await alertRepository.ResolveAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Résoudre une alerte et la supprimer de la liste
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await alertRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Résoudre toutes les alertes (les marquer comme résolues et lues)
    /// </summary>
    [HttpPost("resolve-all")]
    public async Task<IActionResult> ResolveAll()
    {
        await alertRepository.ResolveAllAsync();
        return Ok(new { message = "Toutes les alertes ont été résolues" });
    }

    /// <summary>
    /// Supprimer toutes les alertes
    /// </summary>
    [HttpDelete("all")]
    public async Task<IActionResult> DeleteAll()
    {
        await alertRepository.DeleteAllAsync();
        return Ok(new { message = "Toutes les alertes ont été supprimées" });
    }

    /// <summary>
    /// Réinitialiser: Effacer toutes les alertes, les cooldowns, et relancer les checks
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        // 1. Supprimer toutes les alertes
        await alertRepository.DeleteAllAsync();

        // 2. Clear notification cooldowns
        notificationService.ClearNotifications();

        // 3. Reset anomaly detection trackers
        anomalyDetection.Reset();

        // 4. Relancer un scan réseau
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await deviceDiscovery.ScanNetworkAsync();
        });

        return Ok(new { 
            message = "Réinitialisation effectuée. Un nouveau scan réseau a été lancé.",
            actions = new[] 
            {
                "Alertes supprimées",
                "Cooldowns de notifications réinitialisés",
                "Détection d'anomalies réinitialisée",
                "Scan réseau relancé"
            }
        });
    }

    /// <summary>
    /// Obtenir les statistiques des alertes
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var alerts = await alertRepository.GetRecentAsync(1000);
        var alertList = alerts.ToList();

        var stats = new
        {
            Total = alertList.Count,
            Unread = alertList.Count(a => !a.IsRead),
            Resolved = alertList.Count(a => a.IsResolved),
            BySeverity = alertList.GroupBy(a => a.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ByType = alertList.GroupBy(a => a.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            Last24Hours = alertList.Count(a => a.Timestamp > DateTime.UtcNow.AddHours(-24)),
            NotificationStats = notificationService.GetStats()
        };

        return Ok(stats);
    }
}
