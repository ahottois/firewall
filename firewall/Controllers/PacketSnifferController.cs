using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SnifferController : ControllerBase
{
    private readonly IPacketSnifferService _snifferService;

    public SnifferController(IPacketSnifferService snifferService)
    {
        _snifferService = snifferService;
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] SnifferFilter? filter)
    {
        _snifferService.StartSniffing(filter);
        return Ok(new { Message = "Packet sniffing started", Filter = filter });
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        _snifferService.StopSniffing();
        return Ok(new { Message = "Packet sniffing stopped" });
    }

    [HttpGet("packets")]
    public IActionResult GetPackets([FromQuery] int limit = 100)
    {
        var packets = _snifferService.GetPackets(limit);
        return Ok(packets);
    }

    [HttpDelete("packets")]
    public IActionResult ClearPackets()
    {
        _snifferService.ClearPackets();
        return Ok(new { Message = "Packet buffer cleared" });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new 
        { 
            IsSniffing = _snifferService.IsSniffing,
            CurrentFilter = _snifferService.CurrentFilter
        });
    }
}
