using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IIso15408Service
{
    // Protection Profile
    ProtectionProfile? GetProtectionProfile();
    Task UpdateProtectionProfileAsync(ProtectionProfile profile);
    
    // Security Target
    SecurityTarget? GetSecurityTarget();
    Task UpdateSecurityTargetAsync(SecurityTarget target);
    
    // Functional Requirements
    IEnumerable<SecurityFunctionalRequirement> GetFunctionalRequirements();
    SecurityFunctionalRequirement? GetFunctionalRequirement(string id);
    Task UpdateFunctionalRequirementAsync(SecurityFunctionalRequirement requirement);
    
    // Assurance Requirements
    IEnumerable<SecurityAssuranceRequirement> GetAssuranceRequirements();
    SecurityAssuranceRequirement? GetAssuranceRequirement(string id);
    Task UpdateAssuranceRequirementAsync(SecurityAssuranceRequirement requirement);
    
    // Security Objectives
    IEnumerable<SecurityObjective> GetSecurityObjectives();
    Task UpdateSecurityObjectiveAsync(SecurityObjective objective);
    
    // Threats
    IEnumerable<ThreatDefinition> GetThreats();
    Task<ThreatDefinition> AddThreatAsync(ThreatDefinition threat);
    
    // Evaluation
    Task<EvaluationResult> EvaluateComplianceAsync();
    Iso15408Summary GetSummary();
}

