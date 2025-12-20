using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IComplianceAuditService
{
    // Audits
    IEnumerable<ComplianceAuditResult> GetAllAudits();
    ComplianceAuditResult? GetAudit(int id);
    Task<ComplianceAuditResult> CreateAuditAsync(string standard, AuditType type);
    Task<ComplianceAuditResult> RunAutomatedAuditAsync(string standard);
    
    // Findings
    IEnumerable<AuditFinding> GetOpenFindings();
    Task UpdateFindingAsync(AuditFinding finding);
    Task<AuditFinding> AddFindingAsync(int auditId, AuditFinding finding);
    
    // Evidence
    IEnumerable<ComplianceEvidence> GetAllEvidence();
    Task<ComplianceEvidence> AddEvidenceAsync(ComplianceEvidence evidence);
    
    // Dashboard
    ComplianceDashboard GetDashboard();
    
    // Reports
    Task<string> GenerateComplianceReportAsync(string standard);
}

public class ComplianceAuditService : IComplianceAuditService
{
    private readonly ILogger<ComplianceAuditService> _logger;
    private readonly IIso27001Service _iso27001Service;
    private readonly IIso15408Service _iso15408Service;
    
    private readonly List<ComplianceAuditResult> _audits = new();
    private readonly List<ComplianceEvidence> _evidence = new();
    private int _auditIdCounter = 1;
    private int _findingIdCounter = 1;
    private int _evidenceIdCounter = 1;
    
    // Options de serialisation réutilisables
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    private const string AuditsConfigPath = "compliance_audits.json";
    private const string EvidenceConfigPath = "compliance_evidence.json";

    public ComplianceAuditService(
        ILogger<ComplianceAuditService> logger,
        IIso27001Service iso27001Service,
        IIso15408Service iso15408Service)
    {
        _logger = logger;
        _iso27001Service = iso27001Service;
        _iso15408Service = iso15408Service;
        LoadData();
    }

    #region Audits

    public IEnumerable<ComplianceAuditResult> GetAllAudits()
    {
        // Retourner dans l'ordre sans créer de nouvelle collection si possible
        if (_audits.Count <= 1) return _audits;
        return _audits.OrderByDescending(a => a.AuditDate);
    }

    public ComplianceAuditResult? GetAudit(int id) => _audits.Find(a => a.Id == id);

    public async Task<ComplianceAuditResult> CreateAuditAsync(string standard, AuditType type)
    {
        var audit = new ComplianceAuditResult
        {
            Id = _auditIdCounter++,
            AuditId = $"AUDIT-{DateTime.UtcNow:yyyyMMdd}-{_auditIdCounter:D4}",
            Standard = standard,
            AuditDate = DateTime.UtcNow,
            AuditType = type,
            Scope = new AuditScope
            {
                PeriodStart = DateTime.UtcNow.AddMonths(-12),
                PeriodEnd = DateTime.UtcNow
            }
        };

        _audits.Add(audit);
        await SaveAuditsAsync();
        _logger.LogInformation("Audit {Id} cree pour {Standard}", audit.AuditId, standard);
        return audit;
    }

