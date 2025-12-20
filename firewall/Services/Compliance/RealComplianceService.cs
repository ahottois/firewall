namespace NetworkFirewall.Services.Compliance;

/// <summary>
/// Service de vérification automatique de la conformité ISO
/// </summary>
public interface IRealComplianceService
{
    Task<List<ComplianceCheckResult>> RunAllChecksAsync();
    Task<ComplianceCheckResult> CheckControlAsync(string controlId);
    Task<RealComplianceSummary> GetRealComplianceSummaryAsync();
}

public class RealComplianceService : IRealComplianceService
{
    private readonly ILogger<RealComplianceService> _logger;
    private readonly SystemComplianceChecker _systemChecker;
    private readonly NetworkComplianceChecker _networkChecker;
    private readonly SecurityComplianceChecker _securityChecker;
    private readonly OrganizationalComplianceChecker _orgChecker;
    private readonly Dictionary<string, Func<Task<ComplianceCheckResult>>> _controlChecks;

    public RealComplianceService(
        ILogger<RealComplianceService> logger,
        SystemComplianceChecker systemChecker,
        NetworkComplianceChecker networkChecker,
        SecurityComplianceChecker securityChecker,
        OrganizationalComplianceChecker orgChecker)
    {
        _logger = logger;
        _systemChecker = systemChecker;
        _networkChecker = networkChecker;
        _securityChecker = securityChecker;
        _orgChecker = orgChecker;
        _controlChecks = InitializeControlChecks();
    }

    private Dictionary<string, Func<Task<ComplianceCheckResult>>> InitializeControlChecks()
    {
        return new Dictionary<string, Func<Task<ComplianceCheckResult>>>(StringComparer.OrdinalIgnoreCase)
        {
            // A.8 - Contrôles technologiques (System)
            ["A.8.2"] = () => _systemChecker.CheckPrivilegedAccessRights(),
            ["A.8.5"] = () => _systemChecker.CheckSecureAuthentication(),
            ["A.8.6"] = () => _systemChecker.CheckCapacityManagement(),
            ["A.8.9"] = () => _systemChecker.CheckConfigurationManagement(),
            ["A.8.17"] = () => _systemChecker.CheckClockSynchronization(),
            ["A.8.24"] = () => _systemChecker.CheckCryptography(),
            
            // A.8 - Contrôles technologiques (Security)
            ["A.8.1"] = () => _securityChecker.CheckUserEndpointDevices(),
            ["A.8.7"] = () => _securityChecker.CheckMalwareProtection(),
            ["A.8.8"] = () => _securityChecker.CheckVulnerabilityManagement(),
            ["A.8.13"] = () => _securityChecker.CheckBackupImplementation(),
            ["A.8.15"] = () => _securityChecker.CheckLogging(),
            ["A.8.16"] = () => _securityChecker.CheckMonitoringActivities(),
            
            // A.8 - Contrôles technologiques (Network)
            ["A.8.20"] = () => _networkChecker.CheckNetworkSecurity(),
            ["A.8.21"] = () => _networkChecker.CheckNetworkServicesSecurity(),
            ["A.8.22"] = () => _networkChecker.CheckNetworkSegregation(),
            ["A.8.23"] = () => _networkChecker.CheckWebFiltering(),
            
            // A.5 - Contrôles organisationnels
            ["A.5.7"] = () => _orgChecker.CheckThreatIntelligence(),
            ["A.5.24"] = () => _orgChecker.CheckIncidentManagementPlanning(),
            
            // A.7 - Contrôles physiques
            ["A.7.4"] = () => _orgChecker.CheckPhysicalSecurityMonitoring(),
        };
    }

    public async Task<List<ComplianceCheckResult>> RunAllChecksAsync()
    {
        var results = new List<ComplianceCheckResult>();
        
        foreach (var (controlId, checkFunc) in _controlChecks)
        {
            try
            {
                var result = await checkFunc();
                results.Add(result);
                _logger.LogDebug("Contrôle {ControlId}: {Status} - {Message}", 
                    controlId, result.Status, result.Message);
            }
            catch (Exception ex)
            {
                results.Add(new ComplianceCheckResult
                {
                    ControlId = controlId,
                    Status = ComplianceStatus.Error,
                    Message = $"Erreur lors de la vérification: {ex.Message}",
                    CheckedAt = DateTime.UtcNow
                });
                _logger.LogError(ex, "Erreur vérification contrôle {ControlId}", controlId);
            }
        }

        return results;
    }

    public async Task<ComplianceCheckResult> CheckControlAsync(string controlId)
    {
        if (_controlChecks.TryGetValue(controlId, out var checkFunc))
        {
            return await checkFunc();
        }

        return new ComplianceCheckResult
        {
            ControlId = controlId,
            Status = ComplianceStatus.NotVerifiable,
            Message = "Ce contrôle nécessite une vérification manuelle",
            CheckedAt = DateTime.UtcNow
        };
    }

    public async Task<RealComplianceSummary> GetRealComplianceSummaryAsync()
    {
        var results = await RunAllChecksAsync();
        
        return new RealComplianceSummary
        {
            TotalChecks = results.Count,
            Compliant = results.Count(r => r.Status == ComplianceStatus.Compliant),
            PartiallyCompliant = results.Count(r => r.Status == ComplianceStatus.PartiallyCompliant),
            NonCompliant = results.Count(r => r.Status == ComplianceStatus.NonCompliant),
            NotVerifiable = results.Count(r => r.Status == ComplianceStatus.NotVerifiable),
            Errors = results.Count(r => r.Status == ComplianceStatus.Error),
            CheckResults = results,
            CheckedAt = DateTime.UtcNow
        };
    }
}
