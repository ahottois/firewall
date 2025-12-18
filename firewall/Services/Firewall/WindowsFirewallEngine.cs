using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services.Firewall;

/// <summary>
/// Implémentation du moteur de firewall pour Windows utilisant netsh advfirewall
/// </summary>
public class WindowsFirewallEngine : IFirewallEngine
{
    private readonly ILogger<WindowsFirewallEngine> _logger;
    private readonly ISecurityLogService? _securityLogService;
    private readonly ConcurrentDictionary<string, FirewallRule> _activeRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string RULE_PREFIX = "WebGuard_Block_";

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public string EngineName => "Windows Firewall (netsh advfirewall)";

    public WindowsFirewallEngine(ILogger<WindowsFirewallEngine> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        // Récupérer le service de log de manière optionnelle pour éviter les dépendances circulaires
        _securityLogService = serviceProvider.GetService<ISecurityLogService>();
    }

    public async Task<FirewallResult> BlockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        if (!IsSupported)
            return FirewallResult.Fail("Windows Firewall non disponible sur cette plateforme", FirewallErrorCode.UnsupportedPlatform);

        macAddress = NormalizeMac(macAddress);

        if (string.IsNullOrEmpty(ipAddress))
        {
            _logger.LogWarning("Windows Firewall nécessite une adresse IP pour bloquer efficacement. MAC: {Mac}", macAddress);
            // On enregistre quand même la règle pour la cohérence
            _activeRules.TryAdd(macAddress, new FirewallRule
            {
                RuleName = GetRuleName(macAddress),
                MacAddress = macAddress,
                IpAddress = null,
                CreatedAt = DateTime.UtcNow,
                Direction = FirewallRuleDirection.Both,
                Action = FirewallRuleAction.Block
            });
            return FirewallResult.Ok("Règle enregistrée (blocage par IP nécessaire sur Windows)");
        }

        await _lock.WaitAsync();
        try
        {
            if (_activeRules.ContainsKey(macAddress))
            {
                return FirewallResult.Fail("Appareil déjà bloqué", FirewallErrorCode.AlreadyBlocked);
            }

            var ruleName = GetRuleName(macAddress);

            // Créer les règles d'entrée et de sortie
            var inboundResult = await RunNetshCommandAsync(
                $"advfirewall firewall add rule name=\"{ruleName}_IN\" dir=in action=block remoteip={ipAddress} enable=yes");

            var outboundResult = await RunNetshCommandAsync(
                $"advfirewall firewall add rule name=\"{ruleName}_OUT\" dir=out action=block remoteip={ipAddress} enable=yes");

            if (!inboundResult.Success && !outboundResult.Success)
            {
                return FirewallResult.Fail(
                    "Échec de la création des règles firewall",
                    FirewallErrorCode.CommandFailed,
                    $"IN: {inboundResult.Error}, OUT: {outboundResult.Error}");
            }

            _activeRules.TryAdd(macAddress, new FirewallRule
            {
                RuleName = ruleName,
                MacAddress = macAddress,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow,
                Direction = FirewallRuleDirection.Both,
                Action = FirewallRuleAction.Block
            });

            _logger.LogInformation("Appareil bloqué via Windows Firewall: MAC={Mac}, IP={Ip}", macAddress, ipAddress);

            // Logger l'événement de sécurité
            if (_securityLogService != null)
            {
                await _securityLogService.LogFirewallRuleAddedAsync(ruleName, macAddress, ipAddress);
            }

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
            return FirewallResult.Fail("Windows Firewall non disponible", FirewallErrorCode.UnsupportedPlatform);

        macAddress = NormalizeMac(macAddress);

        await _lock.WaitAsync();
        try
        {
            var ruleName = GetRuleName(macAddress);

            // Supprimer les règles
            var inResult = await RunNetshCommandAsync($"advfirewall firewall delete rule name=\"{ruleName}_IN\"");
            var outResult = await RunNetshCommandAsync($"advfirewall firewall delete rule name=\"{ruleName}_OUT\"");

            _activeRules.TryRemove(macAddress, out _);

            _logger.LogInformation("Appareil débloqué: MAC={Mac}", macAddress);

            // Logger l'événement de sécurité
            if (_securityLogService != null)
            {
                await _securityLogService.LogFirewallRuleRemovedAsync(ruleName, macAddress, ipAddress);
            }

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

        _logger.LogInformation("Restauration des règles firewall: {Count} règles appliquées", restoredCount);
        
        // Logger l'événement système
        if (_securityLogService != null && restoredCount > 0)
        {
            await _securityLogService.LogSystemEventAsync(
                $"Restauration de {restoredCount} règles de blocage au démarrage",
                LogSeverity.Info);
        }

        return restoredCount;
    }

    public async Task<FirewallResult> ClearAllRulesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Lister toutes les règles WebGuard et les supprimer
            var listResult = await RunNetshCommandAsync("advfirewall firewall show rule name=all");
            
            if (listResult.Success)
            {
                var lines = listResult.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains(RULE_PREFIX))
                    {
                        var match = Regex.Match(line, @"Rule Name:\s*(.+)");
                        if (match.Success)
                        {
                            var ruleName = match.Groups[1].Value.Trim();
                            await RunNetshCommandAsync($"advfirewall firewall delete rule name=\"{ruleName}\"");
                        }
                    }
                }
            }

            var ruleCount = _activeRules.Count;
            _activeRules.Clear();
            
            _logger.LogInformation("Toutes les règles WebGuard ont été supprimées");

            // Logger l'événement système
            if (_securityLogService != null && ruleCount > 0)
            {
                await _securityLogService.LogSystemEventAsync(
                    $"Suppression de {ruleCount} règles de blocage",
                    LogSeverity.Warning);
            }

            return FirewallResult.Ok("Toutes les règles ont été supprimées");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> CheckPermissionsAsync()
    {
        var result = await RunNetshCommandAsync("advfirewall show currentprofile");
        return result.Success;
    }

    private async Task<CommandResult> RunNetshCommandAsync(string arguments)
    {
        var result = new CommandResult();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas" // Essayer d'exécuter en tant qu'admin
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Success = false;
                result.Error = "Impossible de démarrer netsh";
                return result;
            }

            result.Output = await process.StandardOutput.ReadToEndAsync();
            result.Error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            result.Success = process.ExitCode == 0;

            _logger.LogDebug("netsh {Args} -> ExitCode={Exit}", arguments, process.ExitCode);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Erreur lors de l'exécution de netsh");
        }

        return result;
    }

    private static string NormalizeMac(string mac) => mac.Replace("-", ":").ToUpperInvariant();
    private static string GetRuleName(string mac) => $"{RULE_PREFIX}{mac.Replace(":", "")}";

    private class CommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
