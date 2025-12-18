using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(IAgentService agentService, ILogger<AgentsController> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeat heartbeat)
    {
        try
        {
            await _agentService.ProcessHeartbeatAsync(heartbeat);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAgents()
    {
        var agents = await _agentService.GetAllAgentsAsync();
        return Ok(agents);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(int id)
    {
        await _agentService.DeleteAgentAsync(id);
        return Ok();
    }

    [HttpGet("install-script")]
    public IActionResult GetInstallCommand([FromQuery] string os)
    {
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        string command;

        if (string.Equals(os, "linux", StringComparison.OrdinalIgnoreCase))
        {
            command = $"curl -s {serverUrl}/api/agents/install/linux | sudo bash";
        }
        else if (string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
        {
            command = $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm {serverUrl}/api/agents/install/windows | iex\"";
        }
        else
        {
            return BadRequest("Unknown OS. Supported: linux, windows");
        }

        return Ok(new { command });
    }

    [HttpGet("install/{platform}")]
    public IActionResult GetInstallScript(string platform)
    {
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        
        if (platform.Equals("linux", StringComparison.OrdinalIgnoreCase))
        {
            var script = _agentService.GenerateLinuxInstallScript(serverUrl);
            return Content(script, "text/x-shellscript");
        }
        else if (platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            var script = _agentService.GenerateWindowsInstallScript(serverUrl);
            return Content(script, "text/plain");
        }
        
        return BadRequest("Unknown platform. Supported: linux, windows");
    }
}
