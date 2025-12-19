using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface ISnmpService
{
    SnmpConfig GetConfig();
    Task UpdateConfigAsync(SnmpConfig config);
    Task<SnmpStatistics> GetStatisticsAsync();
    Task<IEnumerable<SnmpUser>> GetUsersAsync();
    Task<SnmpUser> AddUserAsync(SnmpUser user);
    Task DeleteUserAsync(string username);
    Task<IEnumerable<SnmpTrapReceiver>> GetTrapReceiversAsync();
    Task<SnmpTrapReceiver> AddTrapReceiverAsync(SnmpTrapReceiver receiver);
    Task DeleteTrapReceiverAsync(string address);
    Task RestartSnmpServiceAsync();
    Task SendTestTrapAsync(string receiverAddress);
}

public class SnmpService : ISnmpService
{
    private readonly ILogger<SnmpService> _logger;
    private SnmpConfig _config = new();

    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const string ConfigPath = "snmp_config.json";
    private const string SnmpdConfigPath = "/etc/snmp/snmpd.conf";

    public SnmpService(ILogger<SnmpService> logger)
    {
        _logger = logger;
        LoadConfig();
    }

    public SnmpConfig GetConfig() => _config;

    public async Task UpdateConfigAsync(SnmpConfig config)
    {
        _config = config;
        SaveConfig();

        if (IsLinux)
        {
            await ApplySnmpConfigAsync();
        }

        _logger.LogInformation("SNMP configuration updated (Enabled: {Enabled}, Version: {Version})", 
            config.Enabled, config.Version);
    }

    public async Task<SnmpStatistics> GetStatisticsAsync()
    {
        var stats = new SnmpStatistics();

        if (!IsLinux) return stats;

        try
        {
            // Lire les statistiques depuis /proc ou snmpget
            var output = await ExecuteCommandAsync("snmpget", 
                "-v2c -c public localhost SNMPv2-MIB::snmpInPkts.0 SNMPv2-MIB::snmpOutPkts.0");

            // Parser les résultats (simplifiée ici)
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("snmpInPkts"))
                {
                    var parts = line.Split('=');
                    if (parts.Length > 1 && long.TryParse(parts[1].Replace("Counter32:", "").Trim(), out var val))
                        stats.PacketsReceived = val;
                }
                else if (line.Contains("snmpOutPkts"))
                {
                    var parts = line.Split('=');
                    if (parts.Length > 1 && long.TryParse(parts[1].Replace("Counter32:", "").Trim(), out var val))
                        stats.PacketsSent = val;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get SNMP statistics");
        }

        return stats;
    }

    public Task<IEnumerable<SnmpUser>> GetUsersAsync()
    {
        return Task.FromResult(_config.Users.AsEnumerable());
    }

    public async Task<SnmpUser> AddUserAsync(SnmpUser user)
    {
        // Valider l'utilisateur
        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ArgumentException("Username is required");

        if (user.SecurityLevel >= SnmpSecurityLevel.AuthNoPriv && 
            string.IsNullOrWhiteSpace(user.AuthPassword))
            throw new ArgumentException("Auth password required for AuthNoPriv/AuthPriv");

        if (user.SecurityLevel == SnmpSecurityLevel.AuthPriv && 
            string.IsNullOrWhiteSpace(user.PrivPassword))
            throw new ArgumentException("Privacy password required for AuthPriv");

        _config.Users.Add(user);
        SaveConfig();

        if (IsLinux && _config.Enabled)
        {
            await ApplySnmpConfigAsync();
        }

        _logger.LogInformation("SNMP user added: {Username} ({Level})", user.Username, user.SecurityLevel);
        return user;
    }

    public async Task DeleteUserAsync(string username)
    {
        var user = _config.Users.FirstOrDefault(u => u.Username == username);
        if (user == null)
            throw new KeyNotFoundException($"SNMP user {username} not found");

        _config.Users.Remove(user);
        SaveConfig();

        if (IsLinux && _config.Enabled)
        {
            await ApplySnmpConfigAsync();
        }

        _logger.LogInformation("SNMP user deleted: {Username}", username);
    }

    public Task<IEnumerable<SnmpTrapReceiver>> GetTrapReceiversAsync()
    {
        return Task.FromResult(_config.TrapReceivers.AsEnumerable());
    }

