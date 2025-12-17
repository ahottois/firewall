using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkFirewall.Services;

public interface IPiholeService
{
    Task<PiholeStatus> GetStatusAsync();
    Task<bool> InstallAsync();
    Task<bool> UninstallAsync();
    Task<bool> SetPasswordAsync(string password);
    Task<string> GetInstallLogAsync();
}

public class PiholeStatus
{
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string Version { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
}

public class PiholeService : IPiholeService
{
    private readonly ILogger<PiholeService> _logger;
    private string _installLog = string.Empty;

    public PiholeService(ILogger<PiholeService> logger)
    {
        _logger = logger;
    }

    public async Task<PiholeStatus> GetStatusAsync()
    {
        var status = new PiholeStatus();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return status; // Pi-hole only runs on Linux
        }

        // Check if installed
        if (File.Exists("/usr/local/bin/pihole"))
        {
            status.IsInstalled = true;
            
            // Check if running
            try
            {
                var output = await RunCommandAsync("pihole", "status");
                status.IsRunning = output.Contains("DNS service is active") || output.Contains("listening");
                
                // Get version
                var versionOutput = await RunCommandAsync("pihole", "-v");
                status.Version = versionOutput.Split('\n').FirstOrDefault() ?? "Unknown";
                
                // Determine Web URL (assume default port 80 or check lighttpd)
                // We can't easily know the external IP here without context, but we can guess
                status.WebUrl = "/admin/"; 
            }
            catch
            {
                status.IsRunning = false;
            }
        }

        return status;
    }

    public async Task<bool> InstallAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogError("Pi-hole installation is only supported on Linux.");
            return false;
        }

        _installLog = "Starting installation...\n";

        try
        {
            // Automated install requires setupVars.conf or interactive mode.
            // We will try to run the unattended install if possible, or just the basic script.
            // WARNING: This is a simplified approach. Real-world requires handling prompts.
            // We'll use the official basic install command but piped to bash.
            
            // Note: Running curl | bash from code is risky and might hang on prompts.
            // A better way for "Real" implementation in this context is to assume the user
            // wants us to trigger the standard installer.
            
            // We will attempt to run the installer in non-interactive mode if possible
            // by setting environment variables or pre-creating setupVars.conf
            
            // Create basic setupVars.conf if not exists
            if (!File.Exists("/etc/pihole/setupVars.conf"))
            {
                Directory.CreateDirectory("/etc/pihole");
                await File.WriteAllTextAsync("/etc/pihole/setupVars.conf", 
                    "PIHOLE_INTERFACE=eth0\n" +
                    "PIHOLE_DNS_1=8.8.8.8\n" +
                    "PIHOLE_DNS_2=8.8.4.4\n" +
                    "QUERY_LOGGING=true\n" +
                    "INSTALL_WEB_SERVER=true\n" +
                    "INSTALL_WEB_INTERFACE=true\n" +
                    "LIGHTTPD_ENABLED=true\n");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    _installLog += "Downloading and running installer...\n";
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

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) _installLog += e.Data + "\n"; };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) _installLog += "ERR: " + e.Data + "\n"; };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    
                    _installLog += $"Installation finished with code {process.ExitCode}\n";
                }
                catch (Exception ex)
                {
                    _installLog += $"Error: {ex.Message}\n";
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Pi-hole installation");
            return false;
        }
    }

    public async Task<bool> UninstallAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;

        try
        {
            _installLog = "Starting uninstallation...\n";
            var output = await RunCommandAsync("pihole", "uninstall");
            _installLog += output;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Pi-hole");
            return false;
        }
    }

    public async Task<bool> SetPasswordAsync(string password)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;

        try
        {
            await RunCommandAsync("pihole", $"-a -p {password}");
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

        if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed: {error}");
        }

        return output;
    }
}
