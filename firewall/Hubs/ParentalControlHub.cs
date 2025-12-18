using Microsoft.AspNetCore.SignalR;
using NetworkFirewall.Models;

namespace NetworkFirewall.Hubs;

/// <summary>
/// Hub SignalR pour les notifications temps réel du contrôle parental
/// </summary>
public class ParentalControlHub : Hub
{
    private readonly ILogger<ParentalControlHub> _logger;

    public ParentalControlHub(ILogger<ParentalControlHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected to ParentalControlHub: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected from ParentalControlHub: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// S'abonner aux mises à jour d'un profil spécifique
    /// </summary>
    public async Task SubscribeToProfile(int profileId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"profile-{profileId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to profile {ProfileId}", Context.ConnectionId, profileId);
    }

    /// <summary>
    /// Se désabonner d'un profil
    /// </summary>
    public async Task UnsubscribeFromProfile(int profileId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"profile-{profileId}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from profile {ProfileId}", Context.ConnectionId, profileId);
    }

    /// <summary>
    /// Demander une mise à jour de statut immédiate pour un profil
    /// </summary>
    public async Task RequestStatusUpdate(int profileId)
    {
        // Le service ParentalControlService enverra la réponse
        await Clients.Caller.SendAsync("StatusUpdateRequested", profileId);
    }
}

/// <summary>
/// Extensions pour simplifier l'envoi de notifications depuis le service
/// </summary>
public static class ParentalControlHubExtensions
{
    /// <summary>
    /// Notifie tous les clients d'un changement de statut de profil
    /// </summary>
    public static async Task NotifyProfileStatusChanged(
        this IHubContext<ParentalControlHub> hubContext,
        ProfileStatus status)
    {
        await hubContext.Clients.All.SendAsync("ProfileStatusChanged", status);
        await hubContext.Clients.Group($"profile-{status.ProfileId}").SendAsync("ProfileStatusChanged", status);
    }

    /// <summary>
    /// Notifie tous les clients qu'un profil a été créé
    /// </summary>
    public static async Task NotifyProfileCreated(
        this IHubContext<ParentalControlHub> hubContext,
        ProfileStatus status)
    {
        await hubContext.Clients.All.SendAsync("ProfileCreated", status);
    }

    /// <summary>
    /// Notifie tous les clients qu'un profil a été mis à jour
    /// </summary>
    public static async Task NotifyProfileUpdated(
        this IHubContext<ParentalControlHub> hubContext,
        ProfileStatus status)
    {
        await hubContext.Clients.All.SendAsync("ProfileUpdated", status);
        await hubContext.Clients.Group($"profile-{status.ProfileId}").SendAsync("ProfileUpdated", status);
    }

    /// <summary>
    /// Notifie tous les clients qu'un profil a été supprimé
    /// </summary>
    public static async Task NotifyProfileDeleted(
        this IHubContext<ParentalControlHub> hubContext,
        int profileId)
    {
        await hubContext.Clients.All.SendAsync("ProfileDeleted", profileId);
        await hubContext.Clients.Group($"profile-{profileId}").SendAsync("ProfileDeleted", profileId);
    }

    /// <summary>
    /// Notifie d'un blocage automatique (pour afficher une alerte)
    /// </summary>
    public static async Task NotifyAutoBlock(
        this IHubContext<ParentalControlHub> hubContext,
        int profileId,
        string profileName,
        string reason,
        string deviceMac)
    {
        var notification = new
        {
            ProfileId = profileId,
            ProfileName = profileName,
            Reason = reason,
            DeviceMac = deviceMac,
            Timestamp = DateTime.UtcNow
        };

        await hubContext.Clients.All.SendAsync("AutoBlockTriggered", notification);
    }

    /// <summary>
    /// Notifie d'un déblocage (fin de restriction)
    /// </summary>
    public static async Task NotifyAutoUnblock(
        this IHubContext<ParentalControlHub> hubContext,
        int profileId,
        string profileName)
    {
        var notification = new
        {
            ProfileId = profileId,
            ProfileName = profileName,
            Timestamp = DateTime.UtcNow
        };

        await hubContext.Clients.All.SendAsync("AutoUnblockTriggered", notification);
    }

    /// <summary>
    /// Notifie d'une alerte de temps restant (ex: 10 minutes restantes)
    /// </summary>
    public static async Task NotifyTimeWarning(
        this IHubContext<ParentalControlHub> hubContext,
        int profileId,
        string profileName,
        int remainingMinutes)
    {
        var notification = new
        {
            ProfileId = profileId,
            ProfileName = profileName,
            RemainingMinutes = remainingMinutes,
            Timestamp = DateTime.UtcNow
        };

        await hubContext.Clients.All.SendAsync("TimeWarning", notification);
        await hubContext.Clients.Group($"profile-{profileId}").SendAsync("TimeWarning", notification);
    }
}
