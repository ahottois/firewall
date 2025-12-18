using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkFirewall.Services;

public interface IPiholeService
{
    Task<PiholeStatus> GetStatusAsync();
    Task<PiholeSummary?> GetSummaryAsync();
    Task<bool> InstallAsync();
    Task<bool> UninstallAsync();
    Task<bool> SetPasswordAsync(string password);
    Task<bool> EnableAsync();
    Task<bool> DisableAsync(int? duration = null);
    Task<string> GetInstallLogAsync();
    bool IsLinux { get; }
}

public class PiholeStatus
{
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public bool IsEnabled { get; set; }
    public string Version { get; set; } = string.Empty;
    public string FtlVersion { get; set; } = string.Empty;
    public string WebVersion { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public bool IsLinux { get; set; }
}

public class PiholeSummary
{
    [JsonPropertyName("domains_being_blocked")]
    public long DomainsBeingBlocked { get; set; }

    [JsonPropertyName("dns_queries_today")]
    public long DnsQueriesToday { get; set; }

    [JsonPropertyName("ads_blocked_today")]
    public long AdsBlockedToday { get; set; }

    [JsonPropertyName("ads_percentage_today")]
    public double AdsPercentageToday { get; set; }

    [JsonPropertyName("unique_clients")]
    public int UniqueClients { get; set; }

    [JsonPropertyName("dns_queries_all_types")]
    public long DnsQueriesAllTypes { get; set; }

    [JsonPropertyName("reply_NODATA")]
    public long ReplyNodata { get; set; }

    [JsonPropertyName("reply_NXDOMAIN")]
    public long ReplyNxdomain { get; set; }

    [JsonPropertyName("reply_CNAME")]
    public long ReplyCname { get; set; }

    [JsonPropertyName("reply_IP")]
    public long ReplyIp { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("gravity_last_updated")]
    public GravityInfo? GravityLastUpdated { get; set; }
}

public class GravityInfo
{
    [JsonPropertyName("file_exists")]
    public bool FileExists { get; set; }

    [JsonPropertyName("absolute")]
    public long Absolute { get; set; }

    [JsonPropertyName("relative")]
    public GravityRelative? Relative { get; set; }
}

public class GravityRelative
{
    [JsonPropertyName("days")]
    public int Days { get; set; }

    [JsonPropertyName("hours")]
    public int Hours { get; set; }

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; }
}

