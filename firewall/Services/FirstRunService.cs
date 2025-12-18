using NetworkFirewall.Data;

namespace NetworkFirewall.Services;

/// <summary>
/// Service pour détecter et gérer la première exécution de l'application.
/// Déclenche un scan réseau initial si aucun appareil n'est en base.
/// </summary>
public interface IFirstRunService
{
    /// <summary>
    /// Vérifie si c'est la première exécution (aucun appareil découvert)
    /// </summary>
    Task<bool> IsFirstRunAsync();
    
    /// <summary>
    /// Marque la première exécution comme terminée
    /// </summary>
    Task MarkFirstRunCompleteAsync();
}

public class FirstRunService : IFirstRunService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirstRunService> _logger;
    private bool _isFirstRunChecked = false;
    private bool _isFirstRun = false;

    public FirstRunService(
        IServiceScopeFactory scopeFactory,
        ILogger<FirstRunService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> IsFirstRunAsync()
    {
        // Mise en cache pour éviter des requêtes répétées
        if (_isFirstRunChecked)
            return _isFirstRun;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            
            // Première exécution si aucun appareil n'existe en base
            var devices = await deviceRepo.GetAllAsync();
            _isFirstRun = !devices.Any();
            _isFirstRunChecked = true;

            if (_isFirstRun)
            {
                _logger.LogInformation("?? Première exécution détectée - Un scan réseau initial sera effectué");
            }
            else
            {
                _logger.LogInformation("Exécution normale - {Count} appareil(s) déjà en base", devices.Count());
            }

            return _isFirstRun;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification de première exécution");
            return false;
        }
    }

    public Task MarkFirstRunCompleteAsync()
    {
        _isFirstRun = false;
        _isFirstRunChecked = true;
        _logger.LogInformation("? Première exécution terminée");
        return Task.CompletedTask;
    }
}
