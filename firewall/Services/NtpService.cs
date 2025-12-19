using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INtpService
{
    NtpConfig GetConfig();
    Task UpdateConfigAsync(NtpConfig config);
    Task<NtpStatus> GetStatusAsync();
    Task<IEnumerable<NtpServer>> GetServersStatusAsync();
    Task SyncNowAsync();
    Task<DateTime> GetSystemTimeAsync();
    Task SetTimezoneAsync(string timezone);
    Task<IEnumerable<string>> GetAvailableTimezonesAsync();
}

public class NtpService : INtpService
{
    private readonly ILogger<NtpService> _logger;
    private NtpConfig _config = new();

    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const string ConfigPath = "ntp_config.json";

    public NtpService(ILogger<NtpService> logger)
    {
        _logger = logger;
        LoadConfig();
        InitializeDefaultServers();
    }

    private void InitializeDefaultServers()
    {
        if (_config.Servers.Count == 0)
        {
            _config.Servers = new List<NtpServer>
            {
                new NtpServer { Address = "0.pool.ntp.org", Prefer = true, IBurst = true },
                new NtpServer { Address = "1.pool.ntp.org", IBurst = true },
                new NtpServer { Address = "2.pool.ntp.org", IBurst = true },
                new NtpServer { Address = "3.pool.ntp.org", IBurst = true }
            };
            SaveConfig();
        }
    }

    public NtpConfig GetConfig() => _config;

    public async Task UpdateConfigAsync(NtpConfig config)
    {
        _config = config;
        SaveConfig();

        if (IsLinux)
        {
            await ApplyNtpConfigAsync();
        }

        _logger.LogInformation("NTP configuration updated (Enabled: {Enabled}, Servers: {Count})", 
            config.Enabled, config.Servers.Count);
    }

    public async Task<NtpStatus> GetStatusAsync()
    {
        var status = new NtpStatus
        {
            SystemTime = DateTime.UtcNow,
            Timezone = GetCurrentTimezone()
        };

        if (!IsLinux) return status;

        try
        {
            // Utiliser timedatectl pour obtenir le statut
            var output = await ExecuteCommandAsync("timedatectl", "status");
            
            if (output.Contains("synchronized: yes", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("NTP synchronized: yes", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("System clock synchronized: yes", StringComparison.OrdinalIgnoreCase))
            {
                status.Synchronized = true;
            }

            // Essayer d'obtenir plus de détails avec chronyc ou ntpq
            var chronyOutput = await ExecuteCommandAsync("chronyc", "tracking");
            if (!string.IsNullOrEmpty(chronyOutput))
            {
                ParseChronyTracking(chronyOutput, status);
            }
            else
            {
                var ntpqOutput = await ExecuteCommandAsync("ntpq", "-p");
                if (!string.IsNullOrEmpty(ntpqOutput))
                {
                    ParseNtpqOutput(ntpqOutput, status);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get NTP status");
        }

        return status;
    }

    private void ParseChronyTracking(string output, NtpStatus status)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            switch (key)
            {
                case "reference id":
                    status.CurrentServer = value.Split(' ')[0];
                    break;
                case "stratum":
                    if (int.TryParse(value, out var stratum))
                        status.Stratum = stratum;
                    break;
                case "system time":
                    // Format: "0.000001234 seconds slow of NTP time"
                    var match = Regex.Match(value, @"([\d.]+)\s+seconds");
                    if (match.Success && double.TryParse(match.Groups[1].Value, out var offset))
                        status.Offset = offset * 1000; // Convertir en ms
                    break;
                case "root delay":
                    if (double.TryParse(value.Replace(" seconds", ""), out var delay))
                        status.RootDelay = delay * 1000;
                    break;
                case "root dispersion":
                    if (double.TryParse(value.Replace(" seconds", ""), out var disp))
                        status.RootDispersion = disp * 1000;
                    break;
            }
        }
    }

    private void ParseNtpqOutput(string output, NtpStatus status)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            // La ligne avec * indique le serveur actuel
            if (line.StartsWith("*"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    status.CurrentServer = parts[0].TrimStart('*');
                    status.Synchronized = true;
                }
                if (parts.Length > 2 && int.TryParse(parts[2], out var stratum))
                    status.Stratum = stratum;
                if (parts.Length > 8 && double.TryParse(parts[8], out var offset))
                    status.Offset = offset;
            }
        }
    }

