namespace NetworkFirewall.Models;

#region ISO/IEC 27001 - Système de Management de la Sécurité de l'Information

/// <summary>
/// Représente un contrôle de sécurité ISO 27001 (Annexe A)
/// </summary>
public class Iso27001Control
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Iso27001ControlStatus Status { get; set; }
    public ImplementationLevel ImplementationLevel { get; set; }
    public string? Evidence { get; set; }
    public string? ResponsiblePerson { get; set; }
    public DateTime? LastReviewDate { get; set; }
    public DateTime? NextReviewDate { get; set; }
    public List<string> RelatedRisks { get; set; } = new();
    public List<ComplianceEvidence> Evidences { get; set; } = new();
}

public enum Iso27001ControlStatus
{
    NotApplicable,
    NotImplemented,
    PartiallyImplemented,
    Implemented,
    Effective
}

public enum ImplementationLevel
{
    None = 0,
    Initial = 1,
    Repeatable = 2,
    Defined = 3,
    Managed = 4,
    Optimized = 5
}

/// <summary>
/// Catégories de contrôles ISO 27001:2022 (Annexe A)
/// </summary>
public static class Iso27001Categories
{
    public const string OrganizationalControls = "A.5";
    public const string PeopleControls = "A.6";
    public const string PhysicalControls = "A.7";
    public const string TechnologicalControls = "A.8";
}

