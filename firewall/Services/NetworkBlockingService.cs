using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkFirewall.Services;

/// <summary>
/// Interface pour le service de blocage réseau
/// </summary>
public interface INetworkBlockingService
{
    Task<bool> BlockDeviceAsync(string macAddress, string? ipAddress = null);
    Task<bool> UnblockDeviceAsync(string macAddress, string? ipAddress = null);
    Task<IEnumerable<string>> GetBlockedMacsAsync();
    bool IsSupported { get; }
}

/// <summary>
/// Service de blocage réseau utilisant iptables (Linux) ou netsh (Windows)
/// </summary>
public class NetworkBlockingService : INetworkBlockingService
{
    private readonly ILogger<NetworkBlockingService> _logger;
    private readonly HashSet<string> _blockedMacs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                               RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public NetworkBlockingService(ILogger<NetworkBlockingService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> BlockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        macAddress = NormalizeMac(macAddress);
        
        await _lock.WaitAsync();
        try
        {
            if (_blockedMacs.Contains(macAddress))
            {
                _logger.LogWarning("L'appareil {Mac} est déjà bloqué", macAddress);
                return true;
            }

            bool success;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                success = await BlockWithIptablesAsync(macAddress, ipAddress);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                success = await BlockWithWindowsFirewallAsync(macAddress, ipAddress);
            }
            else
            {
                _logger.LogError("Plateforme non supportée pour le blocage réseau");
                return false;
            }

            if (success)
            {
                _blockedMacs.Add(macAddress);
                _logger.LogInformation("Appareil {Mac} ({Ip}) bloqué avec succès", macAddress, ipAddress ?? "N/A");
            }

            return success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> UnblockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        macAddress = NormalizeMac(macAddress);
        
        await _lock.WaitAsync();
        try
        {
            if (!_blockedMacs.Contains(macAddress))
            {
                _logger.LogWarning("L'appareil {Mac} n'est pas bloqué", macAddress);
                return true;
            }

            bool success;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                success = await UnblockWithIptablesAsync(macAddress, ipAddress);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                success = await UnblockWithWindowsFirewallAsync(macAddress, ipAddress);
            }
            else
            {
                return false;
            }

            if (success)
            {
                _blockedMacs.Remove(macAddress);
                _logger.LogInformation("Appareil {Mac} débloqué avec succès", macAddress);
            }

            return success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IEnumerable<string>> GetBlockedMacsAsync()
    {
        return Task.FromResult<IEnumerable<string>>(_blockedMacs.ToList());
    }

    #region Linux (iptables/ebtables)

    private async Task<bool> BlockWithIptablesAsync(string mac, string? ip)
    {
        try
        {
            // Bloquer par MAC avec ebtables (bridge filtering)
            var ebtablesResult = await RunCommandAsync("ebtables", $"-A FORWARD -s {mac} -j DROP");
            
            // Bloquer également avec iptables si on a l'IP
            if (!string.IsNullOrEmpty(ip))
            {
                // Bloquer le trafic entrant de cette IP
                await RunCommandAsync("iptables", $"-A INPUT -s {ip} -j DROP");
                // Bloquer le trafic sortant vers cette IP  
                await RunCommandAsync("iptables", $"-A OUTPUT -d {ip} -j DROP");
                // Bloquer le forwarding
                await RunCommandAsync("iptables", $"-A FORWARD -s {ip} -j DROP");
                await RunCommandAsync("iptables", $"-A FORWARD -d {ip} -j DROP");
            }

            // Alternative: bloquer par MAC avec iptables (fonctionne sur certaines configs)
            await RunCommandAsync("iptables", $"-A INPUT -m mac --mac-source {mac} -j DROP");
            await RunCommandAsync("iptables", $"-A FORWARD -m mac --mac-source {mac} -j DROP");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du blocage avec iptables pour {Mac}", mac);
            return false;
        }
    }

    private async Task<bool> UnblockWithIptablesAsync(string mac, string? ip)
    {
        try
        {
            // Supprimer les règles ebtables
            await RunCommandAsync("ebtables", $"-D FORWARD -s {mac} -j DROP");

            // Supprimer les règles iptables
            if (!string.IsNullOrEmpty(ip))
            {
                await RunCommandAsync("iptables", $"-D INPUT -s {ip} -j DROP");
                await RunCommandAsync("iptables", $"-D OUTPUT -d {ip} -j DROP");
                await RunCommandAsync("iptables", $"-D FORWARD -s {ip} -j DROP");
                await RunCommandAsync("iptables", $"-D FORWARD -d {ip} -j DROP");
            }

            await RunCommandAsync("iptables", $"-D INPUT -m mac --mac-source {mac} -j DROP");
            await RunCommandAsync("iptables", $"-D FORWARD -m mac --mac-source {mac} -j DROP");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du déblocage avec iptables pour {Mac}", mac);
            return false;
        }
    }

    #endregion

    #region Windows (netsh/Windows Firewall)

    private async Task<bool> BlockWithWindowsFirewallAsync(string mac, string? ip)
    {
        try
        {
            // Windows Firewall ne supporte pas le blocage par MAC directement
            // On bloque par IP si disponible
            if (string.IsNullOrEmpty(ip))
            {
                _logger.LogWarning("Windows ne supporte pas le blocage par MAC sans IP");
                // On peut quand même ajouter à notre liste interne
                return true;
            }

            var ruleName = $"WebGuard_Block_{mac.Replace(":", "")}";

            // Bloquer le trafic entrant
            var inResult = await RunCommandAsync("netsh", 
                $"advfirewall firewall add rule name=\"{ruleName}_IN\" dir=in action=block remoteip={ip}");

            // Bloquer le trafic sortant
            var outResult = await RunCommandAsync("netsh",
                $"advfirewall firewall add rule name=\"{ruleName}_OUT\" dir=out action=block remoteip={ip}");

            return inResult.ExitCode == 0 || outResult.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du blocage avec Windows Firewall pour {Mac}", mac);
            return false;
        }
    }

    private async Task<bool> UnblockWithWindowsFirewallAsync(string mac, string? ip)
    {
        try
        {
            var ruleName = $"WebGuard_Block_{mac.Replace(":", "")}";

            // Supprimer les règles
            await RunCommandAsync("netsh", $"advfirewall firewall delete rule name=\"{ruleName}_IN\"");
            await RunCommandAsync("netsh", $"advfirewall firewall delete rule name=\"{ruleName}_OUT\"");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du déblocage avec Windows Firewall pour {Mac}", mac);
            return false;
        }
    }

    #endregion

    private async Task<CommandResult> RunCommandAsync(string command, string arguments)
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

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.ExitCode = -1;
                result.Error = "Impossible de démarrer le processus";
                return result;
            }

            result.Output = await process.StandardOutput.ReadToEndAsync();
            result.Error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            result.ExitCode = process.ExitCode;

            _logger.LogDebug("Commande: {Cmd} {Args} -> Exit: {Exit}", command, arguments, result.ExitCode);
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Error = ex.Message;
        }

        return result;
    }

    private static string NormalizeMac(string mac)
    {
        return mac.Replace("-", ":").ToUpperInvariant();
    }

    private class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