    public async Task<IEnumerable<NtpServer>> GetServersStatusAsync()
    {
        if (!IsLinux)
        {
            return _config.Servers;
        }

        try
        {
            // Essayer chronyc en premier
            var output = await ExecuteCommandAsync("chronyc", "sources");
            if (!string.IsNullOrEmpty(output))
            {
                ParseChronySources(output);
            }
            else
            {
                // Fallback sur ntpq
                output = await ExecuteCommandAsync("ntpq", "-p");
                if (!string.IsNullOrEmpty(output))
                {
                    ParseNtpqSources(output);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get NTP servers status");
        }

        return _config.Servers;
    }

    private void ParseChronySources(string output)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("^") || line.StartsWith("*") || line.StartsWith("+") || 
                line.StartsWith("-") || line.StartsWith("?"))
            {
                var parts = line.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;

                var serverAddr = parts[0];
                var server = _config.Servers.FirstOrDefault(s => 
                    s.Address.Contains(serverAddr) || serverAddr.Contains(s.Address));

                if (server != null)
                {
                    server.Status = line[0] switch
                    {
                        '*' => NtpServerStatus.Selected,
                        '+' => NtpServerStatus.Synchronized,
                        '-' => NtpServerStatus.Syncing,
                        '?' => NtpServerStatus.Unreachable,
                        _ => NtpServerStatus.Unknown
                    };

                    if (int.TryParse(parts[1], out var stratum))
                        server.Stratum = stratum;

                    // Les dernières colonnes contiennent offset et jitter
                    if (parts.Length > 4)
                    {
                        var offsetStr = parts[^2];
                        var jitterStr = parts[^1];

                        if (double.TryParse(offsetStr.Replace("ms", "").Replace("us", "").Replace("ns", ""), out var offset))
                        {
                            if (offsetStr.Contains("us")) offset /= 1000;
                            else if (offsetStr.Contains("ns")) offset /= 1000000;
                            server.Offset = offset;
                        }

                        if (double.TryParse(jitterStr.Replace("ms", "").Replace("us", "").Replace("ns", ""), out var jitter))
                        {
                            if (jitterStr.Contains("us")) jitter /= 1000;
                            else if (jitterStr.Contains("ns")) jitter /= 1000000;
                            server.Jitter = jitter;
                        }
                    }

                    server.LastSync = DateTime.UtcNow;
                }
            }
        }
    }

    private void ParseNtpqSources(string output)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("=") || line.Contains("remote"))
                continue;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) continue;

            var serverAddr = parts[0].TrimStart('*', '+', '-', 'x', '.', '#');
            var server = _config.Servers.FirstOrDefault(s => 
                s.Address.Contains(serverAddr) || serverAddr.Contains(s.Address));

            if (server != null)
            {
                server.Status = line[0] switch
                {
                    '*' => NtpServerStatus.Selected,
                    '+' => NtpServerStatus.Synchronized,
                    '-' => NtpServerStatus.Syncing,
                    'x' => NtpServerStatus.Unreachable,
                    _ => NtpServerStatus.Unknown
                };

                if (int.TryParse(parts[2], out var stratum))
                    server.Stratum = stratum;
                if (double.TryParse(parts[7], out var delay))
                    server.Delay = delay;
                if (double.TryParse(parts[8], out var offset))
                    server.Offset = offset;
                if (parts.Length > 9 && double.TryParse(parts[9], out var jitter))
                    server.Jitter = jitter;

                server.LastSync = DateTime.UtcNow;
            }
        }
    }

    public async Task SyncNowAsync()
    {
        if (!IsLinux) return;

        try
        {
            // Essayer chronyc
            var result = await ExecuteCommandAsync("chronyc", "makestep");
            if (string.IsNullOrEmpty(result))
            {
                // Fallback sur ntpdate
                if (_config.Servers.Any())
                {
                    await ExecuteCommandAsync("ntpdate", _config.Servers.First().Address);
                }
            }

            _logger.LogInformation("NTP synchronization forced");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force NTP sync");
            throw;
        }
    }

    public Task<DateTime> GetSystemTimeAsync()
    {
        return Task.FromResult(DateTime.UtcNow);
    }

    public async Task SetTimezoneAsync(string timezone)
    {
        if (!IsLinux)
        {
            _config.Timezone = timezone;
            SaveConfig();
            return;
        }

        try
        {
            await ExecuteCommandAsync("timedatectl", $"set-timezone {timezone}");
            _config.Timezone = timezone;
            SaveConfig();
            _logger.LogInformation("Timezone set to {Timezone}", timezone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set timezone");
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetAvailableTimezonesAsync()
    {
        if (!IsLinux)
        {
            return TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id);
        }

        try
        {
            var output = await ExecuteCommandAsync("timedatectl", "list-timezones");
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id);
        }
    }

    private string GetCurrentTimezone()
    {
        if (!string.IsNullOrEmpty(_config.Timezone))
            return _config.Timezone;

        try
        {
            return TimeZoneInfo.Local.Id;
        }
        catch
        {
            return "UTC";
        }
    }

    private async Task ApplyNtpConfigAsync()
    {
        try
        {
            // Détecter si on utilise chrony ou ntp
            var useChrony = File.Exists("/etc/chrony/chrony.conf") || 
                           File.Exists("/etc/chrony.conf");

            if (useChrony)
            {
                await ApplyChronyConfigAsync();
            }
            else
            {
                await ApplyNtpdConfigAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply NTP configuration");
        }
    }

    private async Task ApplyChronyConfigAsync()
    {
        var configPath = File.Exists("/etc/chrony/chrony.conf") 
            ? "/etc/chrony/chrony.conf" 
            : "/etc/chrony.conf";

        var config = @"# NetGuard NTP Configuration
# Generated at " + DateTime.UtcNow.ToString("u") + @"

";

        foreach (var server in _config.Servers)
        {
            config += $"server {server.Address}";
            if (server.IBurst) config += " iburst";
            if (server.Prefer) config += " prefer";
            config += "\n";
        }

        config += @"
# Record the rate at which the system clock gains/losses time
driftfile /var/lib/chrony/drift

# Allow the system clock to be stepped in the first three updates
makestep 1.0 3

# Enable kernel synchronization
rtcsync

# Enable hardware timestamping on all interfaces
#hwtimestamp *

# Increase the minimum number of selectable sources
minsources 2

# Allow NTP client access from local network
";

        if (_config.IsServer)
        {
            config += "allow 192.168.0.0/16\nallow 10.0.0.0/8\nallow 172.16.0.0/12\n";
        }
        else
        {
            config += "#allow 192.168.0.0/16\n";
        }

        config += @"
# Serve time even if not synchronized to a time source
#local stratum 10

# Specify directory for log files
logdir /var/log/chrony
";

        await File.WriteAllTextAsync(configPath, config);
        await ExecuteCommandAsync("systemctl", "restart chronyd");
    }

    private async Task ApplyNtpdConfigAsync()
    {
        var config = @"# NetGuard NTP Configuration
# Generated at " + DateTime.UtcNow.ToString("u") + @"

driftfile /var/lib/ntp/drift

# Permit time synchronization with our time source
restrict default kod nomodify notrap nopeer noquery
restrict -6 default kod nomodify notrap nopeer noquery

# Permit all access over the loopback interface
restrict 127.0.0.1
restrict ::1

";

        foreach (var server in _config.Servers)
        {
            config += $"server {server.Address}";
            if (server.IBurst) config += " iburst";
            if (server.Prefer) config += " prefer";
            config += "\n";
        }

        if (_config.IsServer)
        {
            config += @"
# Allow clients from local networks
restrict 192.168.0.0 mask 255.255.0.0 nomodify notrap
restrict 10.0.0.0 mask 255.0.0.0 nomodify notrap
restrict 172.16.0.0 mask 255.240.0.0 nomodify notrap
";
        }

        await File.WriteAllTextAsync("/etc/ntp.conf", config);
        await ExecuteCommandAsync("systemctl", "restart ntpd");
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<NtpConfig>(json) ?? new NtpConfig();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load NTP configuration");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save NTP configuration");
        }
    }

    private async Task<string> ExecuteCommandAsync(string command, string arguments)
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Command failed: {Cmd} {Args}", command, arguments);
            return string.Empty;
        }
    }
}
