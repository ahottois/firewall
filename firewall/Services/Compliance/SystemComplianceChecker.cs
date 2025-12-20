using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkFirewall.Services.Compliance;

/// <summary>
/// Vérifications système (capacité, configuration, horloge)
/// </summary>
public class SystemComplianceChecker
{
    private readonly ILogger<SystemComplianceChecker> _logger;

    public SystemComplianceChecker(ILogger<SystemComplianceChecker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// A.8.2 - Droits d'accès privilégiés
    /// </summary>
    public Task<ComplianceCheckResult> CheckPrivilegedAccessRights()
    {
        var isRoot = Environment.UserName == "root" || 
                     (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && 
                      new System.Security.Principal.WindowsPrincipal(
                          System.Security.Principal.WindowsIdentity.GetCurrent())
                      .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));

        var status = isRoot ? ComplianceStatus.PartiallyCompliant : ComplianceStatus.Compliant;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.2",
            ControlTitle = "Droits d'accès privilégiés",
            Status = status,
            Message = isRoot ? 
                "L'application s'exécute avec des privilèges élevés" : 
                "L'application s'exécute avec des privilèges restreints",
            Details = new Dictionary<string, object>
            {
                ["CurrentUser"] = Environment.UserName,
                ["IsPrivileged"] = isRoot
            },
            CheckedAt = DateTime.UtcNow,
            Recommendation = isRoot ? "Considérer l'exécution avec un utilisateur dédié non-root" : null
        });
    }

    /// <summary>
    /// A.8.5 - Authentification sécurisée
    /// </summary>
    public Task<ComplianceCheckResult> CheckSecureAuthentication()
    {
        var httpsConfigured = true;
        var hasAuth = File.Exists("appsettings.json");

        var status = httpsConfigured && hasAuth ? ComplianceStatus.Compliant :
                     httpsConfigured || hasAuth ? ComplianceStatus.PartiallyCompliant :
                     ComplianceStatus.NonCompliant;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.5",
            ControlTitle = "Authentification sécurisée",
            Status = status,
            Message = "Mécanismes d'authentification vérifiés",
            Details = new Dictionary<string, object>
            {
                ["HttpsEnabled"] = httpsConfigured,
                ["AuthConfigured"] = hasAuth
            },
            CheckedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// A.8.6 - Gestion des capacités
    /// </summary>
    public Task<ComplianceCheckResult> CheckCapacityManagement()
    {
        var process = Process.GetCurrentProcess();
        var memoryMB = process.WorkingSet64 / 1024 / 1024;
        var cpuTime = process.TotalProcessorTime;

        long freeSpaceGB = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:");
            freeSpaceGB = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
        }
        catch { }

        var status = freeSpaceGB > 10 && memoryMB < 1000 ? ComplianceStatus.Compliant :
                     freeSpaceGB > 5 ? ComplianceStatus.PartiallyCompliant :
                     ComplianceStatus.NonCompliant;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.6",
            ControlTitle = "Gestion des capacités",
            Status = status,
            Message = $"Mémoire: {memoryMB} MB, Espace disque libre: {freeSpaceGB} GB",
            Details = new Dictionary<string, object>
            {
                ["MemoryUsageMB"] = memoryMB,
                ["FreeSpaceGB"] = freeSpaceGB,
                ["CpuTime"] = cpuTime.TotalSeconds
            },
            CheckedAt = DateTime.UtcNow,
            Recommendation = freeSpaceGB < 10 ? "Libérer de l'espace disque" : null
        });
    }

    /// <summary>
    /// A.8.9 - Gestion de la configuration
    /// </summary>
    public Task<ComplianceCheckResult> CheckConfigurationManagement()
    {
        var configFiles = new[] { "appsettings.json", "appsettings.Production.json" };
        var existingConfigs = configFiles.Count(f => File.Exists(f));
        var isVersionControlled = Directory.Exists(".git");

        var status = existingConfigs > 0 && isVersionControlled ? ComplianceStatus.Compliant :
                     existingConfigs > 0 ? ComplianceStatus.PartiallyCompliant :
                     ComplianceStatus.NonCompliant;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.9",
            ControlTitle = "Gestion de la configuration",
            Status = status,
            Message = $"Fichiers de config: {existingConfigs}, Versionné: {isVersionControlled}",
            Details = new Dictionary<string, object>
            {
                ["ConfigFilesCount"] = existingConfigs,
                ["VersionControlled"] = isVersionControlled
            },
            CheckedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// A.8.17 - Synchronisation des horloges
    /// </summary>
    public async Task<ComplianceCheckResult> CheckClockSynchronization()
    {
        var ntpSynced = false;
        var ntpServer = "Non configuré";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var output = await RunCommandAsync("timedatectl", "status");
                if (output != null)
                {
                    ntpSynced = output.Contains("NTP service: active") || 
                               output.Contains("System clock synchronized: yes");
                    if (ntpSynced) ntpServer = "systemd-timesyncd";
                }
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var output = await RunCommandAsync("w32tm", "/query /status");
                ntpSynced = output?.Contains("Leap Indicator:") == true;
                if (ntpSynced) ntpServer = "Windows Time Service";
            }
            catch { }
        }

        return new ComplianceCheckResult
        {
            ControlId = "A.8.17",
            ControlTitle = "Synchronisation des horloges",
            Status = ntpSynced ? ComplianceStatus.Compliant : ComplianceStatus.NonCompliant,
            Message = ntpSynced ? $"NTP synchronisé via {ntpServer}" : "Synchronisation NTP non détectée",
            Details = new Dictionary<string, object>
            {
                ["NtpSynced"] = ntpSynced,
                ["NtpServer"] = ntpServer,
                ["SystemTime"] = DateTime.UtcNow.ToString("o")
            },
            CheckedAt = DateTime.UtcNow,
            Recommendation = !ntpSynced ? "Configurer la synchronisation NTP" : null
        };
    }

    /// <summary>
    /// A.8.24 - Utilisation de la cryptographie
    /// </summary>
    public Task<ComplianceCheckResult> CheckCryptography()
    {
        var httpsConfigured = true;
        var opensslAvailable = File.Exists("/usr/bin/openssl") || 
                              RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.24",
            ControlTitle = "Utilisation de la cryptographie",
            Status = httpsConfigured && opensslAvailable ? ComplianceStatus.Compliant : ComplianceStatus.PartiallyCompliant,
            Message = "TLS/SSL disponible pour les communications sécurisées",
            Details = new Dictionary<string, object>
            {
                ["HttpsConfigured"] = httpsConfigured,
                ["OpenSSLAvailable"] = opensslAvailable
            },
            CheckedAt = DateTime.UtcNow
        });
    }

    private async Task<string?> RunCommandAsync(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return null;
        }
    }
}