    public async Task<SnmpTrapReceiver> AddTrapReceiverAsync(SnmpTrapReceiver receiver)
    {
        if (string.IsNullOrWhiteSpace(receiver.Address))
            throw new ArgumentException("Address is required");

        _config.TrapReceivers.Add(receiver);
        SaveConfig();

        if (IsLinux && _config.Enabled)
        {
            await ApplySnmpConfigAsync();
        }

        _logger.LogInformation("SNMP trap receiver added: {Address}:{Port}", receiver.Address, receiver.Port);
        return receiver;
    }

    public async Task DeleteTrapReceiverAsync(string address)
    {
        var receiver = _config.TrapReceivers.FirstOrDefault(r => r.Address == address);
        if (receiver == null)
            throw new KeyNotFoundException($"Trap receiver {address} not found");

        _config.TrapReceivers.Remove(receiver);
        SaveConfig();

        if (IsLinux && _config.Enabled)
        {
            await ApplySnmpConfigAsync();
        }

        _logger.LogInformation("SNMP trap receiver deleted: {Address}", address);
    }

    public async Task RestartSnmpServiceAsync()
    {
        if (!IsLinux) return;

        try
        {
            await ExecuteCommandAsync("systemctl", "restart snmpd");
            _logger.LogInformation("SNMP service restarted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart SNMP service");
            throw;
        }
    }