/// <summary>
/// Évaluation des risques selon ISO 27001
/// </summary>
public class RiskAssessment
{
    public int Id { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public string ThreatDescription { get; set; } = string.Empty;
    public string VulnerabilityDescription { get; set; } = string.Empty;
    public RiskLikelihood Likelihood { get; set; }
    public RiskImpact Impact { get; set; }
    public int RiskScore => (int)Likelihood * (int)Impact;
    public ComplianceRiskLevel RiskLevel => CalculateRiskLevel();
    public RiskTreatment Treatment { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? ResidualRiskDescription { get; set; }
    public int? ResidualRiskScore { get; set; }
    public string? Owner { get; set; }
    public DateTime AssessmentDate { get; set; }
    public DateTime? ReviewDate { get; set; }
    public RiskStatus Status { get; set; }

    private ComplianceRiskLevel CalculateRiskLevel()
    {
        return RiskScore switch
        {
            <= 4 => ComplianceRiskLevel.Low,
            <= 9 => ComplianceRiskLevel.Medium,
            <= 15 => ComplianceRiskLevel.High,
            _ => ComplianceRiskLevel.Critical
        };
    }
}

public enum AssetType
{
    Information,
    Software,
    Hardware,
    Service,
    People,
    Infrastructure,
    IntangibleAsset
}

public enum RiskLikelihood
{
    Rare = 1,
    Unlikely = 2,
    Possible = 3,
    Likely = 4,
    AlmostCertain = 5
}

public enum RiskImpact
{
    Negligible = 1,
    Minor = 2,
    Moderate = 3,
    Major = 4,
    Catastrophic = 5
}

public enum ComplianceRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum RiskTreatment
{
    Accept,
    Mitigate,
    Transfer,
    Avoid
}

public enum RiskStatus
{
    Identified,
    UnderAssessment,
    TreatmentPlanned,
    TreatmentInProgress,
    Treated,
    Accepted,
    Closed
}

/// <summary>
/// Incident de sécurité selon ISO 27001
/// </summary>
public class SecurityIncident
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; }
    public IncidentCategory Category { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? AffectedAssets { get; set; }
    public string? RootCause { get; set; }
    public string? ImpactDescription { get; set; }
    public string? ResponseActions { get; set; }
    public string? LessonsLearned { get; set; }
    public string? ReportedBy { get; set; }
    public string? AssignedTo { get; set; }
    public List<string> RelatedControls { get; set; } = new();
}

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum IncidentCategory
{
    Malware,
    UnauthorizedAccess,
    DataBreach,
    DenialOfService,
    SocialEngineering,
    PhysicalSecurity,
    PolicyViolation,
    Other
}

public enum IncidentStatus
{
    New,
    Investigating,
    Containment,
    Eradication,
    Recovery,
    LessonsLearned,
    Closed
}

/// <summary>
/// Politique de sécurité
/// </summary>
public class SecurityPolicy
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public PolicyStatus Status { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Owner { get; set; }
    public List<string> RelatedControls { get; set; } = new();
    public string? Content { get; set; }
}

public enum PolicyStatus
{
    Draft,
    UnderReview,
    Approved,
    Effective,
    UnderRevision,
    Retired
}

#endregion

#region ISO/IEC 15408 - Critères Communs (Common Criteria)

/// <summary>
/// Profil de Protection (PP) selon ISO 15408
/// </summary>
public class ProtectionProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public EvaluationAssuranceLevel AssuranceLevel { get; set; }
    public List<SecurityObjective> SecurityObjectives { get; set; } = new();
    public List<SecurityFunctionalRequirement> FunctionalRequirements { get; set; } = new();
    public List<SecurityAssuranceRequirement> AssuranceRequirements { get; set; } = new();
    public List<ThreatDefinition> Threats { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> OrganizationalSecurityPolicies { get; set; } = new();
}

/// <summary>
/// Cible de Sécurité (ST) selon ISO 15408
/// </summary>
public class SecurityTarget
{
    public string Id { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ProtectionProfileId { get; set; }
    public EvaluationAssuranceLevel ClaimedAssuranceLevel { get; set; }
    public EvaluationStatus EvaluationStatus { get; set; }
    public List<SecurityObjective> SecurityObjectives { get; set; } = new();
    public List<SecurityFunctionalRequirement> FunctionalRequirements { get; set; } = new();
    public ToeDescription ToeDescription { get; set; } = new();
    public DateTime? EvaluationDate { get; set; }
    public string? CertificationId { get; set; }
}

/// <summary>
/// Description de la Cible d'Évaluation (TOE)
/// </summary>
public class ToeDescription
{
    public string PhysicalScope { get; set; } = string.Empty;
    public string LogicalScope { get; set; } = string.Empty;
    public List<string> Interfaces { get; set; } = new();
    public List<string> SecurityFeatures { get; set; } = new();
}

/// <summary>
/// Niveau d'Assurance d'Évaluation (EAL)
/// </summary>
public enum EvaluationAssuranceLevel
{
    EAL1 = 1, // Testé fonctionnellement
    EAL2 = 2, // Testé structurellement
    EAL3 = 3, // Testé et vérifié méthodiquement
    EAL4 = 4, // Conçu, testé et vérifié méthodiquement
    EAL5 = 5, // Conçu et testé de manière semi-formelle
    EAL6 = 6, // Conception vérifiée et testé de manière semi-formelle
    EAL7 = 7  // Conception vérifiée et testé de manière formelle
}

public enum EvaluationStatus
{
    NotStarted,
    InProgress,
    Evaluated,
    Certified,
    CertificationExpired
}

/// <summary>
/// Objectif de Sécurité
/// </summary>
public class SecurityObjective
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ObjectiveType Type { get; set; }
    public List<string> AddressedThreats { get; set; } = new();
    public List<string> AddressedPolicies { get; set; } = new();
    public ObjectiveStatus Status { get; set; }
}

public enum ObjectiveType
{
    ForToe,           // Objectifs pour la TOE
    ForEnvironment    // Objectifs pour l'environnement opérationnel
}

public enum ObjectiveStatus
{
    Defined,
    PartiallyMet,
    Met,
    NotMet
}

/// <summary>
/// Exigence Fonctionnelle de Sécurité (SFR)
/// </summary>
public class SecurityFunctionalRequirement
{
    public string Id { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RequirementStatus Status { get; set; }
    public List<string> RelatedObjectives { get; set; } = new();
    public string? Implementation { get; set; }
    public string? TestEvidence { get; set; }
}

/// <summary>
/// Classes fonctionnelles ISO 15408 Part 2
/// </summary>
public static class FunctionalClasses
{
    public const string FAU = "FAU"; // Security Audit
    public const string FCO = "FCO"; // Communication
    public const string FCS = "FCS"; // Cryptographic Support
    public const string FDP = "FDP"; // User Data Protection
    public const string FIA = "FIA"; // Identification and Authentication
    public const string FMT = "FMT"; // Security Management
    public const string FPR = "FPR"; // Privacy
    public const string FPT = "FPT"; // Protection of TSF
    public const string FRU = "FRU"; // Resource Utilization
    public const string FTA = "FTA"; // TOE Access
    public const string FTP = "FTP"; // Trusted Path/Channels
}

/// <summary>
/// Exigence d'Assurance de Sécurité (SAR)
/// </summary>
public class SecurityAssuranceRequirement
{
    public string Id { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Description { get; set; } = string.Empty;
    public RequirementStatus Status { get; set; }
    public string? Evidence { get; set; }
}

/// <summary>
/// Classes d'assurance ISO 15408 Part 3
/// </summary>
public static class AssuranceClasses
{
    public const string ADV = "ADV"; // Development
    public const string AGD = "AGD"; // Guidance Documents
    public const string ALC = "ALC"; // Life-Cycle Support
    public const string ASE = "ASE"; // Security Target Evaluation
    public const string ATE = "ATE"; // Tests
    public const string AVA = "AVA"; // Vulnerability Assessment
}

public enum RequirementStatus
{
    NotAddressed,
    PartiallyAddressed,
    FullyAddressed,
    Verified
}

/// <summary>
/// Définition de Menace
/// </summary>
public class ThreatDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ThreatAgent { get; set; } = string.Empty;
    public string AttackMethod { get; set; } = string.Empty;
    public string AffectedAsset { get; set; } = string.Empty;
    public List<string> Countermeasures { get; set; } = new();
}

#endregion

#region Éléments Communs

/// <summary>
/// Preuve de conformité
/// </summary>
public class ComplianceEvidence
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public EvidenceType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public DateTime CollectedDate { get; set; }
    public string? CollectedBy { get; set; }
    public string? ControlId { get; set; }
    public bool IsValid { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public enum EvidenceType
{
    Document,
    Screenshot,
    Log,
    Configuration,
    TestResult,
    Interview,
    Observation,
    Artifact
}

/// <summary>
/// Résultat d'audit de conformité
/// </summary>
public class ComplianceAuditResult
{
    public int Id { get; set; }
    public string AuditId { get; set; } = string.Empty;
    public string Standard { get; set; } = string.Empty;
    public DateTime AuditDate { get; set; }
    public string? Auditor { get; set; }
    public AuditType AuditType { get; set; }
    public AuditScope Scope { get; set; }
    public double OverallComplianceScore { get; set; }
    public List<AuditFinding> Findings { get; set; } = new();
    public List<ControlAssessment> ControlAssessments { get; set; } = new();
    public string? Summary { get; set; }
    public string? Recommendations { get; set; }
    public DateTime? NextAuditDate { get; set; }
}

public enum AuditType
{
    Internal,
    External,
    Certification,
    Surveillance,
    SelfAssessment
}

public class AuditScope
{
    public List<string> IncludedControls { get; set; } = new();
    public List<string> IncludedSystems { get; set; } = new();
    public List<string> ExcludedItems { get; set; } = new();
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}

/// <summary>
/// Constatation d'audit
/// </summary>
public class AuditFinding
{
    public int Id { get; set; }
    public string ControlId { get; set; } = string.Empty;
    public FindingType Type { get; set; }
    public FindingSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public string? RootCause { get; set; }
    public string? Recommendation { get; set; }
    public FindingStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public string? AssignedTo { get; set; }
    public string? CorrectiveAction { get; set; }
    public DateTime? ClosedDate { get; set; }
}

public enum FindingType
{
    NonConformity,
    Observation,
    Opportunity,
    Strength
}

public enum FindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum FindingStatus
{
    Open,
    InProgress,
    PendingVerification,
    Closed,
    Deferred
}

/// <summary>
/// Évaluation d'un contrôle
/// </summary>
public class ControlAssessment
{
    public string ControlId { get; set; } = string.Empty;
    public ControlAssessmentResult Result { get; set; }
    public double Score { get; set; }
    public string? Notes { get; set; }
    public List<string> EvidenceIds { get; set; } = new();
}

public enum ControlAssessmentResult
{
    NotApplicable,
    NotAssessed,
    NonCompliant,
    PartiallyCompliant,
    Compliant
}

/// <summary>
/// Tableau de bord de conformité
/// </summary>
public class ComplianceDashboard
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public Iso27001Summary Iso27001 { get; set; } = new();
    public Iso15408Summary Iso15408 { get; set; } = new();
    public List<RiskSummary> TopRisks { get; set; } = new();
    public List<SecurityIncident> RecentIncidents { get; set; } = new();
    public List<AuditFinding> OpenFindings { get; set; } = new();
    public List<UpcomingTask> UpcomingTasks { get; set; } = new();
}

public class Iso27001Summary
{
    public int TotalControls { get; set; }
    public int ImplementedControls { get; set; }
    public int PartiallyImplementedControls { get; set; }
    public int NotImplementedControls { get; set; }
    public int NotApplicableControls { get; set; }
    public double CompliancePercentage { get; set; }
    public Dictionary<string, CategorySummary> ByCategory { get; set; } = new();
    public DateTime? LastAuditDate { get; set; }
    public DateTime? CertificationExpiry { get; set; }
}

public class CategorySummary
{
    public string CategoryName { get; set; } = string.Empty;
    public int TotalControls { get; set; }
    public int ImplementedControls { get; set; }
    public double CompliancePercentage { get; set; }
}

public class Iso15408Summary
{
    public EvaluationAssuranceLevel TargetEal { get; set; }
    public EvaluationStatus CurrentStatus { get; set; }
    public int TotalFunctionalRequirements { get; set; }
    public int MetFunctionalRequirements { get; set; }
    public int TotalAssuranceRequirements { get; set; }
    public int MetAssuranceRequirements { get; set; }
    public double FunctionalCompliancePercentage { get; set; }
    public double AssuranceCompliancePercentage { get; set; }
    public DateTime? EvaluationDate { get; set; }
    public string? CertificationId { get; set; }
}

public class RiskSummary
{
    public int RiskId { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public string ThreatDescription { get; set; } = string.Empty;
    public ComplianceRiskLevel RiskLevel { get; set; }
    public int RiskScore { get; set; }
    public RiskStatus Status { get; set; }
}

public class UpcomingTask
{
    public string TaskType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string? AssignedTo { get; set; }
    public TaskPriority Priority { get; set; }
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

#endregion
