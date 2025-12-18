using Microsoft.AspNetCore.SignalR;
using NetworkFirewall.Models;

namespace NetworkFirewall.Hubs;

/// <summary>
/// Hub SignalR pour les alertes et logs de sécurité en temps réel
/// </summary>
public class AlertHub : Hub
{
    private readonly ILogger<AlertHub> _logger;

    public AlertHub(ILogger<AlertHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connecté à l'AlertHub: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client déconnecté de l'AlertHub: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// S'abonner à un type d'alerte spécifique
    /// </summary>
    public async Task SubscribeToSeverity(string severity)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"severity_{severity}");
        _logger.LogDebug("Client {ConnectionId} abonné aux alertes {Severity}", Context.ConnectionId, severity);
    }

    /// <summary>
    /// Se désabonner d'un type d'alerte
    /// </summary>
    public async Task UnsubscribeFromSeverity(string severity)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"severity_{severity}");
    }
}

/// <summary>
/// Interface pour envoyer des notifications via l'AlertHub
/// </summary>
public interface IAlertHubNotifier
{
    /// <summary>
    /// Envoie une nouvelle alerte à tous les clients connectés
    /// </summary>
    Task NotifyNewAlertAsync(NetworkAlert alert);

    /// <summary>
    /// Envoie un nouveau log de sécurité à tous les clients connectés
    /// </summary>
    Task NotifyNewSecurityLogAsync(SecurityLogDto log);

    /// <summary>
    /// Notifie les clients qu'une alerte a été lue
    /// </summary>
    Task NotifyAlertReadAsync(int alertId);

    /// <summary>
    /// Notifie les clients qu'une alerte a été résolue
    /// </summary>
    Task NotifyAlertResolvedAsync(int alertId);

    /// <summary>
    /// Notifie les clients que toutes les alertes ont été lues
    /// </summary>
    Task NotifyAllAlertsReadAsync();

    /// <summary>
    /// Notifie les clients que toutes les alertes ont été résolues
    /// </summary>
    Task NotifyAllAlertsResolvedAsync();

    /// <summary>
    /// Notifie les clients d'un événement de blocage
    /// </summary>
    Task NotifyBlockEventAsync(BlockEventDto blockEvent);

    /// <summary>
    /// Envoie les statistiques mises à jour
    /// </summary>
    Task NotifyStatsUpdateAsync(object stats);

    /// <summary>
    /// Notifie les clients qu'un reset a été effectué
    /// </summary>
    Task NotifyResetAsync();
}

/// <summary>
/// Implémentation du notificateur AlertHub
/// </summary>
public class AlertHubNotifier : IAlertHubNotifier
{
    private readonly IHubContext<AlertHub> _hubContext;
    private readonly ILogger<AlertHubNotifier> _logger;

    public AlertHubNotifier(IHubContext<AlertHub> hubContext, ILogger<AlertHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyNewAlertAsync(NetworkAlert alert)
    {
        var dto = new AlertDto
        {
            Id = alert.Id,
            Type = alert.Type.ToString(),
            Severity = alert.Severity.ToString(),
            Title = alert.Title,
            Message = alert.Message,
            SourceMac = alert.SourceMac,
            SourceIp = alert.SourceIp,
            DestinationIp = alert.DestinationIp,
            DestinationPort = alert.DestinationPort,
            Timestamp = alert.Timestamp,
            IsRead = alert.IsRead
        };

        _logger.LogDebug("Envoi alerte SignalR: {Title}", alert.Title);
        await _hubContext.Clients.All.SendAsync("NewAlert", dto);
        
        // Envoyer aussi au groupe de sévérité correspondant
        await _hubContext.Clients.Group($"severity_{alert.Severity}")
            .SendAsync("NewAlertBySeverity", dto);
    }

    public async Task NotifyNewSecurityLogAsync(SecurityLogDto log)
    {
        _logger.LogDebug("Envoi log sécurité SignalR: {Message}", log.Message);
        await _hubContext.Clients.All.SendAsync("NewSecurityLog", log);
    }

    public async Task NotifyAlertReadAsync(int alertId)
    {
        await _hubContext.Clients.All.SendAsync("AlertRead", alertId);
    }

    public async Task NotifyAlertResolvedAsync(int alertId)
    {
        await _hubContext.Clients.All.SendAsync("AlertResolved", alertId);
    }

    public async Task NotifyAllAlertsReadAsync()
    {
        await _hubContext.Clients.All.SendAsync("AllAlertsRead");
    }

    public async Task NotifyAllAlertsResolvedAsync()
    {
        await _hubContext.Clients.All.SendAsync("AllAlertsResolved");
    }

    public async Task NotifyBlockEventAsync(BlockEventDto blockEvent)
    {
        _logger.LogDebug("Envoi événement blocage SignalR: {Mac} -> {Ip}", blockEvent.SourceMac, blockEvent.DestinationIp);
        await _hubContext.Clients.All.SendAsync("BlockEvent", blockEvent);
    }

    public async Task NotifyStatsUpdateAsync(object stats)
    {
        await _hubContext.Clients.All.SendAsync("StatsUpdate", stats);
    }

    public async Task NotifyResetAsync()
    {
        await _hubContext.Clients.All.SendAsync("LogsReset");
    }
}

/// <summary>
/// DTO pour les alertes (SignalR)
/// </summary>
public class AlertDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceMac { get; set; }
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>
/// DTO pour les événements de blocage
/// </summary>
public class BlockEventDto
{
    public DateTime Timestamp { get; set; }
    public string SourceMac { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? DestinationIp { get; set; }
    public int? DestinationPort { get; set; }
    public string? Protocol { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public int PacketCount { get; set; } = 1;
}
