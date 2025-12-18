using Microsoft.AspNetCore.SignalR;
using NetworkFirewall.Models;

namespace NetworkFirewall.Hubs;

/// <summary>
/// Hub SignalR pour la mise à jour en temps réel des appareils réseau
/// </summary>
public class DeviceHub : Hub
{
    private readonly ILogger<DeviceHub> _logger;

    public DeviceHub(ILogger<DeviceHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connecté au DeviceHub: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client déconnecté du DeviceHub: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Permet à un client de s'abonner aux mises à jour d'un appareil spécifique
    /// </summary>
    public async Task SubscribeToDevice(string macAddress)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device_{macAddress}");
        _logger.LogDebug("Client {ConnectionId} abonné à l'appareil {Mac}", Context.ConnectionId, macAddress);
    }

    /// <summary>
    /// Permet à un client de se désabonner des mises à jour d'un appareil
    /// </summary>
    public async Task UnsubscribeFromDevice(string macAddress)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"device_{macAddress}");
    }
}

/// <summary>
/// Interface pour envoyer des notifications aux clients SignalR
/// </summary>
public interface IDeviceHubNotifier
{
    Task NotifyDeviceDiscovered(NetworkDevice device);
    Task NotifyDeviceUpdated(NetworkDevice device);
    Task NotifyDeviceStatusChanged(NetworkDevice device);
    Task NotifyDeviceBlocked(NetworkDevice device);
    Task NotifyDeviceUnblocked(NetworkDevice device);
    Task NotifyScanProgress(int scanned, int total, int found);
    Task NotifyScanComplete(int totalDevices);
}

/// <summary>
/// Implémentation du notificateur SignalR pour les appareils
/// </summary>
public class DeviceHubNotifier : IDeviceHubNotifier
{
    private readonly IHubContext<DeviceHub> _hubContext;
    private readonly ILogger<DeviceHubNotifier> _logger;

    public DeviceHubNotifier(IHubContext<DeviceHub> hubContext, ILogger<DeviceHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyDeviceDiscovered(NetworkDevice device)
    {
        _logger.LogDebug("Notification: Nouvel appareil découvert {Mac}", device.MacAddress);
        await _hubContext.Clients.All.SendAsync("DeviceDiscovered", MapToDto(device));
    }

    public async Task NotifyDeviceUpdated(NetworkDevice device)
    {
        _logger.LogDebug("Notification: Appareil mis à jour {Mac}", device.MacAddress);
        await _hubContext.Clients.All.SendAsync("DeviceUpdated", MapToDto(device));
    }

    public async Task NotifyDeviceStatusChanged(NetworkDevice device)
    {
        _logger.LogDebug("Notification: Statut changé pour {Mac} -> {Status}", device.MacAddress, device.Status);
        await _hubContext.Clients.All.SendAsync("DeviceStatusChanged", MapToDto(device));
    }

    public async Task NotifyDeviceBlocked(NetworkDevice device)
    {
        _logger.LogInformation("Notification: Appareil bloqué {Mac}", device.MacAddress);
        await _hubContext.Clients.All.SendAsync("DeviceBlocked", MapToDto(device));
    }

    public async Task NotifyDeviceUnblocked(NetworkDevice device)
    {
        _logger.LogInformation("Notification: Appareil débloqué {Mac}", device.MacAddress);
        await _hubContext.Clients.All.SendAsync("DeviceUnblocked", MapToDto(device));
    }

    public async Task NotifyScanProgress(int scanned, int total, int found)
    {
        await _hubContext.Clients.All.SendAsync("ScanProgress", new { scanned, total, found });
    }

    public async Task NotifyScanComplete(int totalDevices)
    {
        await _hubContext.Clients.All.SendAsync("ScanComplete", new { totalDevices });
    }

    private static DeviceDto MapToDto(NetworkDevice device) => new()
    {
        Id = device.Id,
        MacAddress = device.MacAddress,
        IpAddress = device.IpAddress,
        Hostname = device.Hostname,
        Vendor = device.Vendor,
        Description = device.Description,
        Status = device.Status,
        IsKnown = device.IsKnown,
        IsTrusted = device.IsTrusted,
        FirstSeen = device.FirstSeen,
        LastSeen = device.LastSeen
    };
}

/// <summary>
/// DTO pour transférer les données d'appareil via SignalR
/// </summary>
public class DeviceDto
{
    public int Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public string? Description { get; set; }
    public DeviceStatus Status { get; set; }
    public bool IsKnown { get; set; }
    public bool IsTrusted { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}
