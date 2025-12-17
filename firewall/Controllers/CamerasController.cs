using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CamerasController : ControllerBase
{
    private readonly ICameraRepository _cameraRepository;
    private readonly ICameraDetectionService _cameraDetectionService;

    public CamerasController(
        ICameraRepository cameraRepository,
        ICameraDetectionService cameraDetectionService)
    {
        _cameraRepository = cameraRepository;
        _cameraDetectionService = cameraDetectionService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkCamera>>> GetAll()
    {
        var cameras = await _cameraRepository.GetAllAsync();
        return Ok(cameras);
    }

    [HttpGet("online")]
    public async Task<ActionResult<IEnumerable<NetworkCamera>>> GetOnline()
    {
        var cameras = await _cameraRepository.GetOnlineAsync();
        return Ok(cameras);
    }

    [HttpGet("vulnerable")]
    public async Task<ActionResult<IEnumerable<NetworkCamera>>> GetVulnerable()
    {
        var cameras = await _cameraRepository.GetWithDefaultPasswordAsync();
        return Ok(cameras);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<NetworkCamera>> GetById(int id)
    {
        var camera = await _cameraRepository.GetByIdAsync(id);
        if (camera == null) return NotFound();
        return Ok(camera);
    }

    [HttpPost("scan")]
    public async Task<ActionResult<IEnumerable<NetworkCamera>>> ScanCameras()
    {
        var cameras = await _cameraDetectionService.ScanForCamerasAsync();
        return Ok(cameras);
    }

    [HttpPost("{id}/check")]
    public async Task<IActionResult> CheckCamera(int id)
    {
        var camera = await _cameraRepository.GetByIdAsync(id);
        if (camera == null) return NotFound();

        var result = await _cameraDetectionService.CheckCameraAsync(camera.IpAddress, camera.Port);
        if (result != null)
        {
            result.DeviceId = camera.DeviceId;
            await _cameraRepository.AddOrUpdateAsync(result);
            return Ok(result);
        }

        return Ok(new { Message = "Camera check completed, no changes detected" });
    }

    [HttpPost("check")]
    public async Task<ActionResult<NetworkCamera>> CheckNewCamera([FromBody] CameraCheckRequest request)
    {
        var camera = await _cameraDetectionService.CheckCameraAsync(request.IpAddress, request.Port);
        if (camera == null)
        {
            return NotFound(new { Message = "No camera detected at this address" });
        }

        var saved = await _cameraRepository.AddOrUpdateAsync(camera);
        return Ok(saved);
    }

    [HttpPost("{id}/test-credentials")]
    public async Task<IActionResult> TestCredentials(int id, [FromBody] CredentialsRequest request)
    {
        var camera = await _cameraRepository.GetByIdAsync(id);
        if (camera == null) return NotFound();

        var result = await _cameraDetectionService.TestCredentialsAsync(
            camera.IpAddress, camera.Port, request.Username, request.Password);

        return Ok(result);
    }

    [HttpGet("{id}/snapshot")]
    public async Task<IActionResult> GetSnapshot(int id)
    {
        var base64 = await _cameraDetectionService.GetSnapshotAsync(id);
        if (base64 == null)
        {
            return NotFound(new { Message = "Unable to get snapshot" });
        }

        return Ok(new { Image = base64 });
    }

    [HttpGet("{id}/stream-url")]
    public async Task<IActionResult> GetStreamUrl(int id)
    {
        var camera = await _cameraRepository.GetByIdAsync(id);
        if (camera == null) return NotFound();

        // Construire l'URL avec les identifiants si disponibles
        var streamUrl = camera.StreamUrl;
        if (!string.IsNullOrEmpty(camera.DetectedCredentials) && !string.IsNullOrEmpty(streamUrl))
        {
            var uri = new Uri(streamUrl);
            streamUrl = $"{uri.Scheme}://{camera.DetectedCredentials}@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
        }

        return Ok(new { 
            StreamUrl = streamUrl,
            SnapshotUrl = camera.SnapshotUrl,
            RequiresAuth = camera.PasswordStatus == PasswordStatus.PasswordRequired
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _cameraRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }
}

public class CameraCheckRequest
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
}

public class CredentialsRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
