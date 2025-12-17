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
        // Ajouter a la file des alertes recentes
        _recentAlerts.Enqueue(alert);
        
        // Limiter la taille de la file
        while (_recentAlerts.Count > MaxRecentAlerts)
        {
            _recentAlerts.TryDequeue(out _);
        }

        // Declencher l'evenement pour les clients WebSocket/SSE
        AlertReceived?.Invoke(this, alert);

        // Log selon la severite
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
        var prefix = alert.Severity switch
        {
            AlertSeverity.Critical => "[CRITICAL]",
            AlertSeverity.High => "[HIGH]",
            AlertSeverity.Medium => "[MEDIUM]",
            AlertSeverity.Low => "[LOW]",
            _ => "[INFO]"
        };

        _logger.LogWarning("{Prefix} [{Severity}] {Type}: {Message}",
            prefix, alert.Severity, alert.Type, alert.Message);
    }
}
