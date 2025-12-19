using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface ISshService
{
    SshConfig GetConfig();
    Task UpdateConfigAsync(SshConfig config);
    Task<IEnumerable<SshSession>> GetActiveSessionsAsync();
    Task<IEnumerable<SshAuthorizedKey>> GetAuthorizedKeysAsync();
    Task<SshAuthorizedKey> AddAuthorizedKeyAsync(string name, string publicKey);
    Task DeleteAuthorizedKeyAsync(int id);
    Task DisconnectSessionAsync(int pid);
    Task RestartSshServiceAsync();
}

public class SshService : ISshService
{
    private readonly ILogger<SshService> _logger;
    private SshConfig _config = new();
    private readonly List<SshAuthorizedKey> _authorizedKeys = new();
    private int _keyIdCounter = 1;

    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const string ConfigPath = "ssh_config.json";
    private const string SshdConfigPath = "/etc/ssh/sshd_config";
    private const string AuthorizedKeysPath = "/root/.ssh/authorized_keys";

    public SshService(ILogger<SshService> logger)
    {
        _logger = logger;
        LoadConfig();
        LoadAuthorizedKeys();
    }

    public SshConfig GetConfig() => _config;

    public async Task UpdateConfigAsync(SshConfig config)
    {
        _config = config;
        SaveConfig();

        if (IsLinux)
        {
            await ApplySshConfigAsync();
        }

        _logger.LogInformation("SSH configuration updated (Enabled: {Enabled}, Port: {Port})", 
            config.Enabled, config.Port);
    }

    public async Task<IEnumerable<SshSession>> GetActiveSessionsAsync()
    {
        var sessions = new List<SshSession>();

        if (!IsLinux) return sessions;

        try
        {
            // Utiliser 'who' pour obtenir les sessions
            var whoOutput = await ExecuteCommandAsync("who", "");
            var lines = whoOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    var session = new SshSession
                    {
                        User = parts[0],
                        Terminal = parts[1]
                    };

                    // Extraire l'adresse IP (entre parenthèses)
                    var match = Regex.Match(line, @"\(([^)]+)\)");
                    if (match.Success)
                    {
                        session.RemoteAddress = match.Groups[1].Value;
                    }

                    // Obtenir le PID du processus SSH
                    var psOutput = await ExecuteCommandAsync("pgrep", $"-t {session.Terminal}");
                    if (int.TryParse(psOutput.Trim(), out var pid))
                    {
                        session.Pid = pid;
                    }

                    sessions.Add(session);
                }
            }

