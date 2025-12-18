using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public class DeviceHeartbeatService : BackgroundService
{
    private readonly ILogger<DeviceHeartbeatService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public DeviceHeartbeatService(
        ILogger<DeviceHeartbeatService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceHeartbeatService démarré - intervalle: {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDevicesStatusAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du heartbeat des appareils");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckDevicesStatusAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        var devices = await deviceRepo.GetAllAsync();
        var devicesWithIp = devices.Where(d => !string.IsNullOrEmpty(d.IpAddress)).ToList();

        _logger.LogDebug("Vérification du statut de {Count} appareils", devicesWithIp.Count);

        var tasks = devicesWithIp.Select(async device =>
        {
            var isOnline = await PingDeviceAsync(device.IpAddress!, ct);
            return (device, isOnline);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (device, isOnline) in results)
        {
            var newStatus = isOnline ? DeviceStatus.Online : DeviceStatus.Offline;
            
            if (device.Status != newStatus)
            {
                _logger.LogInformation("Appareil {Mac} ({Ip}): {OldStatus} -> {NewStatus}",
                    device.MacAddress, device.IpAddress, device.Status, newStatus);
                    
                await deviceRepo.UpdateStatusAsync(device.Id, newStatus);
            }
        }
    }

    private static async Task<bool> PingDeviceAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 2000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
