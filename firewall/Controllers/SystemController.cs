using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IPacketCaptureService _packetCapture;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SystemController> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    public SystemController(
        IPacketCaptureService packetCapture,
        IDeviceDiscoveryService deviceDiscovery,
        INotificationService notificationService,
        ILogger<SystemController> logger,
        IHostApplicationLifetime appLifetime)
    {
        _packetCapture = packetCapture;
        _deviceDiscovery = deviceDiscovery;
        _notificationService = notificationService;
        _logger = logger;
        _appLifetime = appLifetime;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var notifStats = _notificationService.GetStats();
        
        var process = Process.GetCurrentProcess();
        var memoryUsage = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

        return Ok(new
        {
            IsCapturing = _packetCapture.IsCapturing,
            CurrentInterface = _packetCapture.CurrentInterface,
            ServerTime = DateTime.UtcNow,
            Version = "1.0.0",
            MemoryUsageMb = memoryUsage,
            Uptime = uptime.ToString(@"dd\.hh\:mm\:ss"),
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            ProcessId = process.Id,
            Notifications = new
            {
                notifStats.TotalAlerts,
                notifStats.SentAlerts,
                notifStats.SuppressedAlerts,
                notifStats.ActiveCooldowns,
                SuppressionRate = notifStats.TotalAlerts > 0 
                    ? Math.Round((double)notifStats.SuppressedAlerts / notifStats.TotalAlerts * 100, 1) 
                    : 0
            }
        });
    }

    /// <summary>
    /// Obtenir le statut du service systemd (Linux uniquement)
    /// </summary>
    [HttpGet("service/status")]
    public async Task<IActionResult> GetServiceStatus()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Ok(new ServiceStatus
            {
                IsRunning = true,
                Status = "Running (Non-Daemon)",
                IsEnabled = false,
                IsInstalled = false,
                Message = "Mode développement - pas de service systemd sur Windows"
            });
        }

        try
        {
            // Vérifier si le service est installé
            var isInstalled = System.IO.File.Exists("/etc/systemd/system/webguard.service");
            
            if (!isInstalled)
            {
                return Ok(new ServiceStatus
                {
                    IsRunning = true,
                    Status = "Running (Standalone)",
                    IsEnabled = false,
                    IsInstalled = false,
                    Message = "L'application s'exécute mais n'est pas installée comme service"
                });
            }

            // Obtenir le statut du service
            var statusResult = await RunCommandAsync("systemctl", "is-active webguard.service");
            var isActive = statusResult.Trim() == "active";

            var enabledResult = await RunCommandAsync("systemctl", "is-enabled webguard.service");
            var isEnabled = enabledResult.Trim() == "enabled";

            return Ok(new ServiceStatus
            {
                IsRunning = isActive,
                Status = isActive ? "Active" : "Inactive",
                IsEnabled = isEnabled,
                IsInstalled = true,
                Message = isActive ? "Le service WebGuard fonctionne correctement" : "Le service est arrêté"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification du statut du service");
            return Ok(new ServiceStatus
            {
                IsRunning = true,
                Status = "Unknown",
                IsEnabled = false,
                IsInstalled = false,
                Message = "Impossible de déterminer le statut du service"
            });
        }
    }

    /// <summary>
    /// Installer le service systemd (Linux uniquement)
    /// </summary>
    [HttpPost("service/install")]
    public async Task<IActionResult> InstallService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BadRequest(new { message = "L'installation du service n'est disponible que sur Linux" });
        }

        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var execPath = Process.GetCurrentProcess().MainModule?.FileName ?? $"{currentDir}/firewall";

            var serviceContent = $@"[Unit]
