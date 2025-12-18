using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services.Firewall;

/// <summary>
/// Implémentation du moteur de firewall pour Linux utilisant iptables/nftables
/// </summary>
public class LinuxIptablesEngine : IFirewallEngine
{
    private readonly ILogger<LinuxIptablesEngine> _logger;
    private readonly ConcurrentDictionary<string, FirewallRule> _activeRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string CHAIN_NAME = "WEBGUARD_BLOCK";
    private bool _chainInitialized = false;

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public string EngineName => "Linux iptables/nftables";

    public LinuxIptablesEngine(ILogger<LinuxIptablesEngine> logger)
    {
        _logger = logger;
    }

    public async Task<FirewallResult> BlockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        if (!IsSupported)
            return FirewallResult.Fail("iptables non disponible sur cette plateforme", FirewallErrorCode.UnsupportedPlatform);

        macAddress = NormalizeMac(macAddress);

        await _lock.WaitAsync();
        try
        {
            // Initialiser la chaîne personnalisée si nécessaire
            await EnsureChainExistsAsync();

            if (_activeRules.ContainsKey(macAddress))
            {
                return FirewallResult.Fail("Appareil déjà bloqué", FirewallErrorCode.AlreadyBlocked);
            }

            var commands = new List<(string cmd, string args)>();

            // Bloquer par adresse MAC (plus fiable car ne change pas)
            commands.Add(("iptables", $"-A {CHAIN_NAME} -m mac --mac-source {macAddress} -j DROP"));
            
            // Bloquer également par IP si disponible (pour le trafic sortant)
            if (!string.IsNullOrEmpty(ipAddress))
            {
                commands.Add(("iptables", $"-A {CHAIN_NAME} -s {ipAddress} -j DROP"));
                commands.Add(("iptables", $"-A {CHAIN_NAME} -d {ipAddress} -j DROP"));
            }

            // Essayer ebtables pour le blocage au niveau bridge (layer 2)
            commands.Add(("ebtables", $"-A FORWARD -s {macAddress} -j DROP"));

            bool anySuccess = false;
            var errors = new List<string>();

            foreach (var (cmd, args) in commands)
            {
                var result = await RunCommandAsync(cmd, args);
                if (result.Success)
                    anySuccess = true;
                else if (!string.IsNullOrEmpty(result.Error))
                    errors.Add($"{cmd}: {result.Error}");
            }

            if (!anySuccess)
            {
                return FirewallResult.Fail(
                    "Échec du blocage via iptables",
                    FirewallErrorCode.CommandFailed,
                    string.Join("; ", errors));
            }

            _activeRules.TryAdd(macAddress, new FirewallRule
            {
                RuleName = $"BLOCK_{macAddress.Replace(":", "")}",
                MacAddress = macAddress,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow,
                Direction = FirewallRuleDirection.Both,
                Action = FirewallRuleAction.Block
            });

            _logger.LogInformation("Appareil bloqué via iptables: MAC={Mac}, IP={Ip}", macAddress, ipAddress ?? "N/A");
            return FirewallResult.Ok($"Appareil {macAddress} bloqué avec succès");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FirewallResult> UnblockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        if (!IsSupported)
            return FirewallResult.Fail("iptables non disponible", FirewallErrorCode.UnsupportedPlatform);

        macAddress = NormalizeMac(macAddress);

        await _lock.WaitAsync();
        try
        {
            // Supprimer les règles iptables
            await RunCommandAsync("iptables", $"-D {CHAIN_NAME} -m mac --mac-source {macAddress} -j DROP");
            
            if (!string.IsNullOrEmpty(ipAddress))
            {
                await RunCommandAsync("iptables", $"-D {CHAIN_NAME} -s {ipAddress} -j DROP");
                await RunCommandAsync("iptables", $"-D {CHAIN_NAME} -d {ipAddress} -j DROP");
            }

            // Supprimer la règle ebtables
            await RunCommandAsync("ebtables", $"-D FORWARD -s {macAddress} -j DROP");

            _activeRules.TryRemove(macAddress, out _);

            _logger.LogInformation("Appareil débloqué: MAC={Mac}", macAddress);
            return FirewallResult.Ok($"Appareil {macAddress} débloqué avec succès");
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<bool> IsDeviceBlockedAsync(string macAddress)
    {
        macAddress = NormalizeMac(macAddress);
        return Task.FromResult(_activeRules.ContainsKey(macAddress));
    }

    public Task<IEnumerable<FirewallRule>> GetActiveRulesAsync()
    {
        return Task.FromResult<IEnumerable<FirewallRule>>(_activeRules.Values.ToList());
    }

    public async Task<int> RestoreRulesFromDatabaseAsync(IEnumerable<NetworkDevice> blockedDevices)
    {
        int restoredCount = 0;

        foreach (var device in blockedDevices.Where(d => d.Status == DeviceStatus.Blocked || d.IsBlocked))
        {
            var result = await BlockDeviceAsync(device.MacAddress, device.IpAddress);
            if (result.Success)
                restoredCount++;
            else
                _logger.LogWarning("Échec de restauration de la règle pour {Mac}: {Error}", device.MacAddress, result.Message);
        }

        _logger.LogInformation("Restauration des règles iptables: {Count} règles appliquées", restoredCount);
        return restoredCount;
    }

    public async Task<FirewallResult> ClearAllRulesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Vider la chaîne personnalisée
            await RunCommandAsync("iptables", $"-F {CHAIN_NAME}");
            
            // Supprimer toutes les règles ebtables FORWARD avec DROP
            var listResult = await RunCommandAsync("ebtables", "-L FORWARD --Lx");
            if (listResult.Success)
            {
                var lines = listResult.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("-j DROP") && line.Contains("-s"))
                    {
                        // Extraire la commande de suppression
                        var deleteCmd = line.Replace("-A", "-D");
                        await RunCommandAsync("ebtables", deleteCmd);
                    }
                }
            }

            _activeRules.Clear();
            _logger.LogInformation("Toutes les règles WebGuard iptables ont été supprimées");
            return FirewallResult.Ok("Toutes les règles ont été supprimées");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> CheckPermissionsAsync()
    {
        var result = await RunCommandAsync("iptables", "-L -n");
        return result.Success;
    }

    private async Task EnsureChainExistsAsync()
    {
        if (_chainInitialized)
            return;

        // Créer la chaîne si elle n'existe pas
        await RunCommandAsync("iptables", $"-N {CHAIN_NAME}");
        
        // Insérer les références vers la chaîne dans INPUT et FORWARD
        await RunCommandAsync("iptables", $"-I INPUT -j {CHAIN_NAME}");
        await RunCommandAsync("iptables", $"-I FORWARD -j {CHAIN_NAME}");
        await RunCommandAsync("iptables", $"-I OUTPUT -j {CHAIN_NAME}");

        _chainInitialized = true;
        _logger.LogInformation("Chaîne iptables {Chain} initialisée", CHAIN_NAME);
    }

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
                result.Success = false;
                result.Error = $"Impossible de démarrer {command}";
                return result;
            }

            result.Output = await process.StandardOutput.ReadToEndAsync();
            result.Error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            result.Success = process.ExitCode == 0;

            _logger.LogDebug("{Cmd} {Args} -> ExitCode={Exit}", command, arguments, process.ExitCode);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogDebug("Commande {Cmd} non disponible: {Error}", command, ex.Message);
        }

        return result;
    }

    private static string NormalizeMac(string mac) => mac.Replace("-", ":").ToUpperInvariant();

    private class CommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
