using System.Collections.Concurrent;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INotificationService
{
    event EventHandler<NetworkAlert>? AlertReceived;
    Task SendAlertAsync(NetworkAlert alert);
    IEnumerable<NetworkAlert> GetRecentNotifications(int count = 20);
    void ClearNotifications();
}

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly ConcurrentQueue<NetworkAlert> _recentAlerts = new();
    private const int MaxRecentAlerts = 100;

    public event EventHandler<NetworkAlert>? AlertReceived;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendAlertAsync(NetworkAlert alert)
    {
        // Ajouter à la file des alertes récentes
        _recentAlerts.Enqueue(alert);
        
        // Limiter la taille de la file
        while (_recentAlerts.Count > MaxRecentAlerts)
        {
            _recentAlerts.TryDequeue(out _);
        }

        // Déclencher l'événement pour les clients WebSocket/SSE
        AlertReceived?.Invoke(this, alert);

        // Log avec couleur selon la sévérité
        LogAlert(alert);

        return Task.CompletedTask;
    }

    public IEnumerable<NetworkAlert> GetRecentNotifications(int count = 20)
    {
        return _recentAlerts.Reverse().Take(count);
    }

    public void ClearNotifications()
    {
        while (_recentAlerts.TryDequeue(out _)) { }
    }

    private void LogAlert(NetworkAlert alert)
    {
        var emoji = alert.Severity switch
        {
            AlertSeverity.Critical => "??",
            AlertSeverity.High => "??",
            AlertSeverity.Medium => "?",
            AlertSeverity.Low => "??",
            _ => "??"
        };

        _logger.LogWarning("{Emoji} [{Severity}] {Type}: {Message}",
            emoji, alert.Severity, alert.Type, alert.Message);
    }
}
