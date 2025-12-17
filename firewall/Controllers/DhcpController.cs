using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DhcpController : ControllerBase
{
    private readonly IDhcpService _dhcpService;

    public DhcpController(IDhcpService dhcpService)
    {
        _dhcpService = dhcpService;
    }

    [HttpGet("config")]
    public ActionResult<DhcpConfig> GetConfig()
    {
        return Ok(_dhcpService.GetConfig());
    }

    [HttpPost("config")]
    public IActionResult UpdateConfig([FromBody] DhcpConfig config)
    {
        _dhcpService.UpdateConfig(config);
        return Ok();
    }

    [HttpGet("leases")]
    public ActionResult<IEnumerable<DhcpLease>> GetLeases()
    {
        return Ok(_dhcpService.GetLeases());
    }
}
