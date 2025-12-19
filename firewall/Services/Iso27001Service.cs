using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IIso27001Service
{
    // Contrôles
    IEnumerable<Iso27001Control> GetAllControls();
    Iso27001Control? GetControl(string controlId);
    Task UpdateControlStatusAsync(string controlId, Iso27001ControlStatus status, string? evidence = null);
    IEnumerable<Iso27001Control> GetControlsByCategory(string category);
    
    // Gestion des risques
    IEnumerable<RiskAssessment> GetAllRisks();
    RiskAssessment? GetRisk(int id);
    Task<RiskAssessment> AddRiskAsync(RiskAssessment risk);
    Task UpdateRiskAsync(RiskAssessment risk);
    Task DeleteRiskAsync(int id);
    IEnumerable<RiskAssessment> GetRisksByLevel(ComplianceRiskLevel level);
    
    // Incidents
    IEnumerable<SecurityIncident> GetAllIncidents();
    SecurityIncident? GetIncident(int id);
    Task<SecurityIncident> AddIncidentAsync(SecurityIncident incident);
    Task UpdateIncidentAsync(SecurityIncident incident);
    IEnumerable<SecurityIncident> GetOpenIncidents();
    
    // Politiques
    IEnumerable<SecurityPolicy> GetAllPolicies();
    SecurityPolicy? GetPolicy(int id);
    Task<SecurityPolicy> AddPolicyAsync(SecurityPolicy policy);
    Task UpdatePolicyAsync(SecurityPolicy policy);
    
    // Résumé
    Iso27001Summary GetSummary();
    Task<double> CalculateComplianceScoreAsync();
}

public class Iso27001Service : IIso27001Service
{
    private readonly ILogger<Iso27001Service> _logger;
    private List<Iso27001Control> _controls;
    private List<RiskAssessment> _risks = new();
    private List<SecurityIncident> _incidents = new();
    private List<SecurityPolicy> _policies = new();
    
    private int _riskIdCounter = 1;
    private int _incidentIdCounter = 1;
    private int _policyIdCounter = 1;
    
    private const string ControlsConfigPath = "iso27001_controls.json";
    private const string RisksConfigPath = "iso27001_risks.json";
    private const string IncidentsConfigPath = "iso27001_incidents.json";
    private const string PoliciesConfigPath = "iso27001_policies.json";

    public Iso27001Service(ILogger<Iso27001Service> logger)
    {
        _logger = logger;
        _controls = InitializeControls();
        LoadData();
    }

    #region Contrôles ISO 27001:2022

