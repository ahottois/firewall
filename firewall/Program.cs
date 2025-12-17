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

// Packet Sniffer Service
builder.Services.AddSingleton<IPacketSnifferService, PacketSnifferService>();

// 🏴‍☠️ Monitoring Service - For watching the digital seas!
builder.Services.AddSingleton<INetworkMonitoringService, NetworkMonitoringService>();

// Background Service
builder.Services.AddHostedService<NetworkMonitorService>();
builder.Services.AddHostedService<NetworkSecurityService>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// CORS pour le developpement
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Middleware
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapNotificationEndpoints();

// Fallback pour SPA
app.MapFallbackToFile("index.html");

// 🏴‍☠️ Pirate-themed startup message
Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════╗
║                                                                ║
║   🏴‍☠️  NetGuard - Network Firewall Monitor  🏴‍☠️                ║
║                                                                ║
╠════════════════════════════════════════════════════════════════╣
║                                                                ║
║   ⚓ Web Interface: http://localhost:{0,-5}                   ║
║                                                                ║
║   🗡️  Features:                                                ║
║   • Device Discovery & Tracking                                ║
║   • Real-time Packet Analysis                                  ║
║   • Anomaly Detection (Port Scan, ARP Spoofing)               ║
║   • Camera Detection & Password Check                          ║
║   • Live Notifications                                         ║
║   • Traffic Logging & Monitoring                               ║
║   • Threat Intelligence                                        ║
║   • Security Scanning                                          ║
║   • Bandwidth Monitoring                                       ║
║   • 🏴‍☠️ Network Health Dashboard                               ║
║                                                                ║
║   ⚠️  Note: Run with sudo/admin for packet capture             ║
║                                                                ║
║   🏴‍☠️ Arrr! Ready to patrol the digital seas, Captain!         ║
║                                                                ║
╚════════════════════════════════════════════════════════════════╝
", appSettings.WebPort);

app.Run();