Description=WebGuard Network Firewall Monitor
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory={currentDir}
ExecStart={execPath}
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
";

            // Écrire le fichier de service
            await System.IO.File.WriteAllTextAsync("/etc/systemd/system/webguard.service", serviceContent);

            // Recharger systemd et activer le service
            await RunCommandAsync("systemctl", "daemon-reload");
            await RunCommandAsync("systemctl", "enable webguard.service");

            _logger.LogInformation("Service WebGuard installé avec succès");

            return Ok(new { 
                success = true, 
                message = "Service installé et activé. Il démarrera automatiquement au prochain redémarrage." 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'installation du service");
            return StatusCode(500, new { message = $"Erreur: {ex.Message}" });
        }
    }

    /// <summary>
    /// Désinstaller le service systemd (Linux uniquement)
    /// </summary>
    [HttpPost("service/uninstall")]
    public async Task<IActionResult> UninstallService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BadRequest(new { message = "La désinstallation du service n'est disponible que sur Linux" });
        }

        try
        {
            // Arrêter et désactiver le service
            await RunCommandAsync("systemctl", "stop webguard.service");
            await RunCommandAsync("systemctl", "disable webguard.service");
            
            // Supprimer le fichier de service
            if (System.IO.File.Exists("/etc/systemd/system/webguard.service"))
            {
                System.IO.File.Delete("/etc/systemd/system/webguard.service");
            }

            await RunCommandAsync("systemctl", "daemon-reload");

            _logger.LogInformation("Service WebGuard désinstallé");

            return Ok(new { success = true, message = "Service désinstallé avec succès" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la désinstallation du service");
            return StatusCode(500, new { message = $"Erreur: {ex.Message}" });
        }
    }

    /// <summary>
    /// Démarrer le service (Linux uniquement)
    /// </summary>
    [HttpPost("service/start")]
    public async Task<IActionResult> StartService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BadRequest(new { message = "Commande disponible uniquement sur Linux" });
        }

        try
        {
            await RunCommandAsync("systemctl", "start webguard.service");
            _logger.LogInformation("Service WebGuard démarré");
            return Ok(new { success = true, message = "Service démarré" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du démarrage du service");
            return StatusCode(500, new { message = $"Erreur: {ex.Message}" });
        }
    }

    /// <summary>
    /// Arrêter le service (Linux uniquement)
    /// </summary>
    [HttpPost("service/stop")]
    public async Task<IActionResult> StopService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BadRequest(new { message = "Commande disponible uniquement sur Linux" });
        }

        try
        {
            await RunCommandAsync("systemctl", "stop webguard.service");
            _logger.LogInformation("Service WebGuard arrêté");
            return Ok(new { success = true, message = "Service arrêté" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'arrêt du service");
            return StatusCode(500, new { message = $"Erreur: {ex.Message}" });
        }
    }

    /// <summary>
    /// Redémarrer le service ou l'application
    /// </summary>
    [HttpPost("service/restart")]
    public async Task<IActionResult> RestartService()
    {
        _logger.LogInformation("Demande de redémarrage du service");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Vérifier si c'est un service systemd
            if (System.IO.File.Exists("/etc/systemd/system/webguard.service"))
            {
                try
                {
                    // Le service systemd redémarrera l'application
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        await RunCommandAsync("systemctl", "restart webguard.service");
                    });

                    return Ok(new { success = true, message = "Redémarrage du service en cours..." });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du redémarrage du service");
                    return StatusCode(500, new { message = $"Erreur: {ex.Message}" });
                }
            }
        }

        // Pour Windows ou si pas de service systemd, on arrête l'application gracieusement
        // Elle sera redémarrée par le superviseur si configuré
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            _appLifetime.StopApplication();
        });

        return Ok(new { success = true, message = "Arrêt de l'application en cours. Redémarrez manuellement si nécessaire." });
    }

    /// <summary>
    /// Arrêter l'application
    /// </summary>
    [HttpPost("shutdown")]
    public IActionResult Shutdown()
    {
        _logger.LogWarning("Demande d'arrêt de l'application reçue");

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            _appLifetime.StopApplication();
        });

        return Ok(new { success = true, message = "Arrêt de l'application en cours..." });
    }

    /// <summary>
    /// Obtenir les logs du service (Linux uniquement)
    /// </summary>
    [HttpGet("service/logs")]
    public async Task<IActionResult> GetServiceLogs([FromQuery] int lines = 100)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Ok(new { logs = "Les logs du service ne sont disponibles que sur Linux.\nUtilisez la console Visual Studio pour voir les logs en mode développement." });
        }

        try
        {
            var output = await RunCommandAsync("journalctl", $"-u webguard.service -n {lines} --no-pager");
            return Ok(new { logs = output });
        }
        catch (Exception ex)
        {
            return Ok(new { logs = $"Erreur lors de la récupération des logs: {ex.Message}" });
        }
    }

    [HttpGet("interfaces")]
    public IActionResult GetInterfaces()
    {
        var interfaces = _packetCapture.GetAvailableInterfaces();
        return Ok(interfaces);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanNetwork()
    {
        await _deviceDiscovery.ScanNetworkAsync();
        return Ok(new { Message = "Network scan initiated" });
    }

    [HttpGet("notifications/stats")]
    public IActionResult GetNotificationStats()
    {
        var stats = _notificationService.GetStats();
        return Ok(stats);
    }

    [HttpPost("notifications/clear")]
    public IActionResult ClearNotifications()
    {
        _notificationService.ClearNotifications();
        return Ok(new { Message = "Notifications and cooldowns cleared" });
    }

    private async Task<string> RunCommandAsync(string command, string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new Exception($"Impossible de démarrer {command}");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Commande {Command} {Args} retournée avec code {Code}: {Error}", 
                command, arguments, process.ExitCode, error);
        }

        return output;
    }
}

public class ServiceStatus
{
    public bool IsRunning { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsInstalled { get; set; }
    public string Message { get; set; } = string.Empty;
}
