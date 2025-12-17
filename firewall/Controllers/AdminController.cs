using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ILogger<AdminController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string ServiceName = "netguard";
    private const string InstallPath = "/opt/netguard";
    private const string GitRepoUrl = "https://github.com/ahottois/firewall.git";
    private const string GitApiUrl = "https://api.github.com/repos/ahottois/firewall/commits/master";

    public AdminController(ILogger<AdminController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetServiceStatus()
    {
        var result = new ServiceStatus
        {
            IsInstalled = await IsServiceInstalledAsync(),
            IsRunning = await IsServiceRunningAsync(),
            CurrentVersion = GetCurrentVersion(),
            InstallPath = InstallPath,
            ServiceName = ServiceName
        };

        return Ok(result);
    }

    [HttpGet("check-update")]
    public async Task<IActionResult> CheckForUpdate()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "NetGuard-Updater");
            client.Timeout = TimeSpan.FromSeconds(10);

            // Get latest commit from GitHub API
            var response = await client.GetAsync(GitApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return Ok(new UpdateCheckResult
                {
                    Success = false,
                    Error = $"Failed to check GitHub: {response.StatusCode}"
                });
            }

            var json = await response.Content.ReadAsStringAsync();
            var commitInfo = JsonSerializer.Deserialize<GitHubCommit>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (commitInfo == null)
            {
                return Ok(new UpdateCheckResult
                {
                    Success = false,
                    Error = "Failed to parse GitHub response"
                });
            }

            // Get local commit hash
            var localCommitResult = await ExecuteCommandAsync("git", "rev-parse HEAD", workingDirectory: InstallPath);
            var localCommit = localCommitResult.Output.Trim();

            // Compare commits
            var remoteCommit = commitInfo.Sha ?? "";
            var isUpdateAvailable = !string.IsNullOrEmpty(remoteCommit) && 
                                    !remoteCommit.StartsWith(localCommit, StringComparison.OrdinalIgnoreCase) &&
                                    !localCommit.StartsWith(remoteCommit, StringComparison.OrdinalIgnoreCase);

            // If we can't get local commit, check by date
            if (string.IsNullOrEmpty(localCommit) || localCommitResult.ExitCode != 0)
            {
                // Fallback: assume update available if we can't determine
                isUpdateAvailable = true;
                localCommit = "unknown";
            }

            return Ok(new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = isUpdateAvailable,
                LocalCommit = localCommit.Length > 7 ? localCommit[..7] : localCommit,
                RemoteCommit = remoteCommit.Length > 7 ? remoteCommit[..7] : remoteCommit,
                LatestCommitMessage = commitInfo.Commit?.Message?.Split('\n').FirstOrDefault() ?? "No message",
                LatestCommitDate = commitInfo.Commit?.Author?.Date,
                LatestCommitAuthor = commitInfo.Commit?.Author?.Name ?? "Unknown"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            return Ok(new UpdateCheckResult
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpPost("service/start")]
    public async Task<IActionResult> StartService()
    {
        _logger.LogInformation("Starting service...");
        var result = await ExecuteCommandAsync("systemctl", $"start {ServiceName}");
        return Ok(new { Success = result.ExitCode == 0, Output = result.Output, Error = result.Error });
    }

    [HttpPost("service/stop")]
    public async Task<IActionResult> StopService()
    {
        _logger.LogInformation("Stopping service...");
        var result = await ExecuteCommandAsync("systemctl", $"stop {ServiceName}");
        return Ok(new { Success = result.ExitCode == 0, Output = result.Output, Error = result.Error });
    }

    [HttpPost("service/restart")]
    public async Task<IActionResult> RestartService()
    {
        _logger.LogInformation("Restarting service...");
        var result = await ExecuteCommandAsync("systemctl", $"restart {ServiceName}");
        return Ok(new { Success = result.ExitCode == 0, Output = result.Output, Error = result.Error });
    }

    [HttpPost("service/install")]
    public async Task<IActionResult> InstallService()
    {
        _logger.LogInformation("Installing service...");

        var serviceFile = $@"[Unit]
Description=NetGuard Network Firewall Monitor
After=network.target

[Service]
Type=simple
ExecStart={InstallPath}/firewall
WorkingDirectory={InstallPath}
Restart=always
User=root
Environment=DOTNET_ROOT=/usr/share/dotnet

[Install]
WantedBy=multi-user.target
";

        try
        {
            // Write service file
            var tempFile = Path.GetTempFileName();
            await System.IO.File.WriteAllTextAsync(tempFile, serviceFile);

            // Copy to systemd directory
            var copyResult = await ExecuteCommandAsync("cp", $"{tempFile} /etc/systemd/system/{ServiceName}.service");
            if (copyResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Failed to copy service file: " + copyResult.Error });
            }

            // Reload systemd
            var reloadResult = await ExecuteCommandAsync("systemctl", "daemon-reload");
            if (reloadResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Failed to reload systemd: " + reloadResult.Error });
            }

            // Enable service
            var enableResult = await ExecuteCommandAsync("systemctl", $"enable {ServiceName}");
            if (enableResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Failed to enable service: " + enableResult.Error });
            }

            // Start service
            var startResult = await ExecuteCommandAsync("systemctl", $"start {ServiceName}");

            return Ok(new { 
                Success = startResult.ExitCode == 0, 
                Output = "Service installed and started successfully",
                Error = startResult.Error 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing service");
            return Ok(new { Success = false, Error = ex.Message });
        }
    }

    [HttpPost("service/uninstall")]
    public async Task<IActionResult> UninstallService()
    {
        _logger.LogInformation("Uninstalling service...");

        try
        {
            // Stop service
            await ExecuteCommandAsync("systemctl", $"stop {ServiceName}");

            // Disable service
            await ExecuteCommandAsync("systemctl", $"disable {ServiceName}");

            // Remove service file
            var removeResult = await ExecuteCommandAsync("rm", $"-f /etc/systemd/system/{ServiceName}.service");

            // Reload systemd
            await ExecuteCommandAsync("systemctl", "daemon-reload");

            return Ok(new { 
                Success = true, 
                Output = "Service uninstalled successfully" 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uninstalling service");
            return Ok(new { Success = false, Error = ex.Message });
        }
    }

    [HttpPost("update")]
    public async Task<IActionResult> UpdateFromGithub()
    {
        _logger.LogInformation("Updating from GitHub...");

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "netguard-update");

            // Clean temp directory
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            // Clone repository
            _logger.LogInformation("Cloning repository...");
            var cloneResult = await ExecuteCommandAsync("git", $"clone {GitRepoUrl} {tempDir}", timeoutSeconds: 120);
            if (cloneResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Git clone failed: " + cloneResult.Error });
            }

            // Build project
            _logger.LogInformation("Building project...");
            var buildResult = await ExecuteCommandAsync("dotnet", $"publish -c Release -o {InstallPath}", 
                workingDirectory: Path.Combine(tempDir, "firewall"), timeoutSeconds: 300);
            if (buildResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Build failed: " + buildResult.Error });
            }

            // Restart service if installed
            if (await IsServiceInstalledAsync())
            {
                _logger.LogInformation("Restarting service...");
                await ExecuteCommandAsync("systemctl", $"restart {ServiceName}");
            }

            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }

            return Ok(new { 
                Success = true, 
                Output = "Update completed successfully. Application will restart." 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating from GitHub");
            return Ok(new { Success = false, Error = ex.Message });
        }
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int lines = 100)
    {
        var result = await ExecuteCommandAsync("journalctl", $"-u {ServiceName} -n {lines} --no-pager");
        return Ok(new { Logs = result.Output, Error = result.Error });
    }

    private async Task<bool> IsServiceInstalledAsync()
    {
        var result = await ExecuteCommandAsync("systemctl", $"list-unit-files {ServiceName}.service");
        return result.Output.Contains(ServiceName);
    }

    private async Task<bool> IsServiceRunningAsync()
    {
        var result = await ExecuteCommandAsync("systemctl", $"is-active {ServiceName}");
        return result.Output.Trim() == "active";
    }

    private string GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }

    private async Task<CommandResult> ExecuteCommandAsync(string command, string arguments, 
        string? workingDirectory = null, int timeoutSeconds = 30)
    {
        var result = new CommandResult();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.WhenAny(
                Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync()),
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))
            );

            if (completed is Task<Task[]>)
            {
                result.Output = await outputTask;
                result.Error = await errorTask;
                result.ExitCode = process.ExitCode;
            }
            else
            {
                process.Kill();
                result.ExitCode = -1;
                result.Error = "Command timed out";
            }
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
        }

        return result;
    }

    private class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class ServiceStatus
    {
        public bool IsInstalled { get; set; }
        public bool IsRunning { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool UpdateAvailable { get; set; }
        public string? LocalCommit { get; set; }
        public string? RemoteCommit { get; set; }
        public string? LatestCommitMessage { get; set; }
        public DateTime? LatestCommitDate { get; set; }
        public string? LatestCommitAuthor { get; set; }
        public string? Error { get; set; }
    }

    public class GitHubCommit
    {
        public string? Sha { get; set; }
        public GitHubCommitDetail? Commit { get; set; }
    }

    public class GitHubCommitDetail
    {
        public string? Message { get; set; }
        public GitHubAuthor? Author { get; set; }
    }

    public class GitHubAuthor
    {
        public string? Name { get; set; }
        public DateTime? Date { get; set; }
    }
}
