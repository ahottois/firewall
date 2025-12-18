using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;
using NetworkFirewall.Hubs;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly ISecurityLogRepository _logRepository;
    private readonly IAlertHubNotifier _alertHubNotifier;
    private readonly ISecurityLogService _securityLogService;
    private readonly ILogger<LogsController> _logger;

    public LogsController(
        ISecurityLogRepository logRepository,
        IAlertHubNotifier alertHubNotifier,
        ISecurityLogService securityLogService,
        ILogger<LogsController> logger)
    {
        _logRepository = logRepository;
        _alertHubNotifier = alertHubNotifier;
        _securityLogService = securityLogService;
        _logger = logger;
    }

    /// <summary>
    /// Récupérer les logs de sécurité récents
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SecurityLogDto>>> GetRecent([FromQuery] int count = 100)
    {
        var logs = await _logRepository.GetRecentAsync(count);
        var dtos = logs.Select(SecurityLogDto.FromEntity);
        return Ok(dtos);
    }

    /// <summary>
    /// Récupérer les logs non lus
    /// </summary>
    [HttpGet("unread")]
    public async Task<ActionResult<IEnumerable<SecurityLogDto>>> GetUnread()
    {
        var logs = await _logRepository.GetUnreadAsync();
        var dtos = logs.Select(SecurityLogDto.FromEntity);
        return Ok(dtos);
    }

    /// <summary>
    /// Récupérer le nombre de logs non lus
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var count = await _logRepository.GetUnreadCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// Récupérer les logs par sévérité
    /// </summary>
    [HttpGet("severity/{severity}")]
    public async Task<ActionResult<IEnumerable<SecurityLogDto>>> GetBySeverity(string severity, [FromQuery] int count = 50)
    {
        if (!Enum.TryParse<LogSeverity>(severity, true, out var sev))
            return BadRequest(new { message = "Sévérité invalide" });

        var logs = await _logRepository.GetBySeverityAsync(sev, count);
        var dtos = logs.Select(SecurityLogDto.FromEntity);
        return Ok(dtos);
    }

    /// <summary>
    /// Récupérer les logs par catégorie
    /// </summary>
    [HttpGet("category/{category}")]
    public async Task<ActionResult<IEnumerable<SecurityLogDto>>> GetByCategory(string category, [FromQuery] int count = 50)
    {
        if (!Enum.TryParse<LogCategory>(category, true, out var cat))
            return BadRequest(new { message = "Catégorie invalide" });

        var logs = await _logRepository.GetByCategoryAsync(cat, count);
        var dtos = logs.Select(SecurityLogDto.FromEntity);
        return Ok(dtos);
    }

    /// <summary>
    /// Récupérer les logs d'un appareil spécifique
    /// </summary>
    [HttpGet("device/{deviceId}")]
    public async Task<ActionResult<IEnumerable<SecurityLogDto>>> GetByDevice(int deviceId, [FromQuery] int count = 50)
    {
        var logs = await _logRepository.GetByDeviceAsync(deviceId, count);
        var dtos = logs.Select(SecurityLogDto.FromEntity);
        return Ok(dtos);
    }

    /// <summary>
    /// Marquer un log comme lu
    /// </summary>
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var result = await _logRepository.MarkAsReadAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Marquer tous les logs comme lus
    /// </summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _logRepository.MarkAllAsReadAsync();
        _logger.LogInformation("Tous les logs ont été marqués comme lus");
        return Ok(new { message = "Tous les logs ont été marqués comme lus" });
    }

    /// <summary>
    /// Archiver un log (le résoudre)
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(int id)
    {
        var result = await _logRepository.ArchiveAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Archiver tous les logs (tout résoudre)
    /// </summary>
    [HttpPost("archive-all")]
    public async Task<IActionResult> ArchiveAll()
    {
        await _logRepository.ArchiveAllAsync();
        _logger.LogInformation("Tous les logs ont été archivés");
        return Ok(new { message = "Tous les logs ont été archivés" });
    }

    /// <summary>
    /// Supprimer un log
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _logRepository.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// Supprimer tous les logs
    /// </summary>
    [HttpDelete("all")]
    public async Task<IActionResult> DeleteAll()
    {
        await _logRepository.DeleteAllAsync();
        _logger.LogInformation("Tous les logs ont été supprimés");
        return Ok(new { message = "Tous les logs ont été supprimés" });
    }

    /// <summary>
    /// Supprimer uniquement les logs archivés
    /// </summary>
    [HttpDelete("archived")]
    public async Task<IActionResult> DeleteArchived()
    {
        await _logRepository.DeleteArchivedAsync();
        return Ok(new { message = "Les logs archivés ont été supprimés" });
    }

    /// <summary>
    /// Réinitialiser les logs et relancer le monitoring
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        await _logRepository.DeleteAllAsync();
        
        // Notifier les clients
        await _alertHubNotifier.NotifyResetAsync();
        
        // Logger l'événement système
        await _securityLogService.LogSystemEventAsync("Réinitialisation des logs de sécurité", LogSeverity.Info);

        _logger.LogInformation("Réinitialisation des logs de sécurité");

        return Ok(new { 
            message = "Logs réinitialisés. Le service de monitoring continue de fonctionner.",
            actions = new[] 
            {
                "Tous les logs ont été supprimés",
                "Le monitoring des blocages est actif"
            }
        });
    }

    /// <summary>
    /// Obtenir les statistiques des logs
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _logRepository.GetStatsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Obtenir les catégories disponibles
    /// </summary>
    [HttpGet("categories")]
    public IActionResult GetCategories()
    {
        var categories = Enum.GetNames<LogCategory>();
        return Ok(categories);
    }

    /// <summary>
    /// Obtenir les niveaux de sévérité disponibles
    /// </summary>
    [HttpGet("severities")]
    public IActionResult GetSeverities()
    {
        var severities = Enum.GetNames<LogSeverity>();
        return Ok(severities);
    }

    /// <summary>
    /// Créer un log de test (pour le développement)
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> CreateTestLog([FromBody] CreateTestLogRequest? request)
    {
        var severity = request?.Severity ?? LogSeverity.Warning;
        var message = request?.Message ?? "Log de test créé manuellement";

        await _securityLogService.LogAsync(new SecurityLog
        {
            Severity = severity,
            Category = LogCategory.SystemEvent,
            ActionTaken = "Test",
            Message = message,
            SourceIp = request?.SourceIp ?? "127.0.0.1",
            SourceMac = request?.SourceMac
        });

        return Ok(new { message = "Log de test créé" });
    }
}

public record CreateTestLogRequest(
    LogSeverity? Severity,
    string? Message,
    string? SourceIp,
    string? SourceMac
);
