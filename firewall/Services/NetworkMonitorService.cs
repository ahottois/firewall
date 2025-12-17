using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// Background service that coordinates packet capture and analysis
/// </summary>
public class NetworkMonitorService : BackgroundService
{
    private readonly ILogger<NetworkMonitorService> _logger;
    private readonly IPacketCaptureService _packetCapture;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly IAnomalyDetectionService _anomalyDetection;
    private readonly ITrafficLoggingService _trafficLogging;
    private readonly IThreatIntelligenceService _threatIntelligence;
    private readonly IBandwidthMonitorService _bandwidthMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppSettings _settings;

    public NetworkMonitorService(
        ILogger<NetworkMonitorService> logger,
        IPacketCaptureService packetCapture,
        IDeviceDiscoveryService deviceDiscovery,
        IAnomalyDetectionService anomalyDetection,
        ITrafficLoggingService trafficLogging,
        IThreatIntelligenceService threatIntelligence,
        IBandwidthMonitorService bandwidthMonitor,
        IServiceScopeFactory scopeFactory,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _packetCapture = packetCapture;
        _deviceDiscovery = deviceDiscovery;
        _anomalyDetection = anomalyDetection;
        _trafficLogging = trafficLogging;
        _threatIntelligence = threatIntelligence;
        _bandwidthMonitor = bandwidthMonitor;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Network Monitor Service starting...");

        // Subscribe to packet capture events
        _packetCapture.PacketCaptured += OnPacketCaptured;

        // Subscribe to device discovery events
        _deviceDiscovery.UnknownDeviceDetected += OnUnknownDeviceDetected;

        try
        {
            // Initialize database
            await InitializeDatabaseAsync();

            // Update threat feeds on startup
            if (_settings.EnableThreatFeeds)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000, stoppingToken);
                    await _threatIntelligence.UpdateThreatFeedsAsync();
                }, stoppingToken);
            }

            // Start packet capture
            if (_settings.EnablePacketCapture)
            {
                await _packetCapture.StartAsync(stoppingToken);
            }

            // Scan network on startup
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000, stoppingToken);
                await _deviceDiscovery.ScanNetworkAsync();
            }, stoppingToken);

            // Main loop - periodic tasks
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                await PerformMaintenanceAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Network Monitor Service stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network Monitor Service error");
        }
        finally
        {
            await _packetCapture.StopAsync();
            await _trafficLogging.FlushAsync();
            _packetCapture.PacketCaptured -= OnPacketCaptured;
            _deviceDiscovery.UnknownDeviceDetected -= OnUnknownDeviceDetected;
        }
    }

    private async void OnPacketCaptured(object? sender, PacketCapturedEventArgs e)
    {
        try
        {
            // Log traffic (non-blocking, uses queue)
            _trafficLogging.LogPacket(e);

            // Track bandwidth
            var isInbound = IsInbound(e.DestinationIp);
            _bandwidthMonitor.RecordTraffic(e.SourceMac, e.SourceIp, e.PacketSize, isInbound);

            // Check threat intelligence for external IPs
            if (!string.IsNullOrEmpty(e.SourceIp) && !IsLocalIp(e.SourceIp))
            {
                var threat = await _threatIntelligence.CheckIpReputationAsync(e.SourceIp);
                // Alerts are handled by ThreatIntelligenceService
            }

            // Process device discovery
            await _deviceDiscovery.ProcessPacketAsync(e);

            // Analyze for anomalies
            await _anomalyDetection.AnalyzePacketAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing packet");
        }
    }

    private bool IsInbound(string? destinationIp)
    {
        if (string.IsNullOrEmpty(destinationIp)) return false;
        return IsLocalIp(destinationIp);
    }

    private bool IsLocalIp(string ip)
    {
        return ip.StartsWith("192.168.") ||
               ip.StartsWith("10.") ||
               ip.StartsWith("172.16.") ||
               ip.StartsWith("172.17.") ||
               ip.StartsWith("172.18.") ||
               ip.StartsWith("172.19.") ||
               ip.StartsWith("172.2") ||
               ip.StartsWith("172.30.") ||
               ip.StartsWith("172.31.") ||
               ip == "127.0.0.1";
    }

    private async void OnUnknownDeviceDetected(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var alert = new NetworkAlert
            {
                Type = e.IsNew ? AlertType.NewDevice : AlertType.UnknownDevice,
                Severity = AlertSeverity.Medium,
                Title = e.IsNew ? "New Device Detected" : "Unknown Device Active",
                Message = $"Device detected: MAC={e.Device.MacAddress}, IP={e.Device.IpAddress}, Vendor={e.Device.Vendor ?? "Unknown"}",
                SourceMac = e.Device.MacAddress,
                SourceIp = e.Device.IpAddress,
                DeviceId = e.Device.Id
            };

            await alertRepo.AddAsync(alert);
            await notificationService.SendAlertAsync(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling unknown device");
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        
        _logger.LogInformation("Ensuring database is created...");
        await context.Database.EnsureCreatedAsync();
        _logger.LogInformation("Database ready");
    }

    private async Task PerformMaintenanceAsync()
    {
        _logger.LogInformation("Performing maintenance tasks...");

        try
        {
            // Flush pending logs
            await _trafficLogging.FlushAsync();

            // Update threat feeds periodically
            if (_settings.EnableThreatFeeds)
            {
                await _threatIntelligence.UpdateThreatFeedsAsync();
            }

            // Check bandwidth alerts
            await _bandwidthMonitor.CheckBandwidthAlertsAsync();

            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var trafficRepo = scope.ServiceProvider.GetRequiredService<ITrafficLogRepository>();

            await alertRepo.CleanupOldAlertsAsync(_settings.AlertRetentionDays);
            await trafficRepo.CleanupOldLogsAsync(_settings.TrafficLogRetentionDays);

            _logger.LogInformation("Maintenance completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maintenance error");
        }
    }
}