            // Méthode alternative avec ss
            var ssOutput = await ExecuteCommandAsync("ss", "-tnp | grep :22");
            // Parser les connexions actives
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get SSH sessions");
        }

        return sessions;
    }

    public Task<IEnumerable<SshAuthorizedKey>> GetAuthorizedKeysAsync()
    {
        return Task.FromResult(_authorizedKeys.AsEnumerable());
    }

    public async Task<SshAuthorizedKey> AddAuthorizedKeyAsync(string name, string publicKey)
    {
        // Valider et parser la clé publique
        var parts = publicKey.Trim().Split(' ');
        if (parts.Length < 2)
            throw new ArgumentException("Invalid public key format");

        var keyType = parts[0];
        var validTypes = new[] { "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521" };
        
        if (!validTypes.Contains(keyType))
            throw new ArgumentException($"Unsupported key type: {keyType}");

        var key = new SshAuthorizedKey
        {
            Id = _keyIdCounter++,
            Name = name,
            PublicKey = publicKey.Trim(),
            KeyType = keyType,
            Fingerprint = ComputeKeyFingerprint(parts[1]),
            AddedAt = DateTime.UtcNow
        };

        _authorizedKeys.Add(key);
        SaveConfig();

        if (IsLinux)
        {
            await UpdateAuthorizedKeysFileAsync();
        }

        _logger.LogInformation("SSH key added: {Name} ({Type})", name, keyType);
        return key;
    }

    public async Task DeleteAuthorizedKeyAsync(int id)
    {
        var key = _authorizedKeys.FirstOrDefault(k => k.Id == id);
        if (key == null)
            throw new KeyNotFoundException($"SSH key {id} not found");

        _authorizedKeys.Remove(key);
        SaveConfig();

        if (IsLinux)
        {
            await UpdateAuthorizedKeysFileAsync();
        }

        _logger.LogInformation("SSH key deleted: {Name}", key.Name);
    }

    public async Task DisconnectSessionAsync(int pid)
    {
        if (!IsLinux) return;

        try
        {
            await ExecuteCommandAsync("kill", $"-HUP {pid}");
            _logger.LogInformation("SSH session terminated: PID {Pid}", pid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate SSH session {Pid}", pid);
            throw;
        }
    }

    public async Task RestartSshServiceAsync()
    {
        if (!IsLinux) return;

        try
        {
            await ExecuteCommandAsync("systemctl", "restart sshd");
            _logger.LogInformation("SSH service restarted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart SSH service");
            throw;
        }
    }

    private async Task ApplySshConfigAsync()
    {
        try
        {
            // Lire le fichier de configuration existant
            var existingConfig = File.Exists(SshdConfigPath) 
                ? await File.ReadAllTextAsync(SshdConfigPath) 
                : "";

            // Générer la nouvelle configuration
            var newConfig = GenerateSshdConfig();

            // Sauvegarder une copie de l'ancien fichier
            if (File.Exists(SshdConfigPath))
            {
                File.Copy(SshdConfigPath, SshdConfigPath + ".bak", true);
            }

            // Écrire la nouvelle configuration
            await File.WriteAllTextAsync(SshdConfigPath, newConfig);

            // Valider la configuration
            var testResult = await ExecuteCommandAsync("sshd", "-t");
            if (!string.IsNullOrEmpty(testResult) && testResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                // Restaurer l'ancienne configuration
                if (File.Exists(SshdConfigPath + ".bak"))
                {
                    File.Copy(SshdConfigPath + ".bak", SshdConfigPath, true);
                }
                throw new InvalidOperationException($"Invalid SSH configuration: {testResult}");
            }

            // Recharger le service SSH
            await ExecuteCommandAsync("systemctl", "reload sshd");

            _logger.LogInformation("SSH configuration applied successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply SSH configuration");
            throw;
        }
    }

    private string GenerateSshdConfig()
    {
        var config = $@"# NetGuard SSH Configuration
# Generated at {DateTime.UtcNow:u}

Port {_config.Port}
Protocol 2

# Authentication
PermitRootLogin {(_config.RootLogin ? "yes" : "no")}
PasswordAuthentication {(_config.PasswordAuthentication ? "yes" : "no")}
PubkeyAuthentication {(_config.PubkeyAuthentication ? "yes" : "no")}
MaxAuthTries {_config.MaxAuthTries}
LoginGraceTime {_config.LoginGraceTime}

# Session
ClientAliveInterval {_config.ClientAliveInterval}
ClientAliveCountMax {_config.ClientAliveCountMax}

# Security
PermitEmptyPasswords no
ChallengeResponseAuthentication no
UsePAM yes

# Logging
SyslogFacility AUTH
LogLevel INFO

# X11 and Forwarding
X11Forwarding no
AllowAgentForwarding yes
AllowTcpForwarding yes

# Host keys
HostKey /etc/ssh/ssh_host_rsa_key
HostKey /etc/ssh/ssh_host_ecdsa_key
HostKey /etc/ssh/ssh_host_ed25519_key
";

        // Ajouter les utilisateurs autorisés
        if (_config.AllowedUsers.Any())
        {
            config += $"\nAllowUsers {string.Join(" ", _config.AllowedUsers)}";
        }

        // Ajouter la bannière
        if (!string.IsNullOrEmpty(_config.Banner))
        {
            config += "\nBanner /etc/ssh/banner";
        }

        // Restriction par IP
        if (_config.AllowedIPs.Any())
        {
            // Ceci nécessite généralement une configuration avec Match
            config += $@"

# IP Restrictions
Match Address {string.Join(",", _config.AllowedIPs)}
    PermitRootLogin {(_config.RootLogin ? "yes" : "no")}
";
        }

        if (_config.DeniedIPs.Any())
        {
            config += $@"

Match Address {string.Join(",", _config.DeniedIPs)}
    DenyUsers *
";
        }

        return config;
    }

    private async Task UpdateAuthorizedKeysFileAsync()
    {
        try
        {
            // S'assurer que le répertoire .ssh existe
            var sshDir = Path.GetDirectoryName(AuthorizedKeysPath);
            if (!string.IsNullOrEmpty(sshDir) && !Directory.Exists(sshDir))
            {
                Directory.CreateDirectory(sshDir);
                await ExecuteCommandAsync("chmod", "700 " + sshDir);
            }

            // Générer le contenu du fichier
            var content = "# NetGuard Authorized Keys\n";
            content += $"# Generated at {DateTime.UtcNow:u}\n\n";

            foreach (var key in _authorizedKeys)
            {
                content += $"# {key.Name} (Added: {key.AddedAt:u})\n";
                content += $"{key.PublicKey}\n\n";
            }

            await File.WriteAllTextAsync(AuthorizedKeysPath, content);
            await ExecuteCommandAsync("chmod", "600 " + AuthorizedKeysPath);

            _logger.LogInformation("Authorized keys file updated ({Count} keys)", _authorizedKeys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update authorized keys file");
        }
    }

    private string ComputeKeyFingerprint(string base64Key)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Key);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
        }
        catch
        {
            return "Unknown";
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var data = JsonSerializer.Deserialize<SshConfigData>(json);
                if (data != null)
                {
                    _config = data.Config ?? new SshConfig();
                    _authorizedKeys.AddRange(data.AuthorizedKeys ?? new List<SshAuthorizedKey>());
                    _keyIdCounter = _authorizedKeys.Any() ? _authorizedKeys.Max(k => k.Id) + 1 : 1;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load SSH configuration");
        }
    }

    private void LoadAuthorizedKeys()
    {
        if (!IsLinux || !File.Exists(AuthorizedKeysPath)) return;

        try
        {
            var lines = File.ReadAllLines(AuthorizedKeysPath);
            string? currentName = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("# ") && !line.Contains("Generated") && !line.Contains("Authorized"))
                {
                    currentName = line.Substring(2).Split('(')[0].Trim();
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 2)
                    {
                        var key = new SshAuthorizedKey
                        {
                            Id = _keyIdCounter++,
                            Name = currentName ?? "Imported Key",
                            PublicKey = line.Trim(),
                            KeyType = parts[0],
                            Fingerprint = ComputeKeyFingerprint(parts[1])
                        };

                        if (!_authorizedKeys.Any(k => k.Fingerprint == key.Fingerprint))
                        {
                            _authorizedKeys.Add(key);
                        }
                    }
                    currentName = null;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load existing authorized keys");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var data = new SshConfigData
            {
                Config = _config,
                AuthorizedKeys = _authorizedKeys
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save SSH configuration");
        }
    }

    private class SshConfigData
    {
        public SshConfig? Config { get; set; }
        public List<SshAuthorizedKey>? AuthorizedKeys { get; set; }
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
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return string.IsNullOrEmpty(error) ? output : error;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Command failed: {Cmd} {Args}", command, arguments);
            return string.Empty;
        }
    }
}
