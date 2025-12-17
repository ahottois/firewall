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

// Agent Services
builder.Services.AddSingleton<IAgentService, AgentService>();

// Pi-hole Services
builder.Services.AddSingleton<IPiholeService, PiholeService>();

// Router Services
builder.Services.AddSingleton<PortMappingService>();
builder.Services.AddHostedService<PortMappingService>(provider => provider.GetRequiredService<PortMappingService>());

// Packet Sniffer Service
builder.Services.AddSingleton<IPacketSnifferService, PacketSnifferService>();

// Add Controllers
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapNotificationEndpoints();

// Initialize Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Try to create database if it doesn't exist
        db.Database.EnsureCreated();
        
        // Test access to ensure schema matches the current model
        // This will throw if the schema is outdated (e.g. missing columns)
        _ = db.Alerts.FirstOrDefault();
        _ = db.Agents.FirstOrDefault(); // Check for Agents table
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Database schema mismatch detected. Recreating database to apply new 'Real' models...");
        try
        {
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            logger.LogInformation("Database recreated successfully.");
        }
        catch (Exception retryEx)
        {
            logger.LogError(retryEx, "Failed to recreate database.");
        }
    }
}

app.Run();

