    private List<Iso27001Control> InitializeControls()
    {
        // Contrôles ISO 27001:2022 Annexe A
        return new List<Iso27001Control>
        {
            // A.5 - Contrôles organisationnels
            new() { Id = "A.5.1", Category = "A.5", Title = "Politiques de sécurité de l'information", Description = "Des politiques de sécurité de l'information doivent être définies, approuvées par la direction, publiées et communiquées." },
            new() { Id = "A.5.2", Category = "A.5", Title = "Rôles et responsabilités", Description = "Les rôles et responsabilités en matière de sécurité de l'information doivent être définis et attribués." },
            new() { Id = "A.5.3", Category = "A.5", Title = "Séparation des tâches", Description = "Les tâches et domaines de responsabilité contradictoires doivent être séparés." },
            new() { Id = "A.5.4", Category = "A.5", Title = "Responsabilités de la direction", Description = "La direction doit exiger que tous les employés appliquent la sécurité conformément aux politiques." },
            new() { Id = "A.5.5", Category = "A.5", Title = "Contact avec les autorités", Description = "Des contacts appropriés avec les autorités compétentes doivent être maintenus." },
            new() { Id = "A.5.6", Category = "A.5", Title = "Contact avec des groupes spécialisés", Description = "Des contacts avec des groupes ou forums spécialisés en sécurité doivent être maintenus." },
            new() { Id = "A.5.7", Category = "A.5", Title = "Renseignements sur les menaces", Description = "Les informations relatives aux menaces de sécurité doivent être collectées et analysées." },
            new() { Id = "A.5.8", Category = "A.5", Title = "Sécurité dans la gestion de projet", Description = "La sécurité de l'information doit être intégrée dans la gestion de projet." },
            new() { Id = "A.5.9", Category = "A.5", Title = "Inventaire des informations et actifs", Description = "Un inventaire des actifs informationnels doit être développé et maintenu." },
            new() { Id = "A.5.10", Category = "A.5", Title = "Utilisation acceptable des actifs", Description = "Des règles pour l'utilisation acceptable des informations et actifs doivent être identifiées et documentées." },
            new() { Id = "A.5.11", Category = "A.5", Title = "Restitution des actifs", Description = "Les employés et tiers doivent restituer tous les actifs à la fin de leur emploi ou contrat." },
            new() { Id = "A.5.12", Category = "A.5", Title = "Classification des informations", Description = "L'information doit être classifiée selon les exigences légales et la valeur pour l'organisation." },
            new() { Id = "A.5.13", Category = "A.5", Title = "Marquage des informations", Description = "Un ensemble approprié de procédures pour le marquage des informations doit être développé." },
            new() { Id = "A.5.14", Category = "A.5", Title = "Transfert d'informations", Description = "Des règles, procédures ou accords de transfert d'informations doivent être en place." },
            new() { Id = "A.5.15", Category = "A.5", Title = "Contrôle d'accès", Description = "Des règles de contrôle d'accès physique et logique doivent être établies." },
            new() { Id = "A.5.16", Category = "A.5", Title = "Gestion des identités", Description = "Le cycle de vie complet des identités doit être géré." },
            new() { Id = "A.5.17", Category = "A.5", Title = "Informations d'authentification", Description = "L'attribution et la gestion des informations d'authentification doivent être contrôlées." },
            new() { Id = "A.5.18", Category = "A.5", Title = "Droits d'accès", Description = "Les droits d'accès doivent être provisionnés, revus, modifiés et supprimés." },
            new() { Id = "A.5.19", Category = "A.5", Title = "Sécurité dans les relations fournisseurs", Description = "Les exigences de sécurité doivent être établies avec les fournisseurs." },
            new() { Id = "A.5.20", Category = "A.5", Title = "Sécurité dans les accords fournisseurs", Description = "Les exigences de sécurité pertinentes doivent être établies avec chaque fournisseur." },
            new() { Id = "A.5.21", Category = "A.5", Title = "Gestion de la chaîne d'approvisionnement TIC", Description = "Des processus doivent être définis pour gérer les risques de la chaîne d'approvisionnement." },
            new() { Id = "A.5.22", Category = "A.5", Title = "Surveillance et revue des fournisseurs", Description = "L'organisation doit surveiller et revoir les services des fournisseurs." },
            new() { Id = "A.5.23", Category = "A.5", Title = "Sécurité des services cloud", Description = "Les processus d'acquisition et de gestion des services cloud doivent être établis." },
            new() { Id = "A.5.24", Category = "A.5", Title = "Planification de la gestion des incidents", Description = "La gestion des incidents de sécurité doit être planifiée et préparée." },
            new() { Id = "A.5.25", Category = "A.5", Title = "Évaluation des événements de sécurité", Description = "Les événements de sécurité doivent être évalués pour décider s'ils constituent des incidents." },
            new() { Id = "A.5.26", Category = "A.5", Title = "Réponse aux incidents de sécurité", Description = "Les incidents de sécurité doivent être traités selon les procédures documentées." },
            new() { Id = "A.5.27", Category = "A.5", Title = "Apprentissage des incidents", Description = "Les connaissances acquises des incidents doivent être utilisées pour renforcer les contrôles." },
            new() { Id = "A.5.28", Category = "A.5", Title = "Collecte de preuves", Description = "Des procédures pour la collecte, l'acquisition et la préservation des preuves doivent être définies." },
            new() { Id = "A.5.29", Category = "A.5", Title = "Sécurité pendant les perturbations", Description = "Des plans pour maintenir la sécurité pendant les perturbations doivent être établis." },
            new() { Id = "A.5.30", Category = "A.5", Title = "Préparation TIC pour la continuité", Description = "La disponibilité TIC doit être planifiée, mise en œuvre et testée." },
            new() { Id = "A.5.31", Category = "A.5", Title = "Exigences légales et contractuelles", Description = "Les exigences légales, réglementaires et contractuelles doivent être identifiées." },
            new() { Id = "A.5.32", Category = "A.5", Title = "Droits de propriété intellectuelle", Description = "Des procédures appropriées pour les droits de propriété intellectuelle doivent être mises en œuvre." },
            new() { Id = "A.5.33", Category = "A.5", Title = "Protection des enregistrements", Description = "Les enregistrements doivent être protégés contre la perte, destruction et falsification." },
            new() { Id = "A.5.34", Category = "A.5", Title = "Vie privée et protection des DCP", Description = "La vie privée et la protection des données personnelles doivent être assurées." },
            new() { Id = "A.5.35", Category = "A.5", Title = "Revue indépendante de la sécurité", Description = "L'approche de l'organisation pour la sécurité doit être revue indépendamment." },
            new() { Id = "A.5.36", Category = "A.5", Title = "Conformité aux politiques et normes", Description = "La conformité aux politiques et normes de sécurité doit être régulièrement revue." },
            new() { Id = "A.5.37", Category = "A.5", Title = "Procédures d'exploitation documentées", Description = "Les procédures d'exploitation doivent être documentées et mises à disposition." },

            // A.6 - Contrôles du personnel
            new() { Id = "A.6.1", Category = "A.6", Title = "Sélection du personnel", Description = "Des vérifications des antécédents doivent être effectuées pour tous les candidats." },
            new() { Id = "A.6.2", Category = "A.6", Title = "Conditions d'emploi", Description = "Les accords contractuels doivent établir les responsabilités de sécurité." },
            new() { Id = "A.6.3", Category = "A.6", Title = "Sensibilisation et formation", Description = "Le personnel doit recevoir une sensibilisation et formation appropriées." },
            new() { Id = "A.6.4", Category = "A.6", Title = "Processus disciplinaire", Description = "Un processus disciplinaire pour les violations de sécurité doit être mis en place." },
            new() { Id = "A.6.5", Category = "A.6", Title = "Responsabilités après fin de contrat", Description = "Les responsabilités de sécurité restant valides après la fin du contrat doivent être définies." },
            new() { Id = "A.6.6", Category = "A.6", Title = "Accords de confidentialité", Description = "Des accords de confidentialité doivent être identifiés et régulièrement revus." },
            new() { Id = "A.6.7", Category = "A.6", Title = "Travail à distance", Description = "Des mesures de sécurité pour le travail à distance doivent être mises en œuvre." },
            new() { Id = "A.6.8", Category = "A.6", Title = "Signalement des événements de sécurité", Description = "Un mécanisme pour signaler les événements de sécurité doit être fourni." },

            // A.7 - Contrôles physiques
            new() { Id = "A.7.1", Category = "A.7", Title = "Périmètre de sécurité physique", Description = "Des périmètres de sécurité doivent être définis pour protéger les zones sensibles." },
            new() { Id = "A.7.2", Category = "A.7", Title = "Contrôles d'entrée physique", Description = "Les zones sécurisées doivent être protégées par des contrôles d'entrée appropriés." },
            new() { Id = "A.7.3", Category = "A.7", Title = "Sécurisation des bureaux et locaux", Description = "Une sécurité physique pour les bureaux, salles et installations doit être mise en œuvre." },
            new() { Id = "A.7.4", Category = "A.7", Title = "Surveillance physique de sécurité", Description = "Les locaux doivent être surveillés en continu pour détecter les accès non autorisés." },
            new() { Id = "A.7.5", Category = "A.7", Title = "Protection contre les menaces environnementales", Description = "Une protection contre les menaces physiques et environnementales doit être mise en œuvre." },
            new() { Id = "A.7.6", Category = "A.7", Title = "Travail dans les zones sécurisées", Description = "Des mesures de sécurité pour le travail dans les zones sécurisées doivent être conçues." },
            new() { Id = "A.7.7", Category = "A.7", Title = "Bureau propre et écran vide", Description = "Des règles de bureau propre et d'écran vide doivent être définies." },
            new() { Id = "A.7.8", Category = "A.7", Title = "Emplacement et protection des équipements", Description = "Les équipements doivent être situés et protégés contre les risques environnementaux." },
            new() { Id = "A.7.9", Category = "A.7", Title = "Sécurité des actifs hors site", Description = "Des mesures de sécurité pour les actifs hors site doivent être appliquées." },
            new() { Id = "A.7.10", Category = "A.7", Title = "Supports de stockage", Description = "Les supports de stockage doivent être gérés tout au long de leur cycle de vie." },
            new() { Id = "A.7.11", Category = "A.7", Title = "Services généraux de soutien", Description = "Les installations de traitement doivent être protégées contre les pannes de services." },
            new() { Id = "A.7.12", Category = "A.7", Title = "Sécurité du câblage", Description = "Les câbles transportant des données ou de l'énergie doivent être protégés." },
            new() { Id = "A.7.13", Category = "A.7", Title = "Maintenance des équipements", Description = "Les équipements doivent être correctement maintenus pour assurer leur disponibilité." },
            new() { Id = "A.7.14", Category = "A.7", Title = "Mise au rebut sécurisée des équipements", Description = "Les équipements contenant des supports de stockage doivent être vérifiés avant mise au rebut." },

            // A.8 - Contrôles technologiques
            new() { Id = "A.8.1", Category = "A.8", Title = "Terminaux utilisateur", Description = "L'information stockée, traitée ou accessible via les terminaux doit être protégée." },
            new() { Id = "A.8.2", Category = "A.8", Title = "Droits d'accès privilégiés", Description = "L'attribution et l'utilisation des droits d'accès privilégiés doivent être restreintes." },
            new() { Id = "A.8.3", Category = "A.8", Title = "Restriction d'accès à l'information", Description = "L'accès à l'information et aux fonctions des systèmes doit être restreint." },
            new() { Id = "A.8.4", Category = "A.8", Title = "Accès au code source", Description = "L'accès en lecture et en écriture au code source doit être géré de manière appropriée." },
            new() { Id = "A.8.5", Category = "A.8", Title = "Authentification sécurisée", Description = "Des techniques d'authentification sécurisée doivent être mises en œuvre." },
            new() { Id = "A.8.6", Category = "A.8", Title = "Gestion des capacités", Description = "L'utilisation des ressources doit être surveillée et ajustée." },
            new() { Id = "A.8.7", Category = "A.8", Title = "Protection contre les logiciels malveillants", Description = "Une protection contre les logiciels malveillants doit être mise en œuvre." },
            new() { Id = "A.8.8", Category = "A.8", Title = "Gestion des vulnérabilités techniques", Description = "Les informations sur les vulnérabilités techniques doivent être obtenues et des mesures prises." },
            new() { Id = "A.8.9", Category = "A.8", Title = "Gestion de la configuration", Description = "Les configurations matérielles, logicielles et réseau doivent être établies et documentées." },
            new() { Id = "A.8.10", Category = "A.8", Title = "Suppression des informations", Description = "L'information stockée dans les systèmes et supports doit être supprimée quand plus nécessaire." },
            new() { Id = "A.8.11", Category = "A.8", Title = "Masquage des données", Description = "Le masquage des données doit être utilisé conformément aux politiques." },
            new() { Id = "A.8.12", Category = "A.8", Title = "Prévention des fuites de données", Description = "Des mesures de prévention des fuites de données doivent être appliquées." },
            new() { Id = "A.8.13", Category = "A.8", Title = "Sauvegarde des informations", Description = "Des copies de sauvegarde doivent être maintenues et testées régulièrement." },
            new() { Id = "A.8.14", Category = "A.8", Title = "Redondance des installations", Description = "Les installations de traitement doivent être mises en œuvre avec une redondance suffisante." },
            new() { Id = "A.8.15", Category = "A.8", Title = "Journalisation", Description = "Les journaux d'événements doivent être produits, stockés, protégés et analysés." },
            new() { Id = "A.8.16", Category = "A.8", Title = "Activités de surveillance", Description = "Les réseaux, systèmes et applications doivent être surveillés pour détecter les comportements anormaux." },
            new() { Id = "A.8.17", Category = "A.8", Title = "Synchronisation des horloges", Description = "Les horloges des systèmes doivent être synchronisées avec une source de temps approuvée." },
            new() { Id = "A.8.18", Category = "A.8", Title = "Utilisation des utilitaires privilégiés", Description = "L'utilisation des utilitaires pouvant contourner les contrôles doit être restreinte." },
            new() { Id = "A.8.19", Category = "A.8", Title = "Installation de logiciels sur les systèmes opérationnels", Description = "Des procédures pour l'installation de logiciels sur les systèmes opérationnels doivent être mises en œuvre." },
            new() { Id = "A.8.20", Category = "A.8", Title = "Sécurité des réseaux", Description = "Les réseaux et dispositifs réseau doivent être sécurisés, gérés et contrôlés." },
            new() { Id = "A.8.21", Category = "A.8", Title = "Sécurité des services réseau", Description = "Les mécanismes de sécurité et les niveaux de service des services réseau doivent être identifiés." },
            new() { Id = "A.8.22", Category = "A.8", Title = "Ségrégation des réseaux", Description = "Les groupes de services, utilisateurs et systèmes doivent être séparés sur les réseaux." },
            new() { Id = "A.8.23", Category = "A.8", Title = "Filtrage web", Description = "L'accès aux sites web externes doit être géré pour réduire l'exposition aux contenus malveillants." },
            new() { Id = "A.8.24", Category = "A.8", Title = "Utilisation de la cryptographie", Description = "Des règles pour l'utilisation effective de la cryptographie doivent être définies." },
            new() { Id = "A.8.25", Category = "A.8", Title = "Cycle de vie du développement sécurisé", Description = "Des règles pour le développement sécurisé doivent être établies." },
            new() { Id = "A.8.26", Category = "A.8", Title = "Exigences de sécurité des applications", Description = "Les exigences de sécurité doivent être identifiées lors du développement ou de l'acquisition." },
            new() { Id = "A.8.27", Category = "A.8", Title = "Architecture système sécurisée et principes d'ingénierie", Description = "Des principes d'ingénierie de systèmes sécurisés doivent être établis." },
            new() { Id = "A.8.28", Category = "A.8", Title = "Codage sécurisé", Description = "Des principes de codage sécurisé doivent être appliqués au développement logiciel." },
            new() { Id = "A.8.29", Category = "A.8", Title = "Tests de sécurité en développement et acceptance", Description = "Des processus de test de sécurité doivent être définis et mis en œuvre." },
            new() { Id = "A.8.30", Category = "A.8", Title = "Développement externalisé", Description = "L'organisation doit diriger, surveiller et revoir les activités de développement externalisé." },
            new() { Id = "A.8.31", Category = "A.8", Title = "Séparation des environnements", Description = "Les environnements de développement, test et production doivent être séparés." },
            new() { Id = "A.8.32", Category = "A.8", Title = "Gestion des changements", Description = "Les changements aux installations de traitement et aux systèmes doivent être soumis à la gestion des changements." },
            new() { Id = "A.8.33", Category = "A.8", Title = "Informations de test", Description = "Les informations de test doivent être sélectionnées, protégées et gérées de manière appropriée." },
            new() { Id = "A.8.34", Category = "A.8", Title = "Protection des systèmes d'audit", Description = "Les tests d'audit et autres activités d'assurance impliquant les systèmes opérationnels doivent être planifiés." }
        };
    }

