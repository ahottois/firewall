using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IIso27001Service
{
    // Controles
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
    
    // Resume
    Iso27001Summary GetSummary();
    Task<double> CalculateComplianceScoreAsync();
}

public class Iso27001Service : IIso27001Service
{
    private readonly ILogger<Iso27001Service> _logger;
    private readonly List<Iso27001Control> _controls;
    private readonly Dictionary<string, List<Iso27001Control>> _controlsByCategory;
    private readonly Dictionary<string, Iso27001Control> _controlsById;
    private List<RiskAssessment> _risks = new();
    private List<SecurityIncident> _incidents = new();
    private List<SecurityPolicy> _policies = new();
    
    // Cache du résumé
    private Iso27001Summary? _cachedSummary;
    private bool _summaryInvalid = true;
    
    private int _riskIdCounter = 1;
    private int _incidentIdCounter = 1;
    private int _policyIdCounter = 1;
    
    private const string ControlsConfigPath = "iso27001_controls.json";
    private const string RisksConfigPath = "iso27001_risks.json";
    private const string IncidentsConfigPath = "iso27001_incidents.json";
    private const string PoliciesConfigPath = "iso27001_policies.json";
    
    private static readonly Dictionary<string, string> CategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A.5"] = "Controles organisationnels",
        ["A.6"] = "Controles du personnel",
        ["A.7"] = "Controles physiques",
        ["A.8"] = "Controles technologiques"
    };

    public Iso27001Service(ILogger<Iso27001Service> logger)
    {
        _logger = logger;
        _controls = InitializeControls();
        
        // Créer les index pour accès rapide
        _controlsById = _controls.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _controlsByCategory = _controls
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        
        LoadData();
    }

    #region Controles ISO 27001:2022

    private static List<Iso27001Control> InitializeControls()
    {
        // Controles ISO 27001:2022 Annexe A - Liste statique
        return new List<Iso27001Control>
        {
            // A.5 - Controles organisationnels
            new() { Id = "A.5.1", Category = "A.5", Title = "Politiques de securite de l'information", Description = "Des politiques de securite de l'information doivent etre definies, approuvees par la direction, publiees et communiquees." },
            new() { Id = "A.5.2", Category = "A.5", Title = "Roles et responsabilites", Description = "Les roles et responsabilites en matiere de securite de l'information doivent etre definis et attribues." },
            new() { Id = "A.5.3", Category = "A.5", Title = "Separation des taches", Description = "Les taches et domaines de responsabilite contradictoires doivent etre separes." },
            new() { Id = "A.5.4", Category = "A.5", Title = "Responsabilites de la direction", Description = "La direction doit exiger que tous les employes appliquent la securite conformement aux politiques." },
            new() { Id = "A.5.5", Category = "A.5", Title = "Contact avec les autorites", Description = "Des contacts appropries avec les autorites competentes doivent etre maintenus." },
            new() { Id = "A.5.6", Category = "A.5", Title = "Contact avec des groupes specialises", Description = "Des contacts avec des groupes ou forums specialises en securite doivent etre maintenus." },
            new() { Id = "A.5.7", Category = "A.5", Title = "Renseignements sur les menaces", Description = "Les informations relatives aux menaces de securite doivent etre collectees et analysees." },
            new() { Id = "A.5.8", Category = "A.5", Title = "Securite dans la gestion de projet", Description = "La securite de l'information doit etre integree dans la gestion de projet." },
            new() { Id = "A.5.9", Category = "A.5", Title = "Inventaire des informations et actifs", Description = "Un inventaire des actifs informationnels doit etre developpe et maintenu." },
            new() { Id = "A.5.10", Category = "A.5", Title = "Utilisation acceptable des assets", Description = "Des regles pour l'utilisation acceptable des informations et actifs doivent etre identifiees et documentees." },
            new() { Id = "A.5.11", Category = "A.5", Title = "Restitution des actifs", Description = "Les employes et tiers doivent restituer tous les actifs a la fin de leur emploi ou contrat." },
            new() { Id = "A.5.12", Category = "A.5", Title = "Classification des informations", Description = "L'information doit etre classifiee selon les exigences legales et la valeur pour l'organisation." },
            new() { Id = "A.5.13", Category = "A.5", Title = "Marquage des informations", Description = "Un ensemble approprie de procedures pour le marquage des informations doit etre developpe." },
            new() { Id = "A.5.14", Category = "A.5", Title = "Transfert d'informations", Description = "Des regles, procedures ou accords de transfert d'informations doivent etre en place." },
            new() { Id = "A.5.15", Category = "A.5", Title = "Controle d'acces", Description = "Des regles de controle d'acces physique et logique doivent etre etablies." },
            new() { Id = "A.5.16", Category = "A.5", Title = "Gestion des identites", Description = "Le cycle de vie complet des identites doit etre gere." },
            new() { Id = "A.5.17", Category = "A.5", Title = "Informations d'authentification", Description = "L'attribution et la gestion des informations d'authentification doivent etre controlees." },
            new() { Id = "A.5.18", Category = "A.5", Title = "Droits d'acces", Description = "Les droits d'acces doivent etre provisionnes, revus, modifies et supprimes." },
            new() { Id = "A.5.19", Category = "A.5", Title = "Securite dans les relations fournisseurs", Description = "Les exigences de securite doivent etre etablies avec les fournisseurs." },
            new() { Id = "A.5.20", Category = "A.5", Title = "Securite dans les accords fournisseurs", Description = "Les exigences de securite pertinentes doivent etre etablies avec chaque fournisseur." },
            new() { Id = "A.5.21", Category = "A.5", Title = "Gestion de la chaine d'approvisionnement TIC", Description = "Des processus doivent etre definis pour gerer les risques de la chaine d'approvisionnement." },
            new() { Id = "A.5.22", Category = "A.5", Title = "Surveillance et revue des fournisseurs", Description = "L'organisation doit surveiller et revoir les services des fournisseurs." },
            new() { Id = "A.5.23", Category = "A.5", Title = "Securite des services cloud", Description = "Les processus d'acquisition et de gestion des services cloud doivent etre etablis." },
            new() { Id = "A.5.24", Category = "A.5", Title = "Planification de la gestion des incidents", Description = "La gestion des incidents de securite doit etre planifiee et preparee." },
            new() { Id = "A.5.25", Category = "A.5", Title = "Evaluation des evenements de securite", Description = "Les evenements de securite doivent etre evalues pour decider s'ils constituent des incidents." },
            new() { Id = "A.5.26", Category = "A.5", Title = "Reponse aux incidents de securite", Description = "Les incidents de securite doivent etre traites selon les procedures documentees." },
            new() { Id = "A.5.27", Category = "A.5", Title = "Apprentissage des incidents", Description = "Les connaissances acquises des incidents doivent etre utilisees pour renforcer les controles." },
            new() { Id = "A.5.28", Category = "A.5", Title = "Collecte de preuves", Description = "Des procedures pour la collecte, l'acquisition et la preservation des preuves doivent etre definies." },
            new() { Id = "A.5.29", Category = "A.5", Title = "Securite pendant les perturbations", Description = "Des plans pour maintenir la securite pendant les perturbations doivent etre etablis." },
            new() { Id = "A.5.30", Category = "A.5", Title = "Preparation TIC pour la continuite", Description = "La disponibilite TIC doit etre planifiee, mise en oeuvre et testee." },
            new() { Id = "A.5.31", Category = "A.5", Title = "Exigences legales et contractuelles", Description = "Les exigences legales, reglementaires et contractuelles doivent etre identifiees." },
            new() { Id = "A.5.32", Category = "A.5", Title = "Droits de propriete intellectuelle", Description = "Des procedures appropriees pour les droits de propriete intellectuelle doivent etre mises en oeuvre." },
            new() { Id = "A.5.33", Category = "A.5", Title = "Protection des enregistrements", Description = "Les enregistrements doivent etre proteges contre la perte, destruction et falsification." },
            new() { Id = "A.5.34", Category = "A.5", Title = "Vie privee et protection des DCP", Description = "La vie privee et la protection des donnees personnelles doivent etre assurees." },
            new() { Id = "A.5.35", Category = "A.5", Title = "Revue independante de la securite", Description = "L'approche de l'organisation pour la securite doit etre revue independamment." },
            new() { Id = "A.5.36", Category = "A.5", Title = "Conformite aux politiques et normes", Description = "La conformite aux politiques et normes de securite doit etre regulierement revue." },
            new() { Id = "A.5.37", Category = "A.5", Title = "Procedures d'exploitation documentees", Description = "Les procedures d'exploitation doivent etre documentees et mises a disposition." },

            // A.6 - Controles du personnel
            new() { Id = "A.6.1", Category = "A.6", Title = "Selection du personnel", Description = "Des verifications des antecedents doivent etre effectuees pour tous les candidats." },
            new() { Id = "A.6.2", Category = "A.6", Title = "Conditions d'emploi", Description = "Les accords contractuels doivent etablir les responsabilites de securite." },
            new() { Id = "A.6.3", Category = "A.6", Title = "Sensibilisation et formation", Description = "Le personnel doit recevoir une sensibilisation et formation appropriees." },
            new() { Id = "A.6.4", Category = "A.6", Title = "Processus disciplinaire", Description = "Un processus disciplinaire pour les violations de securite doit etre mis en place." },
            new() { Id = "A.6.5", Category = "A.6", Title = "Responsabilites apres fin de contrat", Description = "Les responsabilites de securite restant valides apres la fin du contrat doivent etre definies." },
            new() { Id = "A.6.6", Category = "A.6", Title = "Accords de confidentialite", Description = "Des accords de confidentialite doivent etre identifies et regulierement revus." },
            new() { Id = "A.6.7", Category = "A.6", Title = "Travail a distance", Description = "Des mesures de securite pour le travail a distance doivent etre mises en oeuvre." },
            new() { Id = "A.6.8", Category = "A.6", Title = "Signalement des evenements de securite", Description = "Un mecanisme pour signaler les evenements de securite doit etre fourni." },

            // A.7 - Controles physiques
            new() { Id = "A.7.1", Category = "A.7", Title = "Perimetre de securite physique", Description = "Des perimetres de securite doivent etre definis pour proteger les zones sensibles." },
            new() { Id = "A.7.2", Category = "A.7", Title = "Controles d'entree physique", Description = "Les zones securisees doivent etre protegees par des controles d'entree appropries." },
            new() { Id = "A.7.3", Category = "A.7", Title = "Securisation des bureaux et locaux", Description = "Une securite physique pour les bureaux, salles et installations doit etre mise en oeuvre." },
            new() { Id = "A.7.4", Category = "A.7", Title = "Surveillance physique de securite", Description = "Les locaux doivent etre surveilles en continu pour detecter les acces non autorises." },
            new() { Id = "A.7.5", Category = "A.7", Title = "Protection contre les menaces environnementales", Description = "Une protection contre les menaces physiques et environnementales doit etre mise en oeuvre." },
            new() { Id = "A.7.6", Category = "A.7", Title = "Travail dans les zones securisees", Description = "Des mesures de securite pour le travail dans les zones securisees doivent etre concues." },
            new() { Id = "A.7.7", Category = "A.7", Title = "Bureau propre et ecran vide", Description = "Des regles de bureau propre et d'ecran vide doivent etre definies." },
            new() { Id = "A.7.8", Category = "A.7", Title = "Emplacement et protection des equipements", Description = "Les equipements doivent etre situes et proteges contre les risques environnementaux." },
            new() { Id = "A.7.9", Category = "A.7", Title = "Securite des assets hors site", Description = "Des mesures de securite pour les actifs hors site doivent etre appliquees." },
            new() { Id = "A.7.10", Category = "A.7", Title = "Supports de stockage", Description = "Les supports de stockage doivent etre geres tout au long de leur cycle de vie." },
            new() { Id = "A.7.11", Category = "A.7", Title = "Services generaux de soutien", Description = "Les installations de traitement doivent etre protegees contre les pannes de services." },
            new() { Id = "A.7.12", Category = "A.7", Title = "Securite du cablage", Description = "Les cables transportant des donnees ou de l'energie doivent etre proteges." },
            new() { Id = "A.7.13", Category = "A.7", Title = "Maintenance des equipements", Description = "Les equipements doivent etre correctement maintenus pour assurer leur disponibilite." },
            new() { Id = "A.7.14", Category = "A.7", Title = "Mise au rebut securisee des equipements", Description = "Les equipements contenant des supports de stockage doivent etre verifies avant mise au rebut." },

            // A.8 - Controles technologiques
            new() { Id = "A.8.1", Category = "A.8", Title = "Terminaux utilisateur", Description = "L'information stockee, traitee ou accessible via les terminaux doit etre protegee." },
            new() { Id = "A.8.2", Category = "A.8", Title = "Droits d'acces privilegies", Description = "L'attribution et l'utilisation des droits d'acces privilegies doivent etre restreintes." },
            new() { Id = "A.8.3", Category = "A.8", Title = "Restriction d'acces a l'information", Description = "L'acces a l'information et aux fonctions des systemes doit etre restreint." },
            new() { Id = "A.8.4", Category = "A.8", Title = "Acces au code source", Description = "L'acces en lecture et en ecriture au code source doit etre gere de maniere appropriee." },
            new() { Id = "A.8.5", Category = "A.8", Title = "Authentification securisee", Description = "Des techniques d'authentification securisee doivent etre mises en oeuvre." },
            new() { Id = "A.8.6", Category = "A.8", Title = "Gestion des capacites", Description = "L'utilisation des ressources doit etre surveillee et ajustee." },
            new() { Id = "A.8.7", Category = "A.8", Title = "Protection contre les logiciels malveillants", Description = "Une protection contre les logiciels malveillants doit etre mise en oeuvre." },
            new() { Id = "A.8.8", Category = "A.8", Title = "Gestion des vulnerabilites techniques", Description = "Les informations sur les vulnerabilites techniques doivent etre obtenues et des mesures prises." },
            new() { Id = "A.8.9", Category = "A.8", Title = "Gestion de la configuration", Description = "Les configurations materielles, logicielles et reseau doivent etre etablies et documentees." },
            new() { Id = "A.8.10", Category = "A.8", Title = "Suppression des informations", Description = "L'information stockee dans les systemes et supports doit etre supprimee quand plus necessaire." },
            new() { Id = "A.8.11", Category = "A.8", Title = "Masquage des donnees", Description = "Le masquage des donnees doit etre utilise conformement aux politiques." },
            new() { Id = "A.8.12", Category = "A.8", Title = "Prevention des fuites de donnees", Description = "Des mesures de prevention des fuites de donnees doivent etre appliquees." },
            new() { Id = "A.8.13", Category = "A.8", Title = "Sauvegarde des informations", Description = "Des copies de sauvegarde doivent etre maintenues et testees regulierement." },
            new() { Id = "A.8.14", Category = "A.8", Title = "Redondance des installations", Description = "Les installations de traitement doivent etre mises en oeuvre avec une redondance suffisante." },
            new() { Id = "A.8.15", Category = "A.8", Title = "Journalisation", Description = "Les journaux d'evenements doivent etre produits, stockes, proteges et analyses." },
            new() { Id = "A.8.16", Category = "A.8", Title = "Activites de surveillance", Description = "Les reseaux, systemes et applications doivent etre surveilles pour detecter les comportements anormaux." },
            new() { Id = "A.8.17", Category = "A.8", Title = "Synchronisation des horloges", Description = "Les horloges des systemes doivent etre synchronisees avec une source de temps approuvee." },
            new() { Id = "A.8.18", Category = "A.8", Title = "Utilisation des utilitaires privilegies", Description = "L'utilisation des utilitaires pouvant contourner les controles doit etre restreinte." },
            new() { Id = "A.8.19", Category = "A.8", Title = "Installation de logiciels sur les systemes operationnels", Description = "Des procedures pour l'installation de logiciels sur les systemes operationnels doivent etre mises en oeuvre." },
            new() { Id = "A.8.20", Category = "A.8", Title = "Securite des reseaux", Description = "Les reseaux et dispositifs reseau doivent etre securises, geres et controles." },
            new() { Id = "A.8.21", Category = "A.8", Title = "Securite des services reseau", Description = "Les mecanismes de securite et les niveaux de service des services reseau doivent etre identifies." },
            new() { Id = "A.8.22", Category = "A.8", Title = "Segregation des reseaux", Description = "Les groupes de services, utilisateurs et systemes doivent etre separes sur les reseaux." },
            new() { Id = "A.8.23", Category = "A.8", Title = "Filtrage web", Description = "L'acces aux sites web externes doit etre gere pour reduire l'exposition aux contenus malveillants." },
            new() { Id = "A.8.24", Category = "A.8", Title = "Utilisation de la cryptographie", Description = "Des regles pour l'utilisation effective de la cryptographie doivent etre definies." },
            new() { Id = "A.8.25", Category = "A.8", Title = "Cycle de vie du developpement securise", Description = "Des regles pour le developpement securise doivent etre etablies." },
            new() { Id = "A.8.26", Category = "A.8", Title = "Exigences de securite des applications", Description = "Les exigences de securite doivent etre identifiees lors du developpement ou de l'acquisition." },
            new() { Id = "A.8.27", Category = "A.8", Title = "Architecture systeme securisee et principes d'ingenierie", Description = "Des principes d'ingenierie de systemes securises doivent etre etablis." },
            new() { Id = "A.8.28", Category = "A.8", Title = "Codage securise", Description = "Des principes de codage securise doivent etre appliques au developpement logiciel." },
            new() { Id = "A.8.29", Category = "A.8", Title = "Tests de securite en developpement et acceptance", Description = "Des processus de test de securite doivent etre definis et mis en oeuvre." },
            new() { Id = "A.8.30", Category = "A.8", Title = "Developpement externalise", Description = "L'organisation doit diriger, surveiller et revoir les activites de developpement externalise." },
            new() { Id = "A.8.31", Category = "A.8", Title = "Separation des environnements", Description = "Les environnements de developpement, test et production doivent etre separes." },
            new() { Id = "A.8.32", Category = "A.8", Title = "Gestion des changements", Description = "Les changements aux installations de traitement et aux systemes doivent etre soumis a la gestion des changements." },
            new() { Id = "A.8.33", Category = "A.8", Title = "Informations de test", Description = "Les informations de test doivent etre selectionnees, protegees et gerees de maniere appropriee." },
            new() { Id = "A.8.34", Category = "A.8", Title = "Protection des systemes d'audit", Description = "Les tests d'audit et autres activites d'assurance impliquant les systemes operationnels doivent etre planifies." }
        };
    }

    public IEnumerable<Iso27001Control> GetAllControls() => _controls;

    public Iso27001Control? GetControl(string controlId)
    {
        return _controlsById.TryGetValue(controlId, out var control) ? control : null;
    }

    public async Task UpdateControlStatusAsync(string controlId, Iso27001ControlStatus status, string? evidence = null)
    {
        var control = GetControl(controlId);
        if (control != null)
        {
            control.Status = status;
            control.Evidence = evidence;
            control.LastReviewDate = DateTime.UtcNow;
            InvalidateSummaryCache();
            await SaveControlsAsync();
            _logger.LogInformation("ISO 27001: Controle {Id} mis a jour - Statut: {Status}", controlId, status);
        }
    }

    public IEnumerable<Iso27001Control> GetControlsByCategory(string category)
    {
        return _controlsByCategory.TryGetValue(category, out var controls) ? controls : Enumerable.Empty<Iso27001Control>();
    }
    
    private void InvalidateSummaryCache() => _summaryInvalid = true;

    #endregion

    #region Gestion des Risques

    public IEnumerable<RiskAssessment> GetAllRisks() => _risks;

    public RiskAssessment? GetRisk(int id) => _risks.Find(r => r.Id == id);

    public async Task<RiskAssessment> AddRiskAsync(RiskAssessment risk)
    {
        risk.Id = _riskIdCounter++;
        risk.AssessmentDate = DateTime.UtcNow;
        risk.Status = RiskStatus.Identified;
        _risks.Add(risk);
        await SaveRisksAsync();
        _logger.LogInformation("ISO 27001: Risque {Id} ajoute - Asset: {Asset}", risk.Id, risk.AssetName);
        return risk;
    }

    public async Task UpdateRiskAsync(RiskAssessment risk)
    {
        var index = _risks.FindIndex(r => r.Id == risk.Id);
        if (index >= 0)
        {
            _risks[index] = risk;
            await SaveRisksAsync();
            _logger.LogInformation("ISO 27001: Risque {Id} mis a jour", risk.Id);
        }
    }

    public async Task DeleteRiskAsync(int id)
    {
        var removed = _risks.RemoveAll(r => r.Id == id);
        if (removed > 0)
        {
            await SaveRisksAsync();
            _logger.LogInformation("ISO 27001: Risque {Id} supprime", id);
        }
    }

    public IEnumerable<RiskAssessment> GetRisksByLevel(ComplianceRiskLevel level)
    {
        return _risks.Where(r => r.RiskLevel == level);
    }

    #endregion

    #region Gestion des Incidents

    public IEnumerable<SecurityIncident> GetAllIncidents() => _incidents;

    public SecurityIncident? GetIncident(int id) => _incidents.Find(i => i.Id == id);

    public async Task<SecurityIncident> AddIncidentAsync(SecurityIncident incident)
    {
        incident.Id = _incidentIdCounter++;
        incident.DetectedAt = DateTime.UtcNow;
        incident.Status = IncidentStatus.New;
        _incidents.Add(incident);
        await SaveIncidentsAsync();
        _logger.LogInformation("ISO 27001: Incident {Id} cree - {Title}", incident.Id, incident.Title);
        return incident;
    }

    public async Task UpdateIncidentAsync(SecurityIncident incident)
    {
        var index = _incidents.FindIndex(i => i.Id == incident.Id);
        if (index >= 0)
        {
            _incidents[index] = incident;
            await SaveIncidentsAsync();
            _logger.LogInformation("ISO 27001: Incident {Id} mis a jour - Statut: {Status}", incident.Id, incident.Status);
        }
    }

    public IEnumerable<SecurityIncident> GetOpenIncidents()
    {
        return _incidents.Where(i => i.Status != IncidentStatus.Closed);
    }

    #endregion

    #region Politiques de Securite

    public IEnumerable<SecurityPolicy> GetAllPolicies() => _policies;

    public SecurityPolicy? GetPolicy(int id) => _policies.Find(p => p.Id == id);

    public async Task<SecurityPolicy> AddPolicyAsync(SecurityPolicy policy)
    {
        policy.Id = _policyIdCounter++;
        policy.CreatedDate = DateTime.UtcNow;
        policy.Status = PolicyStatus.Draft;
        _policies.Add(policy);
        await SavePoliciesAsync();
        _logger.LogInformation("ISO 27001: Politique {Code} creee - {Title}", policy.Code, policy.Title);
        return policy;
    }

    public async Task UpdatePolicyAsync(SecurityPolicy policy)
    {
        var index = _policies.FindIndex(p => p.Id == policy.Id);
        if (index >= 0)
        {
            _policies[index] = policy;
            await SavePoliciesAsync();
            _logger.LogInformation("ISO 27001: Politique {Code} mise a jour", policy.Code);
        }
    }

    #endregion

    #region Resume et Score

    public Iso27001Summary GetSummary()
    {
        // Retourner le cache si valide
        if (!_summaryInvalid && _cachedSummary != null)
            return _cachedSummary;

        var summary = BuildSummary();
        _cachedSummary = summary;
        _summaryInvalid = false;
        return summary;
    }

    private Iso27001Summary BuildSummary()
    {
        // Calculer les compteurs en une seule passe
        int implemented = 0, partial = 0, notImplemented = 0, notApplicable = 0;
        
        foreach (var control in _controls)
        {
            switch (control.Status)
            {
                case Iso27001ControlStatus.Implemented:
                case Iso27001ControlStatus.Effective:
                    implemented++;
                    break;
                case Iso27001ControlStatus.PartiallyImplemented:
                    partial++;
                    break;
                case Iso27001ControlStatus.NotImplemented:
                    notImplemented++;
                    break;
                case Iso27001ControlStatus.NotApplicable:
                    notApplicable++;
                    break;
            }
        }

        var summary = new Iso27001Summary
        {
            TotalControls = _controls.Count,
            ImplementedControls = implemented,
            PartiallyImplementedControls = partial,
            NotImplementedControls = notImplemented,
            NotApplicableControls = notApplicable
        };

        var applicable = summary.TotalControls - notApplicable;
        if (applicable > 0)
        {
            summary.CompliancePercentage = Math.Round(
                (implemented + partial * 0.5) / applicable * 100, 2);
        }

        // Resume par categorie - utiliser le dictionnaire pré-indexé
        foreach (var (category, controls) in _controlsByCategory)
        {
            int catImplemented = 0, catNotApplicable = 0;
            
            foreach (var control in controls)
            {
                if (control.Status is Iso27001ControlStatus.Implemented or Iso27001ControlStatus.Effective)
                    catImplemented++;
                else if (control.Status == Iso27001ControlStatus.NotApplicable)
                    catNotApplicable++;
            }

            var catApplicable = controls.Count - catNotApplicable;
            var catSummary = new CategorySummary
            {
                CategoryName = CategoryNames.TryGetValue(category, out var name) ? name : category,
                TotalControls = controls.Count,
                ImplementedControls = catImplemented,
                CompliancePercentage = catApplicable > 0 ? Math.Round((double)catImplemented / catApplicable * 100, 2) : 0
            };
            
            summary.ByCategory[category] = catSummary;
        }

        return summary;
    }

    public Task<double> CalculateComplianceScoreAsync()
    {
        return Task.FromResult(GetSummary().CompliancePercentage);
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
                    foreach (var saved in savedControls)
                    {
                        if (_controlsById.TryGetValue(saved.Id, out var control))
                        {
                            control.Status = saved.Status;
                            control.Evidence = saved.Evidence;
                            control.LastReviewDate = saved.LastReviewDate;
                            control.ResponsiblePerson = saved.ResponsiblePerson;
                            control.ImplementationLevel = saved.ImplementationLevel;
                        }
                    }
                    InvalidateSummaryCache();
                }
            }

            if (File.Exists(RisksConfigPath))
            {
                var json = File.ReadAllText(RisksConfigPath);
                _risks = JsonSerializer.Deserialize<List<RiskAssessment>>(json) ?? new();
                _riskIdCounter = _risks.Count > 0 ? _risks.Max(r => r.Id) + 1 : 1;
            }

            if (File.Exists(IncidentsConfigPath))
            {
                var json = File.ReadAllText(IncidentsConfigPath);
                _incidents = JsonSerializer.Deserialize<List<SecurityIncident>>(json) ?? new();
                _incidentIdCounter = _incidents.Count > 0 ? _incidents.Max(i => i.Id) + 1 : 1;
            }

            if (File.Exists(PoliciesConfigPath))
            {
                var json = File.ReadAllText(PoliciesConfigPath);
                _policies = JsonSerializer.Deserialize<List<SecurityPolicy>>(json) ?? new();
                _policyIdCounter = _policies.Count > 0 ? _policies.Max(p => p.Id) + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement donnees ISO 27001");
        }
    }

    private async Task SaveControlsAsync()
    {
        var json = JsonSerializer.Serialize(_controls, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ControlsConfigPath, json, System.Text.Encoding.UTF8);
    }

    private async Task SaveRisksAsync()
    {
        var json = JsonSerializer.Serialize(_risks, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(RisksConfigPath, json, System.Text.Encoding.UTF8);
    }

    private async Task SaveIncidentsAsync()
    {
        var json = JsonSerializer.Serialize(_incidents, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(IncidentsConfigPath, json, System.Text.Encoding.UTF8);
    }

    private async Task SavePoliciesAsync()
    {
        var json = JsonSerializer.Serialize(_policies, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PoliciesConfigPath, json, System.Text.Encoding.UTF8);
    }

    #endregion
}
