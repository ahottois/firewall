using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alertRepository;

    public AlertsController(IAlertRepository alertRepository)
    {
        _alertRepository = alertRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetRecent([FromQuery] int count = 50)
    {
        var alerts = await _alertRepository.GetRecentAsync(count);
        return Ok(alerts);
    }

    [HttpGet("unread")]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetUnread()
    {
        var alerts = await _alertRepository.GetUnreadAsync();
        return Ok(alerts);
    }

    [HttpGet("unread/count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var count = await _alertRepository.GetUnreadCountAsync();
        return Ok(count);
    }

    [HttpGet("device/{deviceId}")]
    public async Task<ActionResult<IEnumerable<NetworkAlert>>> GetByDevice(int deviceId)
    {
        var alerts = await _alertRepository.GetByDeviceAsync(deviceId);
        return Ok(alerts);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var result = await _alertRepository.MarkAsReadAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _alertRepository.MarkAllAsReadAsync();
        return Ok();
    }

    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(int id)
    {
        var result = await _alertRepository.ResolveAsync(id);
        if (!result) return NotFound();
        return Ok();
    }
}