    public async Task SendTestTrapAsync(string receiverAddress)
    {
        if (!IsLinux) return;

        try
        {
            // Envoyer un trap de test
            await ExecuteCommandAsync("snmptrap",
                $"-v 2c -c {_config.ReadCommunity ?? "public"} {receiverAddress}:{_config.TrapPort} '' " +
                "NET-SNMP-EXAMPLES-MIB::netSnmpExampleHeartbeatNotification " +
                "netSnmpExampleHeartbeatRate i 123456");

            _logger.LogInformation("Test trap sent to {Address}", receiverAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test trap");
            throw;
        }
    }

    private async Task ApplySnmpConfigAsync()
    {
        if (!_config.Enabled)
        {
            await ExecuteCommandAsync("systemctl", "stop snmpd");
            return;
        }

        try
        {
            var config = GenerateSnmpdConfig();
            
            // Sauvegarder l'ancien fichier
            if (File.Exists(SnmpdConfigPath))
            {
                File.Copy(SnmpdConfigPath, SnmpdConfigPath + ".bak", true);
            }

            await File.WriteAllTextAsync(SnmpdConfigPath, config);

            // Pour SNMPv3, créer les utilisateurs
            if (_config.Version == SnmpVersion.V3 && _config.Users.Any())
            {
                await CreateSnmpV3UsersAsync();
            }

            await ExecuteCommandAsync("systemctl", "restart snmpd");

            _logger.LogInformation("SNMP configuration applied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply SNMP configuration");
            
            // Restaurer l'ancienne configuration
            if (File.Exists(SnmpdConfigPath + ".bak"))
            {
                File.Copy(SnmpdConfigPath + ".bak", SnmpdConfigPath, true);
            }
            
            throw;
        }
    }

    private string GenerateSnmpdConfig()
    {
        var config = $@"# NetGuard SNMP Configuration
# Generated at {DateTime.UtcNow:u}

# Agent configuration
agentAddress udp:{_config.Port},udp6:{_config.Port}

# System information
sysLocation    {_config.SysLocation ?? "NetGuard Router"}
sysContact     {_config.SysContact ?? "admin@localhost"}
sysName        {_config.SysName ?? "netguard"}

# Services (72 = typical router/host)
sysServices    72
";

        // SNMPv1/v2c communities
        if (_config.Version == SnmpVersion.V1 || _config.Version == SnmpVersion.V2c)
        {
            if (!string.IsNullOrEmpty(_config.ReadCommunity))
            {
                config += $"\n# Read-only community\n";
                if (_config.AllowedHosts.Any())
                {
                    foreach (var host in _config.AllowedHosts)
                    {
                        config += $"rocommunity {_config.ReadCommunity} {host}\n";
                    }
                }
                else
                {
                    config += $"rocommunity {_config.ReadCommunity} default\n";
                }
            }

            if (!string.IsNullOrEmpty(_config.WriteCommunity))
            {
                config += $"\n# Read-write community\n";
                if (_config.AllowedHosts.Any())
                {
                    foreach (var host in _config.AllowedHosts)
                    {
                        config += $"rwcommunity {_config.WriteCommunity} {host}\n";
                    }
                }
                else
                {
                    config += $"rwcommunity {_config.WriteCommunity} default\n";
                }
            }
        }

        // SNMPv3 users
        if (_config.Version == SnmpVersion.V3)
        {
            config += "\n# SNMPv3 configuration\n";
            
            foreach (var user in _config.Users)
            {
                var authProto = user.AuthProtocol switch
                {
                    SnmpAuthProtocol.MD5 => "MD5",
                    SnmpAuthProtocol.SHA => "SHA",
                    SnmpAuthProtocol.SHA256 => "SHA-256",
                    SnmpAuthProtocol.SHA512 => "SHA-512",
                    _ => ""
                };

                var privProto = user.PrivProtocol switch
                {
                    SnmpPrivProtocol.DES => "DES",
                    SnmpPrivProtocol.AES128 => "AES",
                    SnmpPrivProtocol.AES192 => "AES-192",
                    SnmpPrivProtocol.AES256 => "AES-256",
                    _ => ""
                };

                // Note: Les utilisateurs SNMPv3 sont créés séparément avec net-snmp-config
                config += $"# User: {user.Username} (Security Level: {user.SecurityLevel})\n";
            }
        }

        // Trap receivers
        if (_config.TrapReceivers.Any())
        {
            config += "\n# Trap destinations\n";
            foreach (var receiver in _config.TrapReceivers.Where(r => r.Enabled))
            {
                if (receiver.Version == SnmpVersion.V1)
                {
                    config += $"trapsink {receiver.Address}:{receiver.Port} {receiver.Community ?? _config.ReadCommunity}\n";
                }
                else
                {
                    config += $"trap2sink {receiver.Address}:{receiver.Port} {receiver.Community ?? _config.ReadCommunity}\n";
                }
            }
        }

        // Views et accès
        config += @"

# Views
view   systemonly  included   .1.3.6.1.2.1.1
view   systemonly  included   .1.3.6.1.2.1.25.1
view   all         included   .1

# Access control
";

        // Disk and process monitoring
        config += @"
# Disk monitoring
disk / 10%
disk /var 10%

# Process monitoring
proc sshd
proc snmpd

# System statistics
extend-sh distro /usr/bin/cat /etc/os-release | grep PRETTY_NAME | cut -d= -f2 | tr -d '""'
";

        return config;
    }

    private async Task CreateSnmpV3UsersAsync()
    {
        // Arrêter le service pour créer les utilisateurs
        await ExecuteCommandAsync("systemctl", "stop snmpd");

        foreach (var user in _config.Users)
        {
            var cmd = $"createUser {user.Username}";

            if (user.SecurityLevel >= SnmpSecurityLevel.AuthNoPriv && 
                user.AuthProtocol != SnmpAuthProtocol.None)
            {
                var authProto = user.AuthProtocol switch
                {
                    SnmpAuthProtocol.MD5 => "MD5",
                    SnmpAuthProtocol.SHA => "SHA",
                    _ => "SHA"
                };
                cmd += $" {authProto} \"{user.AuthPassword}\"";

                if (user.SecurityLevel == SnmpSecurityLevel.AuthPriv && 
                    user.PrivProtocol != SnmpPrivProtocol.None)
                {
                    var privProto = user.PrivProtocol switch
                    {
                        SnmpPrivProtocol.DES => "DES",
                        SnmpPrivProtocol.AES128 => "AES",
                        _ => "AES"
                    };
                    cmd += $" {privProto} \"{user.PrivPassword}\"";
                }
            }

            // Écrire dans /var/lib/snmp/snmpd.conf
            try
            {
                await File.AppendAllTextAsync("/var/lib/snmp/snmpd.conf", cmd + "\n");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SNMPv3 user {User}", user.Username);
            }
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<SnmpConfig>(json) ?? new SnmpConfig();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load SNMP configuration");
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
            _logger.LogError(ex, "Failed to save SNMP configuration");
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