public class EvaluationResult
{
    public EvaluationAssuranceLevel AchievedLevel { get; set; }
    public bool MeetsTargetLevel { get; set; }
    public double FunctionalScore { get; set; }
    public double AssuranceScore { get; set; }
    public List<string> Gaps { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class Iso15408Service : IIso15408Service
{
    private readonly ILogger<Iso15408Service> _logger;
    private ProtectionProfile _protectionProfile = new();
    private SecurityTarget _securityTarget = new();
    private List<SecurityFunctionalRequirement> _functionalRequirements = new();
    private List<SecurityAssuranceRequirement> _assuranceRequirements = new();
    private List<SecurityObjective> _objectives = new();
    private List<ThreatDefinition> _threats = new();

    private const string ConfigPath = "iso15408_config.json";

    public Iso15408Service(ILogger<Iso15408Service> logger)
    {
        _logger = logger;
        InitializeDefaults();
        LoadData();
    }

    private void InitializeDefaults()
    {
        // Initialiser le Profil de Protection pour un Firewall/IDS
        _protectionProfile = new ProtectionProfile
        {
            Id = "PP-FW-001",
            Name = "Protection Profile for Network Firewalls",
            Description = "Profil de protection pour les pare-feu reseau conformement a l'ISO/IEC 15408",
            Version = "1.0",
            AssuranceLevel = EvaluationAssuranceLevel.EAL4,
            Assumptions = new List<string>
            {
                "A.PHYSICAL - L'environnement physique est securise",
                "A.NOEVIL - Les administrateurs sont dignes de confiance",
                "A.MANAGE - Une gestion competente est disponible",
                "A.CONNECT - Les systemes connectes sont surs",
                "A.AUDIT_REVIEW - Les journaux d'audit sont regulierement examines"
            },
            OrganizationalSecurityPolicies = new List<string>
            {
                "P.ACCESS - Seuls les flux autorises sont permis",
                "P.AUDIT - Toutes les actions de securite sont journalisees",
                "P.CRYPTO - La cryptographie approuvee est utilisee",
                "P.MANAGE - Gestion securisee obligatoire"
            }
        };

        // Initialiser la Cible de Securite
        _securityTarget = new SecurityTarget
        {
            Id = "ST-NETGUARD-001",
            ProductName = "NetGuard Network Firewall Monitor",
            ProductVersion = "1.0",
            Description = "Systeme de surveillance et de protection reseau avec fonctions de pare-feu, detection d'intrusion et analyse de trafic",
            ProtectionProfileId = _protectionProfile.Id,
            ClaimedAssuranceLevel = EvaluationAssuranceLevel.EAL4,
            EvaluationStatus = EvaluationStatus.InProgress,
            ToeDescription = new ToeDescription
            {
                PhysicalScope = "Application web .NET 8 deployee sur serveur Linux",
                LogicalScope = "Surveillance reseau, analyse de paquets, detection de menaces, gestion DHCP/NAT",
                Interfaces = new List<string>
                {
                    "Interface Web (HTTPS port 9764)",
                    "API REST securisee",
                    "Interface de capture reseau (libpcap)",
                    "Interface de gestion SSH"
                },
                SecurityFeatures = new List<string>
                {
                    "Filtrage de paquets base sur regles",
                    "Detection d'intrusion (IDS)",
                    "Analyse de menaces en temps reel",
                    "Journalisation securisee",
                    "Gestion des acces"
                }
            }
        };

        // Initialiser les Objectifs de Securite
        _objectives = new List<SecurityObjective>
        {
            new() { Id = "O.AUDIT", Name = "Audit de securite", Description = "La TOE doit fournir des mecanismes pour enregistrer les evenements de securite pertinents", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.Met },
            new() { Id = "O.FILTER", Name = "Filtrage du trafic", Description = "La TOE doit filtrer le trafic reseau selon les politiques definies", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.Met },
            new() { Id = "O.DETECT", Name = "Detection d'intrusion", Description = "La TOE doit detecter les tentatives d'intrusion et les activites malveillantes", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.Met },
            new() { Id = "O.MANAGE", Name = "Gestion securisee", Description = "La TOE doit fournir des mecanismes de gestion securises", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.PartiallyMet },
            new() { Id = "O.PROTECT", Name = "Protection des donnees", Description = "La TOE doit proteger les donnees sensibles de securite", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.Met },
            new() { Id = "O.CRYPTO", Name = "Services cryptographiques", Description = "La TOE doit utiliser des mecanismes cryptographiques approuves", Type = ObjectiveType.ForToe, Status = ObjectiveStatus.Met },
            new() { Id = "OE.PHYSICAL", Name = "Securite physique", Description = "L'environnement doit fournir une protection physique adequate", Type = ObjectiveType.ForEnvironment, Status = ObjectiveStatus.Defined },
            new() { Id = "OE.ADMIN", Name = "Administrateurs de confiance", Description = "Les administrateurs doivent etre formes et dignes de confiance", Type = ObjectiveType.ForEnvironment, Status = ObjectiveStatus.Defined },
            new() { Id = "OE.TIME", Name = "Source de temps fiable", Description = "Une source de temps fiable doit etre disponible", Type = ObjectiveType.ForEnvironment, Status = ObjectiveStatus.Met }
        };

        // Initialiser les Exigences Fonctionnelles de Securite (SFR)
        _functionalRequirements = new List<SecurityFunctionalRequirement>
        {
            // FAU - Security Audit
            new() { Id = "FAU_GEN.1", Class = "FAU", Family = "Security audit data generation", Component = "Audit data generation", Description = "La TSF doit etre capable de generer un enregistrement d'audit pour les evenements auditables", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FAU_GEN.2", Class = "FAU", Family = "Security audit data generation", Component = "User identity association", Description = "La TSF doit enregistrer l'identite de l'utilisateur associee a l'evenement auditable", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FAU_SAR.1", Class = "FAU", Family = "Security audit review", Component = "Audit review", Description = "La TSF doit fournir aux utilisateurs autorises la capacite de lire les informations d'audit", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FAU_STG.1", Class = "FAU", Family = "Security audit event storage", Component = "Protected audit trail storage", Description = "La TSF doit proteger les enregistrements d'audit stockes", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FAU_STG.4", Class = "FAU", Family = "Security audit event storage", Component = "Prevention of audit data loss", Description = "La TSF doit prevenir la perte des donnees d'audit", Status = RequirementStatus.PartiallyAddressed },

            // FCS - Cryptographic Support
            new() { Id = "FCS_CKM.1", Class = "FCS", Family = "Cryptographic key management", Component = "Cryptographic key generation", Description = "La TSF doit generer des cles cryptographiques conformement a un algorithme specifie", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FCS_CKM.4", Class = "FCS", Family = "Cryptographic key management", Component = "Cryptographic key destruction", Description = "La TSF doit detruire les cles cryptographiques de maniere securisee", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "FCS_COP.1", Class = "FCS", Family = "Cryptographic operation", Component = "Cryptographic operation", Description = "La TSF doit effectuer les operations cryptographiques conformement aux algorithmes specifies", Status = RequirementStatus.FullyAddressed },

            // FDP - User Data Protection
            new() { Id = "FDP_IFC.1", Class = "FDP", Family = "Information flow control", Component = "Subset information flow control", Description = "La TSF doit appliquer la politique de controle de flux d'information", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FDP_IFF.1", Class = "FDP", Family = "Information flow control functions", Component = "Simple security attributes", Description = "La TSF doit appliquer la SFP de controle de flux basee sur les attributs de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FDP_ACC.1", Class = "FDP", Family = "Access control policy", Component = "Subset access control", Description = "La TSF doit appliquer la politique de controle d'acces", Status = RequirementStatus.FullyAddressed },

            // FIA - Identification and Authentication
            new() { Id = "FIA_ATD.1", Class = "FIA", Family = "User attribute definition", Component = "User attribute definition", Description = "La TSF doit maintenir les attributs de securite des utilisateurs", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FIA_UAU.2", Class = "FIA", Family = "User authentication", Component = "User authentication before any action", Description = "La TSF doit exiger l'authentification avant toute action", Status = RequirementStatus.NotAddressed },
            new() { Id = "FIA_UID.2", Class = "FIA", Family = "User identification", Component = "User identification before any action", Description = "La TSF doit exiger l'identification avant toute action", Status = RequirementStatus.NotAddressed },

            // FMT - Security Management
            new() { Id = "FMT_MSA.1", Class = "FMT", Family = "Management of security attributes", Component = "Management of security attributes", Description = "La TSF doit appliquer la SFP pour restreindre la gestion des attributs de securite", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "FMT_MSA.3", Class = "FMT", Family = "Management of security attributes", Component = "Static attribute initialisation", Description = "La TSF doit fournir des valeurs par defaut restrictives pour les attributs de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FMT_SMF.1", Class = "FMT", Family = "Specification of management functions", Component = "Specification of management functions", Description = "La TSF doit etre capable d'effectuer les fonctions de gestion de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FMT_SMR.1", Class = "FMT", Family = "Security management roles", Component = "Security roles", Description = "La TSF doit maintenir les roles de securite", Status = RequirementStatus.PartiallyAddressed },

            // FPT - Protection of TSF
            new() { Id = "FPT_STM.1", Class = "FPT", Family = "Time stamps", Component = "Reliable time stamps", Description = "La TSF doit fournir des horodatages fiables", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FPT_TST.1", Class = "FPT", Family = "TSF self test", Component = "TSF testing", Description = "La TSF doit effectuer des auto-tests pour verifier son integrite", Status = RequirementStatus.PartiallyAddressed },

            // FTA - TOE Access
            new() { Id = "FTA_SSL.3", Class = "FTA", Family = "Session locking", Component = "TSF-initiated termination", Description = "La TSF doit terminer une session apres une periode d'inactivite", Status = RequirementStatus.NotAddressed },

            // FTP - Trusted Path/Channels
            new() { Id = "FTP_ITC.1", Class = "FTP", Family = "Inter-TSF trusted channel", Component = "Inter-TSF trusted channel", Description = "La TSF doit fournir un canal de communication securise entre elle et les produits TI distants", Status = RequirementStatus.FullyAddressed },
            new() { Id = "FTP_TRP.1", Class = "FTP", Family = "Trusted path", Component = "Trusted path", Description = "La TSF doit fournir un chemin de communication securise pour l'authentification initiale", Status = RequirementStatus.FullyAddressed }
        };

        // Initialiser les Exigences d'Assurance de Securite (SAR) pour EAL4
        _assuranceRequirements = new List<SecurityAssuranceRequirement>
        {
            // ADV - Development
            new() { Id = "ADV_ARC.1", Class = "ADV", Family = "Security architecture", Component = "Security architecture description", Level = 4, Description = "Description de l'architecture de securite", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ADV_FSP.4", Class = "ADV", Family = "Functional specification", Component = "Complete functional specification", Level = 4, Description = "Specification fonctionnelle complete avec details de securite", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ADV_IMP.1", Class = "ADV", Family = "Implementation representation", Component = "Implementation representation of the TSF", Level = 4, Description = "Representation de l'implementation de la TSF", Status = RequirementStatus.NotAddressed },
            new() { Id = "ADV_TDS.3", Class = "ADV", Family = "TOE design", Component = "Basic modular design", Level = 4, Description = "Conception modulaire de base", Status = RequirementStatus.PartiallyAddressed },

            // AGD - Guidance Documents
            new() { Id = "AGD_OPE.1", Class = "AGD", Family = "Operational user guidance", Component = "Operational user guidance", Level = 4, Description = "Guide d'utilisation operationnelle", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "AGD_PRE.1", Class = "AGD", Family = "Preparative procedures", Component = "Preparative procedures", Level = 4, Description = "Procedures de preparation", Status = RequirementStatus.PartiallyAddressed },

            // ALC - Life-Cycle Support
            new() { Id = "ALC_CMC.4", Class = "ALC", Family = "CM capabilities", Component = "Production support, acceptance procedures and automation", Level = 4, Description = "Gestion de configuration avec automatisation", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ALC_CMS.4", Class = "ALC", Family = "CM scope", Component = "Problem tracking CM coverage", Level = 4, Description = "Couverture de la gestion de configuration pour le suivi des problemes", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ALC_DEL.1", Class = "ALC", Family = "Delivery", Component = "Delivery procedures", Level = 4, Description = "Procedures de livraison", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ALC_DVS.1", Class = "ALC", Family = "Development security", Component = "Identification of security measures", Level = 4, Description = "Identification des mesures de securite de developpement", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ALC_LCD.1", Class = "ALC", Family = "Life-cycle definition", Component = "Developer defined life-cycle model", Level = 4, Description = "Modele de cycle de vie defini par le developpeur", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ALC_TAT.1", Class = "ALC", Family = "Tools and techniques", Component = "Well-defined development tools", Level = 4, Description = "Outils de developpement bien definis", Status = RequirementStatus.FullyAddressed },

            // ASE - Security Target Evaluation
            new() { Id = "ASE_CCL.1", Class = "ASE", Family = "Conformance claims", Component = "Conformance claims", Level = 4, Description = "Declarations de conformite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ASE_ECD.1", Class = "ASE", Family = "Extended components definition", Component = "Extended components definition", Level = 4, Description = "Definition des composants etendus", Status = RequirementStatus.NotAddressed },
            new() { Id = "ASE_INT.1", Class = "ASE", Family = "ST introduction", Component = "ST introduction", Level = 4, Description = "Introduction de la cible de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ASE_OBJ.2", Class = "ASE", Family = "Security objectives", Component = "Security objectives", Level = 4, Description = "Objectifs de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ASE_REQ.2", Class = "ASE", Family = "Security requirements", Component = "Derived security requirements", Level = 4, Description = "Exigences de securite derivees", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ASE_SPD.1", Class = "ASE", Family = "Security problem definition", Component = "Security problem definition", Level = 4, Description = "Definition du probleme de securite", Status = RequirementStatus.FullyAddressed },
            new() { Id = "ASE_TSS.1", Class = "ASE", Family = "TOE summary specification", Component = "TOE summary specification", Level = 4, Description = "Specification resumee de la TOE", Status = RequirementStatus.FullyAddressed },

            // ATE - Tests
            new() { Id = "ATE_COV.2", Class = "ATE", Family = "Coverage", Component = "Analysis of coverage", Level = 4, Description = "Analyse de la couverture des tests", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ATE_DPT.1", Class = "ATE", Family = "Depth", Component = "Testing: basic design", Level = 4, Description = "Tests: conception de base", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ATE_FUN.1", Class = "ATE", Family = "Functional tests", Component = "Functional testing", Level = 4, Description = "Tests fonctionnels", Status = RequirementStatus.PartiallyAddressed },
            new() { Id = "ATE_IND.2", Class = "ATE", Family = "Independent testing", Component = "Independent testing - sample", Level = 4, Description = "Tests independants - echantillon", Status = RequirementStatus.NotAddressed },

            // AVA - Vulnerability Assessment
            new() { Id = "AVA_VAN.3", Class = "AVA", Family = "Vulnerability analysis", Component = "Focused vulnerability analysis", Level = 4, Description = "Analyse de vulnerabilite ciblee", Status = RequirementStatus.PartiallyAddressed }
        };

        // Initialiser les Menaces
        _threats = new List<ThreatDefinition>
        {
            new() { Id = "T.BYPASS", Name = "Contournement du filtrage", Description = "Un attaquant tente de contourner les mecanismes de filtrage du pare-feu", ThreatAgent = "Attaquant externe", AttackMethod = "Exploitation de vulnerabilites, fragmentation de paquets", AffectedAsset = "Politique de filtrage", Countermeasures = new() { "O.FILTER", "FDP_IFC.1", "FDP_IFF.1" } },
            new() { Id = "T.TAMPER", Name = "Alteration des donnees", Description = "Un attaquant tente de modifier les donnees de configuration ou les journaux", ThreatAgent = "Attaquant interne/externe", AttackMethod = "Modification non autorisee", AffectedAsset = "Configuration et journaux", Countermeasures = new() { "O.PROTECT", "FAU_STG.1", "FPT_TST.1" } },
            new() { Id = "T.UNAUTH", Name = "Acces non autorise", Description = "Une personne non autorisee tente d'acceder a la gestion du systeme", ThreatAgent = "Utilisateur non autorise", AttackMethod = "Usurpation d'identite, force brute", AffectedAsset = "Interface d'administration", Countermeasures = new() { "O.MANAGE", "FIA_UAU.2", "FIA_UID.2" } },
            new() { Id = "T.AUDIT_LOSS", Name = "Perte des donnees d'audit", Description = "Les enregistrements d'audit sont perdus ou detruits", ThreatAgent = "Attaquant ou defaillance systeme", AttackMethod = "Suppression, corruption", AffectedAsset = "Journaux d'audit", Countermeasures = new() { "O.AUDIT", "FAU_STG.1", "FAU_STG.4" } },
            new() { Id = "T.CRYPTO", Name = "Compromission cryptographique", Description = "Les mecanismes cryptographiques sont compromis", ThreatAgent = "Attaquant avance", AttackMethod = "Cryptanalyse, vol de cles", AffectedAsset = "Cles et donnees chiffrees", Countermeasures = new() { "O.CRYPTO", "FCS_CKM.1", "FCS_CKM.4", "FCS_COP.1" } },
            new() { Id = "T.DOS", Name = "Deni de service", Description = "Un attaquant tente de rendre le systeme indisponible", ThreatAgent = "Attaquant externe", AttackMethod = "Inondation de paquets, epuisement des ressources", AffectedAsset = "Disponibilite du service", Countermeasures = new() { "O.FILTER", "O.DETECT" } }
        };
    }

    #region Protection Profile

    public ProtectionProfile? GetProtectionProfile() => _protectionProfile;

    public async Task UpdateProtectionProfileAsync(ProtectionProfile profile)
    {
        _protectionProfile = profile;
        await SaveDataAsync();
        _logger.LogInformation("ISO 15408: Profil de protection mis a jour");
    }

    #endregion

    #region Security Target

    public SecurityTarget? GetSecurityTarget() => _securityTarget;

    public async Task UpdateSecurityTargetAsync(SecurityTarget target)
    {
        _securityTarget = target;
        await SaveDataAsync();
        _logger.LogInformation("ISO 15408: Cible de securite mise a jour");
    }

    #endregion

    #region Functional Requirements

    public IEnumerable<SecurityFunctionalRequirement> GetFunctionalRequirements() => _functionalRequirements;

    public SecurityFunctionalRequirement? GetFunctionalRequirement(string id)
    {
        return _functionalRequirements.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateFunctionalRequirementAsync(SecurityFunctionalRequirement requirement)
    {
        var existing = GetFunctionalRequirement(requirement.Id);
        if (existing != null)
        {
            var index = _functionalRequirements.IndexOf(existing);
            _functionalRequirements[index] = requirement;
            await SaveDataAsync();
            _logger.LogInformation("ISO 15408: SFR {Id} mise a jour - Statut: {Status}", requirement.Id, requirement.Status);
        }
    }

    #endregion

    #region Assurance Requirements

    public IEnumerable<SecurityAssuranceRequirement> GetAssuranceRequirements() => _assuranceRequirements;

    public SecurityAssuranceRequirement? GetAssuranceRequirement(string id)
    {
        return _assuranceRequirements.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateAssuranceRequirementAsync(SecurityAssuranceRequirement requirement)
    {
        var existing = GetAssuranceRequirement(requirement.Id);
        if (existing != null)
        {
            var index = _assuranceRequirements.IndexOf(existing);
            _assuranceRequirements[index] = requirement;
            await SaveDataAsync();
            _logger.LogInformation("ISO 15408: SAR {Id} mise a jour - Statut: {Status}", requirement.Id, requirement.Status);
        }
    }

    #endregion

    #region Security Objectives

    public IEnumerable<SecurityObjective> GetSecurityObjectives() => _objectives;

    public async Task UpdateSecurityObjectiveAsync(SecurityObjective objective)
    {
        var existing = _objectives.FirstOrDefault(o => o.Id == objective.Id);
        if (existing != null)
        {
            var index = _objectives.IndexOf(existing);
            _objectives[index] = objective;
            await SaveDataAsync();
            _logger.LogInformation("ISO 15408: Objectif {Id} mis a jour - Statut: {Status}", objective.Id, objective.Status);
        }
    }

    #endregion

    #region Threats

    public IEnumerable<ThreatDefinition> GetThreats() => _threats;

    public async Task<ThreatDefinition> AddThreatAsync(ThreatDefinition threat)
    {
        _threats.Add(threat);
        await SaveDataAsync();
        _logger.LogInformation("ISO 15408: Menace {Id} ajoutee", threat.Id);
        return threat;
    }

    #endregion

    #region Evaluation

    public Task<EvaluationResult> EvaluateComplianceAsync()
    {
        var result = new EvaluationResult();

        // Calculer le score fonctionnel
        var totalFunctional = _functionalRequirements.Count;
        var metFunctional = _functionalRequirements.Count(r => 
            r.Status == RequirementStatus.FullyAddressed || r.Status == RequirementStatus.Verified);
        var partialFunctional = _functionalRequirements.Count(r => r.Status == RequirementStatus.PartiallyAddressed);
        
        result.FunctionalScore = totalFunctional > 0 
            ? Math.Round((metFunctional + partialFunctional * 0.5) / totalFunctional * 100, 2) 
            : 0;

        // Calculer le score d'assurance
        var totalAssurance = _assuranceRequirements.Count;
        var metAssurance = _assuranceRequirements.Count(r => 
            r.Status == RequirementStatus.FullyAddressed || r.Status == RequirementStatus.Verified);
        var partialAssurance = _assuranceRequirements.Count(r => r.Status == RequirementStatus.PartiallyAddressed);
        
        result.AssuranceScore = totalAssurance > 0 
            ? Math.Round((metAssurance + partialAssurance * 0.5) / totalAssurance * 100, 2) 
            : 0;

        // Determiner le niveau EAL atteint
        result.AchievedLevel = DetermineAchievedEal();
        result.MeetsTargetLevel = result.AchievedLevel >= _securityTarget.ClaimedAssuranceLevel;

        // Identifier les lacunes
        foreach (var req in _functionalRequirements.Where(r => r.Status == RequirementStatus.NotAddressed))
        {
            result.Gaps.Add($"SFR {req.Id}: {req.Description}");
        }
        foreach (var req in _assuranceRequirements.Where(r => r.Status == RequirementStatus.NotAddressed))
        {
            result.Gaps.Add($"SAR {req.Id}: {req.Description}");
        }

        // Generer des recommandations
        if (!result.MeetsTargetLevel)
        {
            result.Recommendations.Add($"Le niveau EAL{(int)_securityTarget.ClaimedAssuranceLevel} n'est pas encore atteint. Niveau actuel: EAL{(int)result.AchievedLevel}");
        }

        if (_functionalRequirements.Any(r => r.Class == "FIA" && r.Status != RequirementStatus.FullyAddressed))
        {
            result.Recommendations.Add("Implementer l'authentification et l'identification des utilisateurs (FIA)");
        }

        if (_functionalRequirements.Any(r => r.Class == "FTA" && r.Status != RequirementStatus.FullyAddressed))
        {
            result.Recommendations.Add("Implementer la gestion des sessions et le verrouillage automatique (FTA)");
        }

        return Task.FromResult(result);
    }

    private EvaluationAssuranceLevel DetermineAchievedEal()
    {
        // Verifier EAL par niveau (simplifie)
        var allEal1 = _assuranceRequirements.Where(r => r.Level <= 1)
            .All(r => r.Status >= RequirementStatus.PartiallyAddressed);
        if (!allEal1) return EvaluationAssuranceLevel.EAL1;

        var allEal2 = _assuranceRequirements.Where(r => r.Level <= 2)
            .All(r => r.Status >= RequirementStatus.PartiallyAddressed);
        if (!allEal2) return EvaluationAssuranceLevel.EAL1;

        var allEal3 = _assuranceRequirements.Where(r => r.Level <= 3)
            .All(r => r.Status >= RequirementStatus.PartiallyAddressed);
        if (!allEal3) return EvaluationAssuranceLevel.EAL2;

        var allEal4 = _assuranceRequirements.Where(r => r.Level <= 4)
            .All(r => r.Status >= RequirementStatus.PartiallyAddressed);
        if (!allEal4) return EvaluationAssuranceLevel.EAL3;

        return EvaluationAssuranceLevel.EAL4;
    }

    public Iso15408Summary GetSummary()
    {
        var summary = new Iso15408Summary
        {
            TargetEal = _securityTarget.ClaimedAssuranceLevel,
            CurrentStatus = _securityTarget.EvaluationStatus,
            TotalFunctionalRequirements = _functionalRequirements.Count,
            MetFunctionalRequirements = _functionalRequirements.Count(r => 
                r.Status == RequirementStatus.FullyAddressed || r.Status == RequirementStatus.Verified),
            TotalAssuranceRequirements = _assuranceRequirements.Count,
            MetAssuranceRequirements = _assuranceRequirements.Count(r => 
                r.Status == RequirementStatus.FullyAddressed || r.Status == RequirementStatus.Verified),
            EvaluationDate = _securityTarget.EvaluationDate,
            CertificationId = _securityTarget.CertificationId
        };

        summary.FunctionalCompliancePercentage = summary.TotalFunctionalRequirements > 0
            ? Math.Round((double)summary.MetFunctionalRequirements / summary.TotalFunctionalRequirements * 100, 2)
            : 0;

        summary.AssuranceCompliancePercentage = summary.TotalAssuranceRequirements > 0
            ? Math.Round((double)summary.MetAssuranceRequirements / summary.TotalAssuranceRequirements * 100, 2)
            : 0;

        return summary;
    }

    #endregion

    #region Persistence

    private void LoadData()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var data = JsonSerializer.Deserialize<Iso15408Data>(json);
                if (data != null)
                {
                    if (data.ProtectionProfile != null) _protectionProfile = data.ProtectionProfile;
                    if (data.SecurityTarget != null) _securityTarget = data.SecurityTarget;
                    if (data.FunctionalRequirements?.Any() == true) 
                    {
                        foreach (var saved in data.FunctionalRequirements)
                        {
                            var req = _functionalRequirements.FirstOrDefault(r => r.Id == saved.Id);
                            if (req != null)
                            {
                                req.Status = saved.Status;
                                req.Implementation = saved.Implementation;
                                req.TestEvidence = saved.TestEvidence;
                            }
                        }
                    }
                    if (data.AssuranceRequirements?.Any() == true)
                    {
                        foreach (var saved in data.AssuranceRequirements)
                        {
                            var req = _assuranceRequirements.FirstOrDefault(r => r.Id == saved.Id);
                            if (req != null)
                            {
                                req.Status = saved.Status;
                                req.Evidence = saved.Evidence;
                            }
                        }
                    }
                    if (data.Objectives?.Any() == true)
                    {
                        foreach (var saved in data.Objectives)
                        {
                            var obj = _objectives.FirstOrDefault(o => o.Id == saved.Id);
                            if (obj != null)
                            {
                                obj.Status = saved.Status;
                            }
                        }
                    }
                    if (data.Threats?.Any() == true) _threats = data.Threats;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement donnees ISO 15408");
        }
    }

    private async Task SaveDataAsync()
    {
        var data = new Iso15408Data
        {
            ProtectionProfile = _protectionProfile,
            SecurityTarget = _securityTarget,
            FunctionalRequirements = _functionalRequirements,
            AssuranceRequirements = _assuranceRequirements,
            Objectives = _objectives,
            Threats = _threats
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ConfigPath, json, System.Text.Encoding.UTF8);
    }

    private class Iso15408Data
    {
        public ProtectionProfile? ProtectionProfile { get; set; }
        public SecurityTarget? SecurityTarget { get; set; }
        public List<SecurityFunctionalRequirement>? FunctionalRequirements { get; set; }
        public List<SecurityAssuranceRequirement>? AssuranceRequirements { get; set; }
        public List<SecurityObjective>? Objectives { get; set; }
        public List<ThreatDefinition>? Threats { get; set; }
    }

    #endregion
}
