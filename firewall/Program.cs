using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Controllers;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

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

// HTTP Client Factory for camera detection
builder.Services.AddHttpClient();

// Services (Singleton pour maintenir l'état)
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IPacketCaptureService, PacketCaptureService>();
builder.Services.AddSingleton<IDeviceDiscoveryService, DeviceDiscoveryService>();
builder.Services.AddSingleton<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddSingleton<ICameraDetectionService, CameraDetectionService>();

// Background Service
builder.Services.AddHostedService<NetworkMonitorService>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// CORS pour le développement
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

// Message de démarrage
Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║   🛡️  NetGuard - Network Firewall Monitor                ║
║                                                           ║
╠═══════════════════════════════════════════════════════════╣
║                                                           ║
║   Web Interface: http://localhost:{0,-5}                 ║
║                                                           ║
║   Features:                                               ║
║   • Device Discovery & Tracking                          ║
║   • Real-time Packet Analysis                            ║
║   • Anomaly Detection (Port Scan, ARP Spoofing)          ║
║   • Camera Detection & Password Check                    ║
║   • Live Notifications                                   ║
║                                                           ║
║   Note: Run with sudo/admin for packet capture           ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
", appSettings.WebPort);

app.Run();
