using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Controllers;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Configurer le port web
builder.WebHost.UseUrls($"http://0.0.0.0:{appSettings.WebPort}");

// Database
builder.Services.AddDbContext<FirewallDbContext>(options =>
    options.UseSqlite($"Data Source={appSettings.DatabasePath}"));

// Repositories
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IAlertRepository, AlertRepository>();
builder.Services.AddScoped<ITrafficLogRepository, TrafficLogRepository>();
builder.Services.AddScoped<ICameraRepository, CameraRepository>();

// HTTP Client Factory for camera detection and threat intelligence
builder.Services.AddHttpClient();

// Services (Singleton pour maintenir l'etat)
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IPacketCaptureService, PacketCaptureService>();
builder.Services.AddSingleton<IDeviceDiscoveryService, DeviceDiscoveryService>();
builder.Services.AddSingleton<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddSingleton<IBandwidthMonitorService, BandwidthMonitorService>();
builder.Services.AddSingleton<INetworkMonitoringService, NetworkMonitoringService>();
builder.Services.AddSingleton<ITrafficLoggingService, TrafficLoggingService>();
builder.Services.AddSingleton<ICameraDetectionService, CameraDetectionService>();
builder.Services.AddSingleton<IScanSessionService, ScanSessionService>();

// Scan Log Service - for real-time scan logging
builder.Services.AddSingleton<IScanLogService, ScanLogService>();

// Security Services
builder.Services.AddSingleton<IThreatIntelligenceService, ThreatIntelligenceService>();
builder.Services.AddSingleton<INetworkSecurityService, NetworkSecurityService>();

// DNS Services
builder.Services.AddSingleton<IDnsBlocklistService, DnsBlocklistService>();
builder.Services.AddSingleton<DnsServerService>(); // Register as singleton for controller access
builder.Services.AddHostedService<DnsServerService>(provider => provider.GetRequiredService<DnsServerService>());

// Router Services
builder.Services.AddSingleton<PortMappingService>();
builder.Services.AddHostedService<PortMappingService>(provider => provider.GetRequiredService<PortMappingService>());

```




