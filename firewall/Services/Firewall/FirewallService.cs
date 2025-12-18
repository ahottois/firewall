using System.Runtime.InteropServices;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services.Firewall;

/// <summary>
/// Factory et facade pour le moteur de firewall
/// Sélectionne automatiquement l'implémentation appropriée selon l'OS
/// </summary>
public class FirewallEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FirewallEngineFactory> _logger;

    public FirewallEngineFactory(IServiceProvider serviceProvider, ILogger<FirewallEngineFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IFirewallEngine CreateEngine()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogInformation("Utilisation du moteur Windows Firewall");
            return _serviceProvider.GetRequiredService<WindowsFirewallEngine>();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("Utilisation du moteur Linux iptables");
            return _serviceProvider.GetRequiredService<LinuxIptablesEngine>();
        }
        else
        {
            _logger.LogWarning("Plateforme non supportée: {OS}", RuntimeInformation.OSDescription);
            throw new PlatformNotSupportedException($"Le firewall n'est pas supporté sur {RuntimeInformation.OSDescription}");
        }
    }
}

/// <summary>
/// Service de blocage réseau amélioré avec support multi-plateforme
/// </summary>
public class FirewallService : INetworkBlockingService
{
    private readonly IFirewallEngine _engine;
    private readonly ILogger<FirewallService> _logger;

    public bool IsSupported => _engine.IsSupported;

    public FirewallService(FirewallEngineFactory factory, ILogger<FirewallService> logger)
    {
        _logger = logger;
        try
        {
            _engine = factory.CreateEngine();
            _logger.LogInformation("Moteur de firewall initialisé: {Engine}", _engine.EngineName);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Firewall non supporté sur cette plateforme");
            _engine = new NullFirewallEngine();
        }
    }

    public async Task<bool> BlockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        var result = await _engine.BlockDeviceAsync(macAddress, ipAddress);
        if (!result.Success)
        {
            _logger.LogWarning("Échec du blocage: {Message} - {Details}", result.Message, result.ErrorDetails);
        }
        return result.Success;
    }

    public async Task<bool> UnblockDeviceAsync(string macAddress, string? ipAddress = null)
    {
        var result = await _engine.UnblockDeviceAsync(macAddress, ipAddress);
        if (!result.Success)
        {
            _logger.LogWarning("Échec du déblocage: {Message} - {Details}", result.Message, result.ErrorDetails);
        }
        return result.Success;
    }

    public async Task<IEnumerable<string>> GetBlockedMacsAsync()
    {
        var rules = await _engine.GetActiveRulesAsync();
        return rules.Select(r => r.MacAddress);
    }
}

/// <summary>
/// Implémentation nulle pour les plateformes non supportées
/// </summary>
public class NullFirewallEngine : IFirewallEngine
{
    public bool IsSupported => false;
    public string EngineName => "Null (non supporté)";

    public Task<FirewallResult> BlockDeviceAsync(string macAddress, string? ipAddress = null)
        => Task.FromResult(FirewallResult.Fail("Plateforme non supportée", FirewallErrorCode.UnsupportedPlatform));

    public Task<FirewallResult> UnblockDeviceAsync(string macAddress, string? ipAddress = null)
        => Task.FromResult(FirewallResult.Fail("Plateforme non supportée", FirewallErrorCode.UnsupportedPlatform));

    public Task<bool> IsDeviceBlockedAsync(string macAddress)
        => Task.FromResult(false);

    public Task<IEnumerable<FirewallRule>> GetActiveRulesAsync()
        => Task.FromResult<IEnumerable<FirewallRule>>(Array.Empty<FirewallRule>());

    public Task<int> RestoreRulesFromDatabaseAsync(IEnumerable<NetworkDevice> blockedDevices)
        => Task.FromResult(0);

    public Task<FirewallResult> ClearAllRulesAsync()
        => Task.FromResult(FirewallResult.Fail("Plateforme non supportée", FirewallErrorCode.UnsupportedPlatform));

    public Task<bool> CheckPermissionsAsync()
        => Task.FromResult(false);
}
