using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrafficController : ControllerBase
{
    private readonly ITrafficLogRepository _trafficRepository;
    private readonly ITrafficLoggingService _trafficLogging;

    public TrafficController(
        ITrafficLogRepository trafficRepository,
        ITrafficLoggingService trafficLogging)
    {
        _trafficRepository = trafficRepository;
        _trafficLogging = trafficLogging;
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
        // For short periods, use real-time stats
        if (hours <= 1)
        {
            var realTimeStats = _trafficLogging.GetRealTimeStats();
            return Ok(realTimeStats);
        }
        
        // For longer periods, query database
        var stats = await _trafficRepository.GetStatsAsync(TimeSpan.FromHours(hours));
        return Ok(stats);
    }

    [HttpGet("stats/realtime")]
    public IActionResult GetRealTimeStats()
    {
        var stats = _trafficLogging.GetRealTimeStats();
        return Ok(stats);
    }

    [HttpPost("flush")]
    public async Task<IActionResult> FlushLogs()
    {
        await _trafficLogging.FlushAsync();
        return Ok(new { Success = true, Message = "Traffic logs flushed to database" });
    }
}