    public IEnumerable<Iso27001Control> GetAllControls() => _controls;

    public Iso27001Control? GetControl(string controlId)
    {
        return _controls.FirstOrDefault(c => c.Id.Equals(controlId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateControlStatusAsync(string controlId, Iso27001ControlStatus status, string? evidence = null)
    {
        var control = GetControl(controlId);
        if (control != null)
        {
            control.Status = status;
            control.Evidence = evidence;
            control.LastReviewDate = DateTime.UtcNow;
            await SaveControlsAsync();
            _logger.LogInformation("ISO 27001: Contrôle {Id} mis à jour - Statut: {Status}", controlId, status);
        }
    }

    public IEnumerable<Iso27001Control> GetControlsByCategory(string category)
    {
        return _controls.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Gestion des Risques

    public IEnumerable<RiskAssessment> GetAllRisks() => _risks;

    public RiskAssessment? GetRisk(int id) => _risks.FirstOrDefault(r => r.Id == id);

    public async Task<RiskAssessment> AddRiskAsync(RiskAssessment risk)
    {
        risk.Id = _riskIdCounter++;
        risk.AssessmentDate = DateTime.UtcNow;
        risk.Status = RiskStatus.Identified;
        _risks.Add(risk);
        await SaveRisksAsync();
        _logger.LogInformation("ISO 27001: Risque {Id} ajouté - Asset: {Asset}", risk.Id, risk.AssetName);
        return risk;
    }

    public async Task UpdateRiskAsync(RiskAssessment risk)
    {
        var existing = GetRisk(risk.Id);
        if (existing != null)
        {
            var index = _risks.IndexOf(existing);
            _risks[index] = risk;
            await SaveRisksAsync();
            _logger.LogInformation("ISO 27001: Risque {Id} mis à jour", risk.Id);
        }
    }

    public async Task DeleteRiskAsync(int id)
    {
        var risk = GetRisk(id);
        if (risk != null)
        {
            _risks.Remove(risk);
            await SaveRisksAsync();
            _logger.LogInformation("ISO 27001: Risque {Id} supprimé", id);
        }
    }

    public IEnumerable<RiskAssessment> GetRisksByLevel(ComplianceRiskLevel level)
    {
        return _risks.Where(r => r.RiskLevel == level);
    }

    #endregion

    #region Gestion des Incidents

    public IEnumerable<SecurityIncident> GetAllIncidents() => _incidents;

    public SecurityIncident? GetIncident(int id) => _incidents.FirstOrDefault(i => i.Id == id);

    public async Task<SecurityIncident> AddIncidentAsync(SecurityIncident incident)
    {
        incident.Id = _incidentIdCounter++;
        incident.DetectedAt = DateTime.UtcNow;
        incident.Status = IncidentStatus.New;
        _incidents.Add(incident);
        await SaveIncidentsAsync();
        _logger.LogInformation("ISO 27001: Incident {Id} créé - {Title}", incident.Id, incident.Title);
        return incident;
    }

    public async Task UpdateIncidentAsync(SecurityIncident incident)
    {
        var existing = GetIncident(incident.Id);
        if (existing != null)
        {
            var index = _incidents.IndexOf(existing);
            _incidents[index] = incident;
            await SaveIncidentsAsync();
            _logger.LogInformation("ISO 27001: Incident {Id} mis à jour - Statut: {Status}", incident.Id, incident.Status);
        }
    }

    public IEnumerable<SecurityIncident> GetOpenIncidents()
    {
        return _incidents.Where(i => i.Status != IncidentStatus.Closed);
    }

    #endregion

    #region Politiques de Sécurité

    public IEnumerable<SecurityPolicy> GetAllPolicies() => _policies;

    public SecurityPolicy? GetPolicy(int id) => _policies.FirstOrDefault(p => p.Id == id);

    public async Task<SecurityPolicy> AddPolicyAsync(SecurityPolicy policy)
    {
        policy.Id = _policyIdCounter++;
        policy.CreatedDate = DateTime.UtcNow;
        policy.Status = PolicyStatus.Draft;
        _policies.Add(policy);
        await SavePoliciesAsync();
        _logger.LogInformation("ISO 27001: Politique {Code} créée - {Title}", policy.Code, policy.Title);
        return policy;
    }

    public async Task UpdatePolicyAsync(SecurityPolicy policy)
    {
        var existing = GetPolicy(policy.Id);
        if (existing != null)
        {
            var index = _policies.IndexOf(existing);
            _policies[index] = policy;
            await SavePoliciesAsync();
            _logger.LogInformation("ISO 27001: Politique {Code} mise à jour", policy.Code);
        }
    }

    #endregion

    #region Résumé et Score

    public Iso27001Summary GetSummary()
    {
        var summary = new Iso27001Summary
        {
            TotalControls = _controls.Count,
            ImplementedControls = _controls.Count(c => c.Status == Iso27001ControlStatus.Implemented || c.Status == Iso27001ControlStatus.Effective),
            PartiallyImplementedControls = _controls.Count(c => c.Status == Iso27001ControlStatus.PartiallyImplemented),
            NotImplementedControls = _controls.Count(c => c.Status == Iso27001ControlStatus.NotImplemented),
            NotApplicableControls = _controls.Count(c => c.Status == Iso27001ControlStatus.NotApplicable)
        };

        var applicable = summary.TotalControls - summary.NotApplicableControls;
        if (applicable > 0)
        {
            summary.CompliancePercentage = Math.Round(
                (double)(summary.ImplementedControls + summary.PartiallyImplementedControls * 0.5) / applicable * 100, 2);
        }

        // Résumé par catégorie
        var categories = _controls.GroupBy(c => c.Category);
        foreach (var category in categories)
        {
            var catSummary = new CategorySummary
            {
                CategoryName = GetCategoryName(category.Key),
                TotalControls = category.Count(),
                ImplementedControls = category.Count(c => c.Status == Iso27001ControlStatus.Implemented || c.Status == Iso27001ControlStatus.Effective)
            };
            
            var catApplicable = catSummary.TotalControls - category.Count(c => c.Status == Iso27001ControlStatus.NotApplicable);
            if (catApplicable > 0)
            {
                catSummary.CompliancePercentage = Math.Round((double)catSummary.ImplementedControls / catApplicable * 100, 2);
            }
            
            summary.ByCategory[category.Key] = catSummary;
        }

        return summary;
    }

    private string GetCategoryName(string category)
    {
        return category switch
        {
            "A.5" => "Contrôles organisationnels",
            "A.6" => "Contrôles du personnel",
            "A.7" => "Contrôles physiques",
            "A.8" => "Contrôles technologiques",
            _ => category
        };
    }

    public Task<double> CalculateComplianceScoreAsync()
    {
        var summary = GetSummary();
        return Task.FromResult(summary.CompliancePercentage);
    }

    #endregion

    #region Persistance

    private void LoadData()
    {
        try
        {
            if (File.Exists(ControlsConfigPath))
            {
                var json = File.ReadAllText(ControlsConfigPath);
                var savedControls = JsonSerializer.Deserialize<List<Iso27001Control>>(json);
                if (savedControls != null)
                {
                    // Fusionner avec les contrôles initialisés
                    foreach (var saved in savedControls)
                    {
                        var control = _controls.FirstOrDefault(c => c.Id == saved.Id);
                        if (control != null)
                        {
                            control.Status = saved.Status;
                            control.Evidence = saved.Evidence;
                            control.LastReviewDate = saved.LastReviewDate;
                            control.ResponsiblePerson = saved.ResponsiblePerson;
                            control.ImplementationLevel = saved.ImplementationLevel;
                        }
                    }
                }
            }

            if (File.Exists(RisksConfigPath))
            {
                var json = File.ReadAllText(RisksConfigPath);
                _risks = JsonSerializer.Deserialize<List<RiskAssessment>>(json) ?? new();
                _riskIdCounter = _risks.Any() ? _risks.Max(r => r.Id) + 1 : 1;
            }

            if (File.Exists(IncidentsConfigPath))
            {
                var json = File.ReadAllText(IncidentsConfigPath);
                _incidents = JsonSerializer.Deserialize<List<SecurityIncident>>(json) ?? new();
                _incidentIdCounter = _incidents.Any() ? _incidents.Max(i => i.Id) + 1 : 1;
            }

            if (File.Exists(PoliciesConfigPath))
            {
                var json = File.ReadAllText(PoliciesConfigPath);
                _policies = JsonSerializer.Deserialize<List<SecurityPolicy>>(json) ?? new();
                _policyIdCounter = _policies.Any() ? _policies.Max(p => p.Id) + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement données ISO 27001");
        }
    }

    private async Task SaveControlsAsync()
    {
        var json = JsonSerializer.Serialize(_controls, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ControlsConfigPath, json);
    }

    private async Task SaveRisksAsync()
    {
        var json = JsonSerializer.Serialize(_risks, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(RisksConfigPath, json);
    }

    private async Task SaveIncidentsAsync()
    {
        var json = JsonSerializer.Serialize(_incidents, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(IncidentsConfigPath, json);
    }

    private async Task SavePoliciesAsync()
    {
        var json = JsonSerializer.Serialize(_policies, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PoliciesConfigPath, json);
    }

    #endregion
}
