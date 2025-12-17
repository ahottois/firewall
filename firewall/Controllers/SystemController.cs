using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IPacketCaptureService _packetCapture;
    private readonly IDeviceDiscoveryService _deviceDiscovery;

    public SystemController(
        IPacketCaptureService packetCapture,
        IDeviceDiscoveryService deviceDiscovery)
    {
        _packetCapture = packetCapture;
        _deviceDiscovery = deviceDiscovery;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            IsCapturing = _packetCapture.IsCapturing,
            CurrentInterface = _packetCapture.CurrentInterface,
            ServerTime = DateTime.UtcNow,
            Version = "1.0.0"
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
}
