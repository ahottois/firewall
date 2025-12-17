using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DnsController : ControllerBase
{
    private readonly DnsServerService _dnsServer; // Injected as concrete type or interface if extracted
    private readonly IDnsBlocklistService _blocklistService;
    private readonly AppSettings _settings;

    // Note: DnsServerService is registered as HostedService, so we need to retrieve it carefully or register it as Singleton too.
    // Best practice: Register as Singleton interface, then add as HostedService using the interface.
    public DnsController(
        IEnumerable<IHostedService> hostedServices,
        IDnsBlocklistService blocklistService,
        IOptions<AppSettings> settings)
    {
        _dnsServer = hostedServices.OfType<DnsServerService>().FirstOrDefault() 
                     ?? throw new InvalidOperationException("DNS Server not found");
        _blocklistService = blocklistService;
        _settings = settings.Value;
    }

    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int count = 100)
    {
        return Ok(_dnsServer.GetRecentLogs(count));
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        return Ok(new
        {
            TotalBlockedDomains = _blocklistService.TotalBlockedDomains,
            CategoryStats = _blocklistService.GetStats(),
            Settings = _settings.Dns
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshLists()
    {
        await _blocklistService.RefreshListsAsync();
        return Ok(new { Message = "Blocklists refreshed" });
    }
}
