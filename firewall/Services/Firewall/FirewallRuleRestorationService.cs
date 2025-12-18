using NetworkFirewall.Data;
using NetworkFirewall.Models;
using NetworkFirewall.Services.Firewall;

namespace NetworkFirewall.Services;

/// <summary>
/// Service d'arrière-plan pour restaurer les règles de blocage au démarrage
/// et synchroniser l'état du firewall avec la base de données
/// </summary>
public class FirewallRuleRestorationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirewallRuleRestorationService> _logger;
    private readonly IFirewallEngine _firewallEngine;

    public FirewallRuleRestorationService(
        IServiceScopeFactory scopeFactory,
        ILogger<FirewallRuleRestorationService> logger,
        FirewallEngineFactory engineFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        try
        {
            _firewallEngine = engineFactory.CreateEngine();
        }
        catch (PlatformNotSupportedException)
        {
            _firewallEngine = new NullFirewallEngine();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Attendre un peu pour que la base de données soit prête
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation("Démarrage de la restauration des règles de firewall...");

        if (!_firewallEngine.IsSupported)
        {
            _logger.LogWarning("Moteur de firewall non supporté sur cette plateforme. Restauration ignorée.");
            return;
        }

        try
        {
            // Vérifier les permissions
            var hasPermissions = await _firewallEngine.CheckPermissionsAsync();
            if (!hasPermissions)
            {
                _logger.LogWarning("Permissions insuffisantes pour gérer le firewall. Exécutez en tant qu'administrateur.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            // Récupérer tous les appareils bloqués en base
            var blockedDevices = await deviceRepo.GetBlockedDevicesAsync();
            var blockedList = blockedDevices.ToList();

            if (blockedList.Count == 0)
            {
                _logger.LogInformation("Aucune règle de blocage à restaurer.");
                return;
            }

            _logger.LogInformation("Restauration de {Count} règles de blocage...", blockedList.Count);

            // Restaurer les règles
            var restoredCount = await _firewallEngine.RestoreRulesFromDatabaseAsync(blockedList);

            _logger.LogInformation(
                "Restauration terminée: {Restored}/{Total} règles appliquées",
                restoredCount,
                blockedList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la restauration des règles de firewall");
        }
    }
}
