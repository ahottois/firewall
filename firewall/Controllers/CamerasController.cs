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
    private readonly IScanLogService _scanLogService;

    public CamerasController(
        ICameraRepository cameraRepository,
        ICameraDetectionService cameraDetectionService,
        IScanLogService scanLogService)
    {
        _cameraRepository = cameraRepository;
        _cameraDetectionService = cameraDetectionService;
        _scanLogService = scanLogService;
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
    public async Task<ActionResult<IEnumerable<NetworkCamera>>> ScanCameras(CancellationToken cancellationToken)
    {
        var cameras = await _cameraDetectionService.ScanForCamerasAsync(cancellationToken);
        return Ok(cameras);
    }

    /// <summary>
    /// Stream des logs de scan en temps réel via SSE
    /// </summary>
    [HttpGet("scan/logs/stream")]
    public async Task StreamScanLogs(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Send existing recent logs first
        var existingLogs = _scanLogService.GetRecentLogs("camera-scan", 50);
        foreach (var log in existingLogs.Reverse())
        {
            await WriteLogEvent(log);
        }

        // Subscribe to new logs
        var tcs = new TaskCompletionSource<bool>();
        
        void OnLogEntry(object? sender, ScanLogEntry entry)
        {
            if (entry.Source == "camera-scan")
            {
                _ = WriteLogEvent(entry);
            }
        }

        _scanLogService.LogEntryAdded += OnLogEntry;

        try
        {
            // Keep connection alive
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
                await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(": keepalive\n\n"), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _scanLogService.LogEntryAdded -= OnLogEntry;
        }
    }

    private async Task WriteLogEvent(ScanLogEntry entry)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = entry.Id,
                message = entry.Message,
                level = entry.Level.ToString().ToLower(),
                timestamp = entry.Timestamp,
                progress = entry.Progress != null ? new { entry.Progress.Current, entry.Progress.Total, entry.Progress.Percentage } : null
            });
            
            var data = $"event: log\ndata: {json}\n\n";
            await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(data));
            await Response.Body.FlushAsync();
        }
        catch
        {
            // Ignore write errors (client disconnected)
        }
    }

    /// <summary>
    /// Récupérer les logs de scan récents
    /// </summary>
    [HttpGet("scan/logs")]
    public IActionResult GetScanLogs([FromQuery] int count = 100)
    {
        var logs = _scanLogService.GetRecentLogs("camera-scan", count);
        return Ok(logs);
    }

    /// <summary>
    /// Effacer les logs de scan
    /// </summary>
    [HttpDelete("scan/logs")]
    public IActionResult ClearScanLogs()
    {
        _scanLogService.ClearLogs("camera-scan");
        return Ok(new { message = "Logs effacés" });
    }

    [HttpPost("{id}/check")]
    public async Task<IActionResult> CheckCamera(int id)
    {
        var camera = await _cameraRepository.GetByIdAsync(id);
        if (camera == null) return NotFound();

        _scanLogService.Log("camera-scan", $"?? Vérification manuelle de {camera.IpAddress}:{camera.Port}", ScanLogLevel.Info);

        var result = await _cameraDetectionService.CheckCameraAsync(camera.IpAddress, camera.Port);
        if (result != null)
        {
            result.DeviceId = camera.DeviceId;
            await _cameraRepository.AddOrUpdateAsync(result);
            
            _scanLogService.Log("camera-scan", $"? Vérification terminée pour {camera.IpAddress}:{camera.Port}", ScanLogLevel.Success);
            return Ok(result);
        }

        _scanLogService.Log("camera-scan", $"?? Aucun changement détecté pour {camera.IpAddress}:{camera.Port}", ScanLogLevel.Info);
        return Ok(new { Message = "Camera check completed, no changes detected" });
    }

    [HttpPost("check")]
    public async Task<ActionResult<NetworkCamera>> CheckNewCamera([FromBody] CameraCheckRequest request)
    {
        _scanLogService.Log("camera-scan", $"?? Vérification de nouvelle caméra: {request.IpAddress}:{request.Port}", ScanLogLevel.Info);

        var camera = await _cameraDetectionService.CheckCameraAsync(request.IpAddress, request.Port);
        if (camera == null)
        {
            _scanLogService.Log("camera-scan", $"? Aucune caméra détectée à {request.IpAddress}:{request.Port}", ScanLogLevel.Warning);
            return NotFound(new { Message = "No camera detected at this address" });
        }

        var saved = await _cameraRepository.AddOrUpdateAsync(camera);
        _scanLogService.Log("camera-scan", $"? Caméra ajoutée: {saved.Manufacturer ?? "Inconnue"} à {request.IpAddress}:{request.Port}", ScanLogLevel.Success);
        
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