public class PiholeService : IPiholeService
{
    private readonly ILogger<PiholeService> _logger;
    private readonly HttpClient _httpClient;
    private string _installLog = string.Empty;
    private bool _isInstalling = false;

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public PiholeService(ILogger<PiholeService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<PiholeSummary?> GetSummaryAsync()
    {
        if (!IsLinux) return null;

        try
        {
            var response = await _httpClient.GetStringAsync("http://127.0.0.1/admin/api.php");
            return JsonSerializer.Deserialize<PiholeSummary>(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch Pi-hole summary: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<PiholeStatus> GetStatusAsync()
    {
        var status = new PiholeStatus
        {
            IsLinux = IsLinux
        };

        if (!IsLinux)
        {
            return status; // Pi-hole only runs on Linux
        }

        // Check if installed
        bool isInstalled = File.Exists("/usr/local/bin/pihole");
        bool isRunning = false;
        bool isEnabled = false;

        // Check if running by looking for pihole-FTL process
        try
        {
            var processOutput = await RunCommandAsync("pgrep", "-x pihole-FTL");
            if (!string.IsNullOrWhiteSpace(processOutput))
            {
                isRunning = true;
                isInstalled = true;
            }
            else if (isInstalled)
            {
                var output = await RunCommandAsync("pihole", "status");
                isRunning = output.Contains("DNS service is active") || 
                           output.Contains("listening") ||
                           output.Contains("Pi-hole blocking is enabled");
                isEnabled = output.Contains("Pi-hole blocking is enabled");
            }
        }
        catch
        {
            // Ignore errors, assume not running
        }

        // Double-check enabled status via API if running
        if (isRunning)
        {
            try
            {
                var summary = await GetSummaryAsync();
                if (summary != null)
                {
                    isEnabled = summary.Status?.ToLower() == "enabled";
                }
            }
            catch { }
        }

        if (isInstalled)
        {
            status.IsInstalled = true;
            status.IsRunning = isRunning;
            status.IsEnabled = isEnabled;
            
            try
            {
                // Get versions
                var versionOutput = await RunCommandAsync("pihole", "-v");
                var lines = versionOutput.Split('\n');
                
                foreach (var line in lines)
                {
                    if (line.Contains("Pi-hole version"))
                        status.Version = ExtractVersion(line);
                    else if (line.Contains("FTL version"))
                        status.FtlVersion = ExtractVersion(line);
                    else if (line.Contains("Web Interface version"))
                        status.WebVersion = ExtractVersion(line);
                }
                
                if (string.IsNullOrEmpty(status.Version))
                    status.Version = lines.FirstOrDefault() ?? "Unknown";
                
                status.WebUrl = "/admin/";
            }
            catch
            {
                status.Version = "Unknown";
            }
        }

        return status;
    }

    private string ExtractVersion(string line)
    {
        // Format: "  Pi-hole version is v5.17.1 (Latest: v5.17.1)"
        var parts = line.Split(new[] { " is ", " v" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var ver = parts[1].Split(' ')[0].Trim();
            return ver.StartsWith("v") ? ver : "v" + ver;
        }
        return line.Trim();
    }

    public async Task<bool> EnableAsync()
    {
        if (!IsLinux) return false;

        try
        {
            await RunCommandAsync("pihole", "enable");
            _logger.LogInformation("Pi-hole enabled");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable Pi-hole");
            return false;
        }
    }

    public async Task<bool> DisableAsync(int? duration = null)
    {
        if (!IsLinux) return false;

        try
        {
            var args = duration.HasValue ? $"disable {duration.Value}s" : "disable";
            await RunCommandAsync("pihole", args);
            _logger.LogInformation("Pi-hole disabled" + (duration.HasValue ? $" for {duration}s" : " permanently"));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable Pi-hole");
            return false;
        }
    }

    public async Task<bool> InstallAsync()
    {
        if (!IsLinux)
        {
            _logger.LogError("Pi-hole installation is only supported on Linux.");
            _installLog = "Erreur: Pi-hole ne peut être installé que sur Linux.\n";
            return false;
        }

        if (_isInstalling)
        {
            _logger.LogWarning("Installation already in progress");
            return false;
        }

        _isInstalling = true;
        _installLog = "=== Début de l'installation de Pi-hole ===\n";
        _installLog += $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";

        try
        {
            // Create setupVars.conf for unattended install
            if (!File.Exists("/etc/pihole/setupVars.conf"))
            {
                Directory.CreateDirectory("/etc/pihole");
                
                // Detect default interface
                var defaultInterface = await GetDefaultNetworkInterfaceAsync();
                
                await File.WriteAllTextAsync("/etc/pihole/setupVars.conf", 
                    $"PIHOLE_INTERFACE={defaultInterface}\n" +
                    "PIHOLE_DNS_1=8.8.8.8\n" +
                    "PIHOLE_DNS_2=8.8.4.4\n" +
                    "QUERY_LOGGING=true\n" +
                    "INSTALL_WEB_SERVER=true\n" +
                    "INSTALL_WEB_INTERFACE=true\n" +
                    "LIGHTTPD_ENABLED=true\n" +
                    "CACHE_SIZE=10000\n" +
                    "DNS_FQDN_REQUIRED=true\n" +
                    "DNS_BOGUS_PRIV=true\n" +
                    "DNSMASQ_LISTENING=local\n" +
                    "WEBPASSWORD=\n");
                
                _installLog += $"Configuration créée pour l'interface: {defaultInterface}\n";
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    _installLog += "Téléchargement et exécution de l'installateur...\n";
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "bash",
                            Arguments = "-c \"curl -sSL https://install.pi-hole.net | bash /dev/stdin --unattended\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.OutputDataReceived += (s, e) => 
                    { 
                        if (e.Data != null) 
                        {
                            _installLog += e.Data + "\n";
                            _logger.LogDebug("Pi-hole install: {Line}", e.Data);
                        }
                    };
                    
                    process.ErrorDataReceived += (s, e) => 
                    { 
                        if (e.Data != null) 
                        {
                            _installLog += "[ERR] " + e.Data + "\n";
                            _logger.LogWarning("Pi-hole install error: {Line}", e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        _installLog += "\n=== Installation terminée avec succès! ===\n";
                        _installLog += "Pi-hole est maintenant actif.\n";
                    }
                    else
                    {
                        _installLog += $"\n=== Installation terminée avec code {process.ExitCode} ===\n";
                    }
                }
                catch (Exception ex)
                {
                    _installLog += $"\n[ERREUR FATALE] {ex.Message}\n";
                    _logger.LogError(ex, "Pi-hole installation failed");
                }
                finally
                {
                    _isInstalling = false;
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Pi-hole installation");
            _isInstalling = false;
            return false;
        }
    }

    private async Task<string> GetDefaultNetworkInterfaceAsync()
    {
        try
        {
            // Get the default route interface
            var output = await RunCommandAsync("ip", "route show default");
            // Output format: "default via 192.168.1.1 dev eth0 proto dhcp..."
            var parts = output.Split(' ');
            var devIndex = Array.IndexOf(parts, "dev");
            if (devIndex >= 0 && devIndex < parts.Length - 1)
            {
                return parts[devIndex + 1].Trim();
            }
        }
        catch { }
        
        return "eth0"; // Default fallback
    }

    public async Task<bool> UninstallAsync()
    {
        if (!IsLinux) return false;

        try
        {
            _installLog = "=== Début de la désinstallation de Pi-hole ===\n";
            _installLog += $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";
            
            var output = await RunCommandAsync("pihole", "uninstall --unattended");
            _installLog += output;
            _installLog += "\n=== Désinstallation terminée ===\n";
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Pi-hole");
            _installLog += $"\n[ERREUR] {ex.Message}\n";
            return false;
        }
    }

    public async Task<bool> SetPasswordAsync(string password)
    {
        if (!IsLinux) return false;

        try
        {
            // Use echo to pipe the password to avoid shell escaping issues
            await RunCommandAsync("bash", $"-c \"echo '{password}' | pihole -a -p\"");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Pi-hole password");
            return false;
        }
    }

    public Task<string> GetInstallLogAsync()
    {
        return Task.FromResult(_installLog);
    }

    private async Task<string> RunCommandAsync(string command, string args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            throw new Exception($"Command failed: {error}");
        }

        return output;
    }
}