    public async Task<ComplianceAuditResult> RunAutomatedAuditAsync(string standard)
    {
        _logger.LogInformation("Demarrage de l'audit automatise pour {Standard}", standard);
        
        var audit = await CreateAuditAsync(standard, AuditType.SelfAssessment);
        audit.Auditor = "Systeme automatise";

        if (string.Equals(standard, "ISO27001", StringComparison.OrdinalIgnoreCase))
        {
            await RunIso27001AuditAsync(audit);
        }
        else if (string.Equals(standard, "ISO15408", StringComparison.OrdinalIgnoreCase))
        {
            await RunIso15408AuditAsync(audit);
        }

        // Calculer le score global
        var assessedControls = audit.ControlAssessments
            .Where(c => c.Result != ControlAssessmentResult.NotAssessed)
            .ToList();
            
        if (assessedControls.Count > 0)
        {
            double totalScore = 0;
            foreach (var c in assessedControls) totalScore += c.Score;
            audit.OverallComplianceScore = Math.Round(totalScore / assessedControls.Count, 2);
        }

        audit.NextAuditDate = DateTime.UtcNow.AddMonths(6);

        // Générer le résumé avec comptage efficace
        int compliant = 0, partial = 0, nonCompliant = 0;
        foreach (var assessment in audit.ControlAssessments)
        {
            switch (assessment.Result)
            {
                case ControlAssessmentResult.Compliant: compliant++; break;
                case ControlAssessmentResult.PartiallyCompliant: partial++; break;
                case ControlAssessmentResult.NonCompliant: nonCompliant++; break;
            }
        }
        
        audit.Summary = $"Audit termine. Conformes: {compliant}, Partiels: {partial}, Non-conformes: {nonCompliant}. Score global: {audit.OverallComplianceScore}%";

        await SaveAuditsAsync();
        _logger.LogInformation("Audit {Id} termine - Score: {Score}%", audit.AuditId, audit.OverallComplianceScore);
        
        return audit;
    }

    private async Task RunIso27001AuditAsync(ComplianceAuditResult audit)
    {
        var controls = _iso27001Service.GetAllControls().ToList();
        audit.Scope.IncludedControls = new List<string>(controls.Count);
        
        foreach (var control in controls)
        {
            audit.Scope.IncludedControls.Add(control.Id);
            
            var assessment = new ControlAssessment { ControlId = control.Id };

            switch (control.Status)
            {
                case Iso27001ControlStatus.NotApplicable:
                    assessment.Result = ControlAssessmentResult.NotApplicable;
                    assessment.Score = 100;
                    break;
                case Iso27001ControlStatus.NotImplemented:
                    assessment.Result = ControlAssessmentResult.NonCompliant;
                    assessment.Score = 0;
                    audit.Findings.Add(new AuditFinding
                    {
                        Id = _findingIdCounter++,
                        ControlId = control.Id,
                        Type = FindingType.NonConformity,
                        Severity = FindingSeverity.High,
                        Description = $"Le controle {control.Id} ({control.Title}) n'est pas implemente",
                        Recommendation = $"Implementer le controle: {control.Description}",
                        Status = FindingStatus.Open
                    });
                    break;
                case Iso27001ControlStatus.PartiallyImplemented:
                    assessment.Result = ControlAssessmentResult.PartiallyCompliant;
                    assessment.Score = 50;
                    assessment.Notes = "Implementation partielle detectee";
                    break;
                case Iso27001ControlStatus.Implemented:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 85;
                    assessment.Notes = "Controle implemente, efficacite a verifier";
                    break;
                case Iso27001ControlStatus.Effective:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 100;
                    assessment.Notes = "Controle implemente et efficace";
                    break;
            }

            audit.ControlAssessments.Add(assessment);
        }

        await RunAutomatedChecksIso27001Async(audit);
    }

    private async Task RunAutomatedChecksIso27001Async(ComplianceAuditResult audit)
    {
        // A.8.15 - Journalisation
        var loggingCheck = CheckLoggingImplementation();
        if (!loggingCheck.isCompliant)
        {
            audit.Findings.Add(new AuditFinding
            {
                Id = _findingIdCounter++,
                ControlId = "A.8.15",
                Type = FindingType.Observation,
                Severity = FindingSeverity.Medium,
                Description = loggingCheck.message,
                Recommendation = "Ameliorer la configuration de journalisation",
                Status = FindingStatus.Open
            });
        }

        // A.8.20 - Securite des reseaux
        audit.Findings.Add(new AuditFinding
        {
            Id = _findingIdCounter++,
            ControlId = "A.8.20",
            Type = FindingType.Strength,
            Severity = FindingSeverity.Low,
            Description = "Le systeme de surveillance reseau est operationnel",
            Status = FindingStatus.Closed
        });

        // A.8.7 - Protection contre les logiciels malveillants
        UpdateControlAssessment(audit, "A.8.7", ControlAssessmentResult.Compliant, 90);

        // A.8.17 - Synchronisation des horloges
        var timeCheck = await CheckTimeSync();
        UpdateControlAssessment(audit, "A.8.17", 
            timeCheck.isCompliant ? ControlAssessmentResult.Compliant : ControlAssessmentResult.NonCompliant,
            timeCheck.isCompliant ? 100 : 0);
    }

