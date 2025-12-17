using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RouterController : ControllerBase
{
    private readonly PortMappingService _portMappingService;
    private readonly IPacketCaptureService _packetCaptureService;
    private readonly AppSettings _settings;

    public RouterController(
        IEnumerable<IHostedService> hostedServices,
        IPacketCaptureService packetCaptureService,
        IOptions<AppSettings> settings)
    {
        _portMappingService = hostedServices.OfType<PortMappingService>().FirstOrDefault() 
                              ?? throw new InvalidOperationException("Port Mapping Service not found");
        _packetCaptureService = packetCaptureService;
        _settings = settings.Value;
    }

    [HttpGet("mappings")]
    public IActionResult GetMappings()
    {
        return Ok(_settings.Router.PortMappings);
    }

    [HttpPost("mappings")]
    public IActionResult AddMapping([FromBody] PortMappingRule rule)
    {
        if (string.IsNullOrEmpty(rule.Id)) rule.Id = Guid.NewGuid().ToString();
        
        _settings.Router.PortMappings.Add(rule);
        if (rule.Enabled)
        {
            _portMappingService.StartMapping(rule);
        }
        
        return Ok(rule);
    }

    [HttpDelete("mappings/{id}")]
    public async Task<IActionResult> DeleteMapping(string id)
    {
        var rule = _settings.Router.PortMappings.FirstOrDefault(r => r.Id == id);
        if (rule == null) return NotFound();

        await _portMappingService.StopMappingAsync(id);
        _settings.Router.PortMappings.Remove(rule);
        
        return Ok();
    }

    [HttpGet("interfaces")]
    public IActionResult GetInterfaces()
    {
        return Ok(_packetCaptureService.GetAvailableInterfaces());
    }
}
