using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;
using NetworkFirewall.Services;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ILogger<SettingsController> _logger;
    private readonly AppSettings _settings;
    private readonly IPacketCaptureService _packetCaptureService;

    public SettingsController(
        ILogger<SettingsController> logger,
        IOptions<AppSettings> settings,
        IPacketCaptureService packetCaptureService)
    {
        _logger = logger;
        _settings = settings.Value;
        _packetCaptureService = packetCaptureService;
    }

    /// <summary>
    /// Obtenir les informations système (endpoint simplifié pour le frontend)
    /// </summary>
    [HttpGet("system")]
    public IActionResult GetSystem()
    {
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeStr = uptime.Days > 0 
            ? $"{uptime.Days}j {uptime.Hours}h {uptime.Minutes}m"
            : $"{uptime.Hours}h {uptime.Minutes}m";

        return Ok(new
        {
            os = RuntimeInformation.OSDescription,
            hostname = Environment.MachineName,
            uptime = uptimeStr,
            cpuCores = Environment.ProcessorCount,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            dotnetVersion = RuntimeInformation.FrameworkDescription
        });
    }

    /// <summary>
    /// Obtenir les informations système
    /// </summary>
    [HttpGet("system-info")]
    public IActionResult GetSystemInfo()
    {
        var info = new SystemInfo
        {
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            DotnetVersion = RuntimeInformation.FrameworkDescription,
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            Is64BitProcess = Environment.Is64BitProcess,
            TotalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            WorkingDirectory = Environment.CurrentDirectory,
            StartTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()
        };

        return Ok(info);
    }

    /// <summary>
    /// Obtenir la liste des interfaces réseau
    /// </summary>
    [HttpGet("interfaces")]
    public IActionResult GetNetworkInterfaces()
    {
        var interfaces = new List<NetworkInterfaceDto>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipProps = ni.GetIPProperties();
            var ipv4Address = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                .Address.ToString();

            interfaces.Add(new NetworkInterfaceDto
            {
                Name = ni.Name,
                Description = ni.Description,
                Type = ni.NetworkInterfaceType.ToString(),
                MacAddress = ni.GetPhysicalAddress().ToString(),
                IpAddress = ipv4Address,
                IsUp = ni.OperationalStatus == OperationalStatus.Up,
                Speed = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0 // Mbps
            });
        }

        return Ok(interfaces.OrderByDescending(i => i.IsUp));
    }

    /// <summary>
    /// Obtenir les interfaces disponibles pour la capture de paquets
    /// </summary>
    [HttpGet("capture-interfaces")]
    public IActionResult GetCaptureInterfaces()
    {
        var interfaces = _packetCaptureService.GetAvailableInterfaces();
        return Ok(interfaces);
    }

    /// <summary>
    /// Obtenir la configuration actuelle
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        // Ne pas exposer les clés API sensibles
        return Ok(new
        {
            _settings.WebPort,
            _settings.NetworkInterface,
            _settings.EnablePacketCapture,
            _settings.EnableThreatFeeds,
            _settings.AlertRetentionDays,
            _settings.TrafficLogRetentionDays,
            _settings.SuspiciousPorts,
            _settings.EnableAutoSecurityScan,
            _settings.PortScanTimeWindowSeconds,
            _settings.PortScanThreshold
        });
    }

    /// <summary>
    /// Obtenir le statut de la capture de paquets
    /// </summary>
    [HttpGet("capture/status")]
    public IActionResult GetCaptureStatus()
    {
        return Ok(new
        {
            IsCapturing = _packetCaptureService.IsCapturing,
            CurrentInterface = _packetCaptureService.CurrentInterface
        });
    }

    /// <summary>
    /// Obtenir la version de l'application
    /// </summary>
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var buildDate = System.IO.File.GetLastWriteTime(assembly.Location);

        return Ok(new
        {
            Version = version?.ToString() ?? "1.0.0",
            BuildDate = buildDate
        });
    }
}

public class SystemInfo
{
    public string MachineName { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string OsArchitecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string DotnetVersion { get; set; } = string.Empty;
    public bool Is64BitOperatingSystem { get; set; }
    public bool Is64BitProcess { get; set; }
    public long TotalMemoryMb { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
}

public class NetworkInterfaceDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool IsUp { get; set; }
    public long Speed { get; set; } // Mbps
}