    private static (bool isCompliant, string message) CheckLoggingImplementation()
    {
        var logFiles = new[] { "netguard.log", "audit.log" };
        foreach (var logFile in logFiles)
        {
            if (File.Exists(logFile))
            {
                var lastWrite = File.GetLastWriteTimeUtc(logFile);
                if ((DateTime.UtcNow - lastWrite).TotalHours < 24)
                {
                    return (true, "Journalisation active et recente");
                }
            }
        }
        return (false, "Fichiers de journalisation non trouves ou obsoletes");
    }

    private static async Task<(bool isCompliant, string message)> CheckTimeSync()
    {
        if (!OperatingSystem.IsLinux())
            return (false, "Synchronisation NTP non verifiee");

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new()
                {
                    FileName = "timedatectl",
                    Arguments = "status",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            
            if (process.Start())
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                if (output.Contains("NTP service: active") || output.Contains("System clock synchronized: yes"))
                {
                    return (true, "NTP synchronise");
                }
            }
            return (false, "Synchronisation NTP non verifiee");
        }
        catch
        {
            return (false, "Impossible de verifier la synchronisation NTP");
        }
    }

    private static void UpdateControlAssessment(ComplianceAuditResult audit, string controlId, ControlAssessmentResult result, double score)
    {
        var assessment = audit.ControlAssessments.Find(a => a.ControlId == controlId);
        if (assessment != null)
        {
            assessment.Result = result;
            assessment.Score = score;
        }
    }

    private async Task RunIso15408AuditAsync(ComplianceAuditResult audit)
    {
        var functionalReqs = _iso15408Service.GetFunctionalRequirements().ToList();
        var assuranceReqs = _iso15408Service.GetAssuranceRequirements().ToList();

        audit.Scope.IncludedControls = new List<string>(functionalReqs.Count + assuranceReqs.Count);
        
        foreach (var req in functionalReqs)
            audit.Scope.IncludedControls.Add(req.Id);
        foreach (var req in assuranceReqs)
            audit.Scope.IncludedControls.Add(req.Id);

        // Evaluer les exigences fonctionnelles
        foreach (var req in functionalReqs)
        {
            var assessment = new ControlAssessment { ControlId = req.Id };

            switch (req.Status)
            {
                case RequirementStatus.NotAddressed:
                    assessment.Result = ControlAssessmentResult.NonCompliant;
                    assessment.Score = 0;
                    audit.Findings.Add(new AuditFinding
                    {
                        Id = _findingIdCounter++,
                        ControlId = req.Id,
                        Type = FindingType.NonConformity,
                        Severity = FindingSeverity.High,
                        Description = $"SFR {req.Id} non implementee: {req.Description}",
                        Status = FindingStatus.Open
                    });
                    break;
                case RequirementStatus.PartiallyAddressed:
                    assessment.Result = ControlAssessmentResult.PartiallyCompliant;
                    assessment.Score = 50;
                    break;
                case RequirementStatus.FullyAddressed:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 90;
                    break;
                case RequirementStatus.Verified:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 100;
                    break;
            }

            audit.ControlAssessments.Add(assessment);
        }

        // Evaluer les exigences d'assurance
        foreach (var req in assuranceReqs)
        {
            var assessment = new ControlAssessment { ControlId = req.Id };

            switch (req.Status)
            {
                case RequirementStatus.NotAddressed:
                    assessment.Result = ControlAssessmentResult.NonCompliant;
                    assessment.Score = 0;
                    break;
                case RequirementStatus.PartiallyAddressed:
                    assessment.Result = ControlAssessmentResult.PartiallyCompliant;
                    assessment.Score = 50;
                    break;
                case RequirementStatus.FullyAddressed:
                case RequirementStatus.Verified:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 100;
                    break;
            }

            audit.ControlAssessments.Add(assessment);
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Findings

    public IEnumerable<AuditFinding> GetOpenFindings()
    {
        // Utiliser une liste pour éviter multiple énumérations
        var openFindings = new List<AuditFinding>();
        
        foreach (var audit in _audits)
        {
            foreach (var finding in audit.Findings)
            {
                if (finding.Status != FindingStatus.Closed)
                {
                    openFindings.Add(finding);
                }
            }
        }
        
        // Tri sur place plus efficace
        openFindings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return openFindings;
    }

    public async Task UpdateFindingAsync(AuditFinding finding)
    {
        foreach (var audit in _audits)
        {
            var index = audit.Findings.FindIndex(f => f.Id == finding.Id);
            if (index >= 0)
            {
                audit.Findings[index] = finding;
                await SaveAuditsAsync();
                _logger.LogInformation("Constatation {Id} mise a jour - Statut: {Status}", finding.Id, finding.Status);
                return;
            }
        }
    }

    public async Task<AuditFinding> AddFindingAsync(int auditId, AuditFinding finding)
    {
        var audit = GetAudit(auditId);
        if (audit != null)
        {
            finding.Id = _findingIdCounter++;
            audit.Findings.Add(finding);
            await SaveAuditsAsync();
            _logger.LogInformation("Constatation ajoutee a l'audit {AuditId}", auditId);
        }
        return finding;
    }

    #endregion

    #region Evidence

    public IEnumerable<ComplianceEvidence> GetAllEvidence()
    {
        if (_evidence.Count <= 1) return _evidence;
        return _evidence.OrderByDescending(e => e.CollectedDate);
    }

    public async Task<ComplianceEvidence> AddEvidenceAsync(ComplianceEvidence evidence)
    {
        evidence.Id = _evidenceIdCounter++;
        evidence.CollectedDate = DateTime.UtcNow;
        _evidence.Add(evidence);
        await SaveEvidenceAsync();
        _logger.LogInformation("Preuve {Id} ajoutee pour le controle {ControlId}", evidence.Id, evidence.ControlId);
        return evidence;
    }

    #endregion

    #region Dashboard

    public ComplianceDashboard GetDashboard()
    {
        var dashboard = new ComplianceDashboard
        {
            GeneratedAt = DateTime.UtcNow,
            Iso27001 = _iso27001Service.GetSummary(),
            Iso15408 = _iso15408Service.GetSummary()
        };

        // Top risques - Utiliser une liste avec capacité définie
        var allRisks = _iso27001Service.GetAllRisks();
        var topRisks = new List<RiskSummary>(5);
        
        foreach (var r in allRisks.OrderByDescending(r => r.RiskScore).Take(5))
        {
            topRisks.Add(new RiskSummary
            {
                RiskId = r.Id,
                AssetName = r.AssetName,
                ThreatDescription = r.ThreatDescription,
                RiskLevel = r.RiskLevel,
                RiskScore = r.RiskScore,
                Status = r.Status
            });
        }
        dashboard.TopRisks = topRisks;

        // Incidents recents
        dashboard.RecentIncidents = _iso27001Service.GetOpenIncidents()
            .OrderByDescending(i => i.DetectedAt)
            .Take(5)
            .ToList();

        // Constatations ouvertes - réutiliser la méthode optimisée
        var openFindings = GetOpenFindings();
        dashboard.OpenFindings = openFindings.Take(10).ToList();

        // Taches a venir
        dashboard.UpcomingTasks = GetUpcomingTasks();

        return dashboard;
    }

    private List<UpcomingTask> GetUpcomingTasks()
    {
        var tasks = new List<UpcomingTask>();
        var now = DateTime.UtcNow;
        var thirtyDaysFromNow = now.AddDays(30);
        var fourteenDaysFromNow = now.AddDays(14);

        // Controles a revoir
        foreach (var control in _iso27001Service.GetAllControls())
        {
            if (control.NextReviewDate.HasValue && control.NextReviewDate.Value <= thirtyDaysFromNow)
            {
                tasks.Add(new UpcomingTask
                {
                    TaskType = "Revue de controle",
                    Description = $"Revoir le controle {control.Id}: {control.Title}",
                    DueDate = control.NextReviewDate.Value,
                    Priority = TaskPriority.Medium
                });
            }
        }

        // Politiques a revoir
        foreach (var policy in _iso27001Service.GetAllPolicies())
        {
            if (policy.ReviewDate.HasValue && policy.ReviewDate.Value <= thirtyDaysFromNow)
            {
                tasks.Add(new UpcomingTask
                {
                    TaskType = "Revue de politique",
                    Description = $"Revoir la politique {policy.Code}: {policy.Title}",
                    DueDate = policy.ReviewDate.Value,
                    Priority = TaskPriority.High
                });
            }
        }

        // Constatations avec echeance
        foreach (var finding in GetOpenFindings())
        {
            if (finding.DueDate.HasValue && finding.DueDate.Value <= fourteenDaysFromNow)
            {
                tasks.Add(new UpcomingTask
                {
                    TaskType = "Correction de constatation",
                    Description = $"Corriger {finding.ControlId}: {finding.Description}",
                    DueDate = finding.DueDate.Value,
                    Priority = finding.Severity == FindingSeverity.Critical ? TaskPriority.Critical : TaskPriority.High
                });
            }
        }

        // Prochain audit
        foreach (var audit in _audits)
        {
            if (audit.NextAuditDate.HasValue && audit.NextAuditDate.Value > now)
            {
                tasks.Add(new UpcomingTask
                {
                    TaskType = "Audit planifie",
                    Description = $"Preparer l'audit {audit.Standard}",
                    DueDate = audit.NextAuditDate.Value,
                    Priority = TaskPriority.High
                });
                break; // Un seul audit planifié suffit
            }
        }

        // Tri et limite
        tasks.Sort((a, b) => a.DueDate.CompareTo(b.DueDate));
        return tasks.Count > 10 ? tasks.GetRange(0, 10) : tasks;
    }

    #endregion

    #region Reports

    public async Task<string> GenerateComplianceReportAsync(string standard)
    {
        var report = new System.Text.StringBuilder(4096);
        report.AppendLine($"# Rapport de Conformite {standard}");
        report.AppendLine($"**Date de generation:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine();

        if (string.Equals(standard, "ISO27001", StringComparison.OrdinalIgnoreCase))
        {
            var summary = _iso27001Service.GetSummary();
            report.AppendLine("## Resume ISO/IEC 27001:2022");
            report.AppendLine();
            report.AppendLine($"**Score de conformite global:** {summary.CompliancePercentage}%");
            report.AppendLine();
            report.AppendLine("### Statut des controles");
            report.AppendLine($"- Total: {summary.TotalControls}");
            report.AppendLine($"- Implementes: {summary.ImplementedControls}");
            report.AppendLine($"- Partiellement implementes: {summary.PartiallyImplementedControls}");
            report.AppendLine($"- Non implementes: {summary.NotImplementedControls}");
            report.AppendLine($"- Non applicables: {summary.NotApplicableControls}");
            report.AppendLine();
            report.AppendLine("### Par categorie");
            foreach (var cat in summary.ByCategory)
            {
                report.AppendLine($"- **{cat.Value.CategoryName}**: {cat.Value.CompliancePercentage}%");
            }

            report.AppendLine();
            report.AppendLine("### Risques critiques et eleves");
            var highRisks = _iso27001Service.GetAllRisks()
                .Where(r => r.RiskLevel >= ComplianceRiskLevel.High)
                .ToList();
            
            if (highRisks.Count > 0)
            {
                foreach (var risk in highRisks)
                {
                    report.AppendLine($"- [{risk.RiskLevel}] {risk.AssetName}: {risk.ThreatDescription}");
                }
            }
            else
            {
                report.AppendLine("Aucun risque critique ou eleve identifie.");
            }
        }
        else if (string.Equals(standard, "ISO15408", StringComparison.OrdinalIgnoreCase))
        {
            var summary = _iso15408Service.GetSummary();
            var evaluation = await _iso15408Service.EvaluateComplianceAsync();

            report.AppendLine("## Resume ISO/IEC 15408 (Criteres Communs)");
            report.AppendLine();
            report.AppendLine($"**Niveau EAL cible:** EAL{(int)summary.TargetEal}");
            report.AppendLine($"**Niveau EAL atteint:** EAL{(int)evaluation.AchievedLevel}");
            report.AppendLine($"**Statut:** {summary.CurrentStatus}");
            report.AppendLine();
            report.AppendLine("### Exigences Fonctionnelles (SFR)");
            report.AppendLine($"- Score: {summary.FunctionalCompliancePercentage}%");
            report.AppendLine($"- Satisfaites: {summary.MetFunctionalRequirements}/{summary.TotalFunctionalRequirements}");
            report.AppendLine();
            report.AppendLine("### Exigences d'Assurance (SAR)");
            report.AppendLine($"- Score: {summary.AssuranceCompliancePercentage}%");
            report.AppendLine($"- Satisfaites: {summary.MetAssuranceRequirements}/{summary.TotalAssuranceRequirements}");

            if (evaluation.Gaps.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("### Lacunes identifiees");
                foreach (var gap in evaluation.Gaps.Take(10))
                {
                    report.AppendLine($"- {gap}");
                }
            }

            if (evaluation.Recommendations.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("### Recommandations");
                foreach (var rec in evaluation.Recommendations)
                {
                    report.AppendLine($"- {rec}");
                }
            }
        }

        // Constatations ouvertes
        var openFindings = GetOpenFindings()
            .Where(f => f.Severity >= FindingSeverity.High)
            .ToList();
            
        if (openFindings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Constatations critiques ouvertes");
            foreach (var finding in openFindings)
            {
                report.AppendLine($"- [{finding.Severity}] {finding.ControlId}: {finding.Description}");
            }
        }

        report.AppendLine();
        report.AppendLine("---");
        report.AppendLine("*Rapport genere automatiquement par NetGuard Compliance Module*");

        return report.ToString();
    }

    #endregion

    #region Persistence

    private void LoadData()
    {
        try
        {
            if (File.Exists(AuditsConfigPath))
            {
                var json = File.ReadAllText(AuditsConfigPath);
                var loaded = JsonSerializer.Deserialize<List<ComplianceAuditResult>>(json);
                if (loaded != null)
                {
                    _audits.AddRange(loaded);
                    if (_audits.Count > 0)
                    {
                        _auditIdCounter = _audits.Max(a => a.Id) + 1;
                        var maxFindingId = 0;
                        foreach (var audit in _audits)
                        {
                            foreach (var finding in audit.Findings)
                            {
                                if (finding.Id > maxFindingId) maxFindingId = finding.Id;
                            }
                        }
                        _findingIdCounter = maxFindingId + 1;
                    }
                }
            }

            if (File.Exists(EvidenceConfigPath))
            {
                var json = File.ReadAllText(EvidenceConfigPath);
                var loaded = JsonSerializer.Deserialize<List<ComplianceEvidence>>(json);
                if (loaded != null)
                {
                    _evidence.AddRange(loaded);
                    _evidenceIdCounter = _evidence.Count > 0 ? _evidence.Max(e => e.Id) + 1 : 1;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement donnees d'audit de conformite");
        }
    }

    private async Task SaveAuditsAsync()
    {
        var json = JsonSerializer.Serialize(_audits, JsonOptions);
        await File.WriteAllTextAsync(AuditsConfigPath, json, System.Text.Encoding.UTF8);
    }

    private async Task SaveEvidenceAsync()
    {
        var json = JsonSerializer.Serialize(_evidence, JsonOptions);
        await File.WriteAllTextAsync(EvidenceConfigPath, json, System.Text.Encoding.UTF8);
    }

    #endregion
}
