using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrafficController : ControllerBase
{
    private readonly ITrafficLogRepository _trafficRepository;

    public TrafficController(ITrafficLogRepository trafficRepository)
    {
        _trafficRepository = trafficRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int count = 100)
    {
        var logs = await _trafficRepository.GetRecentAsync(count);
        return Ok(logs);
    }

    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetByDevice(int deviceId, [FromQuery] int count = 100)
    {
        var logs = await _trafficRepository.GetByDeviceAsync(deviceId, count);
        return Ok(logs);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int hours = 1)
    {
        var stats = await _trafficRepository.GetStatsAsync(TimeSpan.FromHours(hours));
        return Ok(stats);
    }
}
