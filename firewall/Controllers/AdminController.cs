using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Data;

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
    private const string GitRepoApiUrl = "https://api.github.com/repos/ahottois/firewall";
    private const string VersionFile = "/opt/netguard/.version";

    public AdminController(ILogger<AdminController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetServiceStatus()
    {
        var status = await GetServiceStateAsync();
        var result = new ServiceStatus
        {
            IsInstalled = await IsServiceInstalledAsync(),
            IsRunning = status == "active",
            Status = status,
            CurrentVersion = GetCurrentVersion(),
            InstallPath = InstallPath,
            ServiceName = ServiceName
        };

        return Ok(result);
    }

    [HttpGet("check-update")]
    [HttpGet("updates/check")]  // Alias pour compatibilité avec le frontend
    public async Task<IActionResult> CheckForUpdate()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "NetGuard-Updater");
            client.Timeout = TimeSpan.FromSeconds(15);

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

            // Get local version from version file
            var localCommit = GetLocalCommit();
            var remoteCommit = commitInfo.Sha ?? "";
            
            // Compare commits
            var isUpdateAvailable = string.IsNullOrEmpty(localCommit) || 
                                    (!remoteCommit.StartsWith(localCommit, StringComparison.OrdinalIgnoreCase) &&
                                     !localCommit.StartsWith(remoteCommit, StringComparison.OrdinalIgnoreCase));

            var commits = new List<CommitInfo>();

            if (isUpdateAvailable)
            {
                try 
                {
                    if (!string.IsNullOrEmpty(localCommit) && localCommit.Length >= 7)
                    {
                        // Try to get comparison
                        var compareUrl = $"{GitRepoApiUrl}/compare/{localCommit}...{remoteCommit}";
                        var compareResponse = await client.GetAsync(compareUrl);
                        if (compareResponse.IsSuccessStatusCode)
                        {
                            var compareJson = await compareResponse.Content.ReadAsStringAsync();
                            var compareData = JsonSerializer.Deserialize<GitHubCompareResult>(compareJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            if (compareData?.Commits != null)
                            {
                                commits = compareData.Commits.Select(c => new CommitInfo 
                                {
                                    Sha = c.Sha,
                                    Message = c.Commit?.Message?.Split('\n').FirstOrDefault(),
                                    Author = c.Commit?.Author?.Name,
                                    Date = c.Commit?.Author?.Date
                                }).OrderByDescending(c => c.Date).ToList();
                            }
                        }
                    }

                    // Fallback: get recent commits if comparison failed or returned nothing (but update is available)
                    if (commits.Count == 0)
                    {
                        var commitsUrl = $"{GitRepoApiUrl}/commits?per_page=5";
                        var commitsResponse = await client.GetAsync(commitsUrl);
                        if (commitsResponse.IsSuccessStatusCode)
                        {
                            var commitsJson = await commitsResponse.Content.ReadAsStringAsync();
                            var recentCommits = JsonSerializer.Deserialize<List<GitHubCommit>>(commitsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            if (recentCommits != null)
                            {
                                commits = recentCommits.Select(c => new CommitInfo 
                                {
                                    Sha = c.Sha,
                                    Message = c.Commit?.Message?.Split('\n').FirstOrDefault(),
                                    Author = c.Commit?.Author?.Name,
                                    Date = c.Commit?.Author?.Date
                                }).ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch commit history");
                }
            }

            return Ok(new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = isUpdateAvailable,
                LocalCommit = string.IsNullOrEmpty(localCommit) ? "non installe" : (localCommit.Length > 7 ? localCommit[..7] : localCommit),
                RemoteCommit = remoteCommit.Length > 7 ? remoteCommit[..7] : remoteCommit,
                LatestCommitMessage = commitInfo.Commit?.Message?.Split('\n').FirstOrDefault() ?? "No message",
                LatestCommitDate = commitInfo.Commit?.Author?.Date,
                LatestCommitAuthor = commitInfo.Commit?.Author?.Name ?? "Unknown",
                Commits = commits
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
            await ExecuteCommandAsync("rm", $"-f /etc/systemd/system/{ServiceName}.service");

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
            var tempDir = Path.Combine(Path.GetTempPath(), $"netguard-update-{DateTime.Now.Ticks}");

            // Clean temp directory if exists
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            // Clone repository with longer timeout
            _logger.LogInformation("Cloning repository to {TempDir}...", tempDir);
            var cloneResult = await ExecuteCommandAsync("git", $"clone --depth 1 {GitRepoUrl} {tempDir}", timeoutSeconds: 180);
            if (cloneResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Git clone failed: " + cloneResult.Error + " " + cloneResult.Output });
            }

            // Get the commit hash before building
            var commitResult = await ExecuteCommandAsync("git", "rev-parse HEAD", workingDirectory: tempDir, timeoutSeconds: 10);
            var commitHash = commitResult.Output.Trim();

            // Build project
            _logger.LogInformation("Building project...");
            var buildResult = await ExecuteCommandAsync("dotnet", $"publish -c Release -o {InstallPath}", 
                workingDirectory: Path.Combine(tempDir, "firewall"), timeoutSeconds: 300);
            if (buildResult.ExitCode != 0)
            {
                return Ok(new { Success = false, Error = "Build failed: " + buildResult.Error + " " + buildResult.Output });
            }

            // Save version info
            if (!string.IsNullOrEmpty(commitHash))
            {
                await System.IO.File.WriteAllTextAsync(VersionFile, commitHash);
                _logger.LogInformation("Saved version: {Commit}", commitHash);
            }

            // Clear all alerts before restarting to start fresh after update
            _logger.LogInformation("Clearing all alerts before update restart...");
            // We use a new scope to get the repository
            using (var scope = HttpContext.RequestServices.CreateScope())
            {
                var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
                await alertRepo.DeleteAllAsync();
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
                Output = $"Update completed successfully (commit: {commitHash[..7]}). Application will restart." 
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

    [HttpGet("updates/history")]
    public async Task<IActionResult> GetUpdateHistory([FromQuery] int count = 20)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "NetGuard-Updater");
            client.Timeout = TimeSpan.FromSeconds(15);

            // Récupérer l'historique des commits
            var commitsUrl = $"{GitRepoApiUrl}/commits?per_page={count}";
            var response = await client.GetAsync(commitsUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                return Ok(new { Success = false, Error = $"Erreur GitHub: {response.StatusCode}" });
            }

            var json = await response.Content.ReadAsStringAsync();
            var commits = JsonSerializer.Deserialize<List<GitHubCommit>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var localCommit = GetLocalCommit();
            var history = commits?.Select(c => new VersionHistoryItem
            {
                Sha = c.Sha ?? "",
                ShortSha = (c.Sha?.Length > 7 ? c.Sha[..7] : c.Sha) ?? "",
                Message = c.Commit?.Message?.Split('\n').FirstOrDefault() ?? "",
                FullMessage = c.Commit?.Message ?? "",
                Author = c.Commit?.Author?.Name ?? "Inconnu",
                Date = c.Commit?.Author?.Date,
                IsCurrent = !string.IsNullOrEmpty(localCommit) && 
                           (c.Sha?.StartsWith(localCommit, StringComparison.OrdinalIgnoreCase) == true ||
                            localCommit.StartsWith(c.Sha ?? "", StringComparison.OrdinalIgnoreCase)),
                IsLatest = c == commits?.FirstOrDefault()
            }).ToList() ?? new List<VersionHistoryItem>();

            // Récupérer les tags/releases si disponibles
            var releases = new List<ReleaseInfo>();
            try
            {
                var releasesUrl = $"{GitRepoApiUrl}/releases?per_page=10";
                var releasesResponse = await client.GetAsync(releasesUrl);
                if (releasesResponse.IsSuccessStatusCode)
                {
                    var releasesJson = await releasesResponse.Content.ReadAsStringAsync();
                    var githubReleases = JsonSerializer.Deserialize<List<GitHubRelease>>(releasesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    releases = githubReleases?.Select(r => new ReleaseInfo
                    {
                        TagName = r.TagName ?? "",
                        Name = r.Name ?? r.TagName ?? "",
                        Body = r.Body ?? "",
                        PublishedAt = r.PublishedAt,
                        IsPrerelease = r.Prerelease,
                        HtmlUrl = r.HtmlUrl ?? ""
                    }).ToList() ?? new List<ReleaseInfo>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Impossible de récupérer les releases");
            }

            return Ok(new VersionHistory
            {
                Success = true,
                CurrentCommit = string.IsNullOrEmpty(localCommit) ? "Non installé" : (localCommit.Length > 7 ? localCommit[..7] : localCommit),
                Commits = history,
                Releases = releases,
                TotalCommits = history.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique");
            return Ok(new { Success = false, Error = ex.Message });
        }
    }

    [HttpGet("updates/changelog")]
    public async Task<IActionResult> GetChangelog([FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "NetGuard-Updater");
            client.Timeout = TimeSpan.FromSeconds(15);

            var localCommit = from ?? GetLocalCommit();
            if (string.IsNullOrEmpty(localCommit))
            {
                // Pas de version locale, retourner les derniers commits
                var commitsUrl = $"{GitRepoApiUrl}/commits?per_page=10";
                var response = await client.GetAsync(commitsUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var commits = JsonSerializer.Deserialize<List<GitHubCommit>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    return Ok(new ChangelogResult
                    {
                        Success = true,
                        FromVersion = "Nouvelle installation",
                        ToVersion = commits?.FirstOrDefault()?.Sha?[..7] ?? "latest",
                        Changes = commits?.Select(c => new ChangelogEntry
                        {
                            Sha = c.Sha?[..7] ?? "",
                            Message = c.Commit?.Message?.Split('\n').FirstOrDefault() ?? "",
                            Author = c.Commit?.Author?.Name ?? "",
                            Date = c.Commit?.Author?.Date,
                            Type = CategorizeCommit(c.Commit?.Message ?? "")
                        }).ToList() ?? new List<ChangelogEntry>()
                    });
                }
            }

            // Comparer les versions
            var targetCommit = to ?? "HEAD";
            var compareUrl = $"{GitRepoApiUrl}/compare/{localCommit}...{targetCommit}";
            var compareResponse = await client.GetAsync(compareUrl);
            
            if (!compareResponse.IsSuccessStatusCode)
            {
                return Ok(new { Success = false, Error = "Impossible de comparer les versions" });
            }

            var compareJson = await compareResponse.Content.ReadAsStringAsync();
            var compareData = JsonSerializer.Deserialize<GitHubCompareResult>(compareJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var changes = compareData?.Commits?.Select(c => new ChangelogEntry
            {
                Sha = c.Sha?[..7] ?? "",
                Message = c.Commit?.Message?.Split('\n').FirstOrDefault() ?? "",
                Author = c.Commit?.Author?.Name ?? "",
                Date = c.Commit?.Author?.Date,
                Type = CategorizeCommit(c.Commit?.Message ?? "")
            }).OrderByDescending(c => c.Date).ToList() ?? new List<ChangelogEntry>();

            return Ok(new ChangelogResult
            {
                Success = true,
                FromVersion = localCommit.Length > 7 ? localCommit[..7] : localCommit,
                ToVersion = targetCommit == "HEAD" ? "Dernière version" : targetCommit,
                Changes = changes,
                TotalChanges = changes.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du changelog");
            return Ok(new { Success = false, Error = ex.Message });
        }
    }

    private static string CategorizeCommit(string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        if (lowerMessage.StartsWith("fix") || lowerMessage.Contains("bugfix") || lowerMessage.Contains("correction"))
            return "fix";
        if (lowerMessage.StartsWith("feat") || lowerMessage.Contains("feature") || lowerMessage.Contains("ajout"))
            return "feature";
        if (lowerMessage.StartsWith("refactor") || lowerMessage.Contains("refactoring"))
            return "refactor";
        if (lowerMessage.StartsWith("doc") || lowerMessage.Contains("documentation"))
            return "docs";
        if (lowerMessage.StartsWith("style") || lowerMessage.Contains("css") || lowerMessage.Contains("ui"))
            return "style";
        if (lowerMessage.StartsWith("perf") || lowerMessage.Contains("performance") || lowerMessage.Contains("optimiz"))
            return "perf";
        if (lowerMessage.StartsWith("test"))
            return "test";
        return "other";
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

    private async Task<string> GetServiceStateAsync()
    {
        var result = await ExecuteCommandAsync("systemctl", $"is-active {ServiceName}");
        return result.Output.Trim();
    }

    private string GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }

    private string GetLocalCommit()
    {
        try
        {
            // First try to read from version file
            if (System.IO.File.Exists(VersionFile))
            {
                var commit = System.IO.File.ReadAllText(VersionFile).Trim();
                if (!string.IsNullOrEmpty(commit))
                    return commit;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read version file");
        }

        return string.Empty;
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

            _logger.LogDebug("Executing: {Command} {Args}", command, arguments);

            using var process = new Process { StartInfo = psi };
            process.Start();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
                
                await process.WaitForExitAsync(cts.Token);
                
                result.Output = await outputTask;
                result.Error = await errorTask;
                result.ExitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                result.ExitCode = -1;
                result.Error = $"Command timed out after {timeoutSeconds} seconds";
            }

            _logger.LogDebug("Command result: Exit={Exit}, Output={Output}, Error={Error}", 
                result.ExitCode, result.Output.Length > 100 ? result.Output[..100] + "..." : result.Output, result.Error);
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
            _logger.LogError(ex, "Command execution failed");
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
        public string Status { get; set; } = string.Empty;
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
        public List<CommitInfo>? Commits { get; set; }
        public string? Error { get; set; }
    }

    public class CommitInfo
    {
        public string? Sha { get; set; }
        public string? Message { get; set; }
        public string? Author { get; set; }
        public DateTime? Date { get; set; }
    }

    public class GitHubCompareResult
    {
        public List<GitHubCommit>? Commits { get; set; }
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

    public class VersionHistory
    {
        public bool Success { get; set; }
        public string CurrentCommit { get; set; } = string.Empty;
        public List<VersionHistoryItem> Commits { get; set; } = new();
        public List<ReleaseInfo> Releases { get; set; } = new();
        public int TotalCommits { get; set; }
    }

    public class VersionHistoryItem
    {
        public string Sha { get; set; } = string.Empty;
        public string ShortSha { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FullMessage { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime? Date { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsLatest { get; set; }
    }

    public class ReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public bool IsPrerelease { get; set; }
        public string HtmlUrl { get; set; } = string.Empty;
    }

    public class ChangelogResult
    {
        public bool Success { get; set; }
        public string FromVersion { get; set; } = string.Empty;
        public string ToVersion { get; set; } = string.Empty;
        public List<ChangelogEntry> Changes { get; set; } = new();
        public int TotalChanges { get; set; }
    }

    public class ChangelogEntry
    {
        public string Sha { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime? Date { get; set; }
        public string Type { get; set; } = "other";
    }

    public class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? Name { get; set; }
        public string? Body { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool Prerelease { get; set; }
        public string? HtmlUrl { get; set; }
    }
}
