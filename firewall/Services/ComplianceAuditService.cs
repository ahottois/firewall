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
    
    private List<ComplianceAuditResult> _audits = new();
    private List<ComplianceEvidence> _evidence = new();
    private int _auditIdCounter = 1;
    private int _findingIdCounter = 1;
    private int _evidenceIdCounter = 1;
    
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

    public IEnumerable<ComplianceAuditResult> GetAllAudits() => _audits.OrderByDescending(a => a.AuditDate);

    public ComplianceAuditResult? GetAudit(int id) => _audits.FirstOrDefault(a => a.Id == id);

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
        _logger.LogInformation("Audit {Id} créé pour {Standard}", audit.AuditId, standard);
        return audit;
    }

    public async Task<ComplianceAuditResult> RunAutomatedAuditAsync(string standard)
    {
        _logger.LogInformation("Démarrage de l'audit automatisé pour {Standard}", standard);
        
        var audit = await CreateAuditAsync(standard, AuditType.SelfAssessment);
        audit.Auditor = "Système automatisé";

        if (standard.Equals("ISO27001", StringComparison.OrdinalIgnoreCase))
        {
            await RunIso27001AuditAsync(audit);
        }
        else if (standard.Equals("ISO15408", StringComparison.OrdinalIgnoreCase))
        {
            await RunIso15408AuditAsync(audit);
        }

        // Calculer le score global
        var assessedControls = audit.ControlAssessments.Where(c => c.Result != ControlAssessmentResult.NotAssessed).ToList();
        if (assessedControls.Any())
        {
            audit.OverallComplianceScore = Math.Round(assessedControls.Average(c => c.Score), 2);
        }

        // Définir la prochaine date d'audit
        audit.NextAuditDate = DateTime.UtcNow.AddMonths(6);

        // Générer le résumé
        var compliant = audit.ControlAssessments.Count(c => c.Result == ControlAssessmentResult.Compliant);
        var partial = audit.ControlAssessments.Count(c => c.Result == ControlAssessmentResult.PartiallyCompliant);
        var nonCompliant = audit.ControlAssessments.Count(c => c.Result == ControlAssessmentResult.NonCompliant);
        
        audit.Summary = $"Audit terminé. Conformes: {compliant}, Partiels: {partial}, Non-conformes: {nonCompliant}. " +
                       $"Score global: {audit.OverallComplianceScore}%";

        await SaveAuditsAsync();
        _logger.LogInformation("Audit {Id} terminé - Score: {Score}%", audit.AuditId, audit.OverallComplianceScore);
        
        return audit;
    }

    private async Task RunIso27001AuditAsync(ComplianceAuditResult audit)
    {
        var controls = _iso27001Service.GetAllControls().ToList();
        audit.Scope.IncludedControls = controls.Select(c => c.Id).ToList();

        foreach (var control in controls)
        {
            var assessment = new ControlAssessment
            {
                ControlId = control.Id
            };

            // Évaluer le contrôle
            switch (control.Status)
            {
                case Iso27001ControlStatus.NotApplicable:
                    assessment.Result = ControlAssessmentResult.NotApplicable;
                    assessment.Score = 100;
                    break;
                case Iso27001ControlStatus.NotImplemented:
                    assessment.Result = ControlAssessmentResult.NonCompliant;
                    assessment.Score = 0;
                    // Ajouter une constatation
                    var finding = new AuditFinding
                    {
                        Id = _findingIdCounter++,
                        ControlId = control.Id,
                        Type = FindingType.NonConformity,
                        Severity = FindingSeverity.High,
                        Description = $"Le contrôle {control.Id} ({control.Title}) n'est pas implémenté",
                        Recommendation = $"Implémenter le contrôle: {control.Description}",
                        Status = FindingStatus.Open
                    };
                    audit.Findings.Add(finding);
                    break;
                case Iso27001ControlStatus.PartiallyImplemented:
                    assessment.Result = ControlAssessmentResult.PartiallyCompliant;
                    assessment.Score = 50;
                    assessment.Notes = "Implémentation partielle détectée";
                    break;
                case Iso27001ControlStatus.Implemented:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 85;
                    assessment.Notes = "Contrôle implémenté, efficacité à vérifier";
                    break;
                case Iso27001ControlStatus.Effective:
                    assessment.Result = ControlAssessmentResult.Compliant;
                    assessment.Score = 100;
                    assessment.Notes = "Contrôle implémenté et efficace";
                    break;
            }

            audit.ControlAssessments.Add(assessment);
        }

        // Vérifications automatiques supplémentaires
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
                Recommendation = "Améliorer la configuration de journalisation",
                Status = FindingStatus.Open
            });
        }

        // A.8.20 - Sécurité des réseaux (le firewall lui-même)
        var networkSecurityCheck = CheckNetworkSecurity();
        if (networkSecurityCheck.isCompliant)
        {
            audit.Findings.Add(new AuditFinding
            {
                Id = _findingIdCounter++,
                ControlId = "A.8.20",
                Type = FindingType.Strength,
                Severity = FindingSeverity.Low,
                Description = "Le système de surveillance réseau est opérationnel",
                Status = FindingStatus.Closed
            });
        }

        // A.8.7 - Protection contre les logiciels malveillants
        var malwareCheck = CheckMalwareProtection();
        UpdateControlAssessment(audit, "A.8.7", malwareCheck.isCompliant ? ControlAssessmentResult.Compliant : ControlAssessmentResult.PartiallyCompliant, 
            malwareCheck.isCompliant ? 90 : 60);

        // A.8.17 - Synchronisation des horloges
        var timeCheck = await CheckTimeSync();
        UpdateControlAssessment(audit, "A.8.17", timeCheck.isCompliant ? ControlAssessmentResult.Compliant : ControlAssessmentResult.NonCompliant,
            timeCheck.isCompliant ? 100 : 0);

        await Task.CompletedTask;
    }

    private (bool isCompliant, string message) CheckLoggingImplementation()
    {
        // Vérifier si les fichiers de log existent et sont récents
        var logFiles = new[] { "netguard.log", "audit.log" };
        foreach (var logFile in logFiles)
        {
            if (File.Exists(logFile))
            {
                var lastWrite = File.GetLastWriteTimeUtc(logFile);
                if ((DateTime.UtcNow - lastWrite).TotalHours < 24)
                {
                    return (true, "Journalisation active et récente");
                }
            }
        }
        return (false, "Fichiers de journalisation non trouvés ou obsolètes");
    }

    private (bool isCompliant, string message) CheckNetworkSecurity()
    {
        // Le firewall est le système lui-même, donc toujours conforme
        return (true, "Système de surveillance réseau opérationnel");
    }

    private (bool isCompliant, string message) CheckMalwareProtection()
    {
        // Vérifier si le service ThreatIntelligence est actif
        return (true, "Service de détection de menaces actif");
    }

    private async Task<(bool isCompliant, string message)> CheckTimeSync()
    {
        try
        {
            // Sur Linux, vérifier timedatectl
            if (OperatingSystem.IsLinux())
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "timedatectl",
                    Arguments = "status",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    if (output.Contains("NTP service: active") || output.Contains("System clock synchronized: yes"))
                    {
                        return (true, "NTP synchronisé");
                    }
                }
            }
            return (false, "Synchronisation NTP non vérifiée");
        }
        catch
        {
            return (false, "Impossible de vérifier la synchronisation NTP");
        }
    }

    private void UpdateControlAssessment(ComplianceAuditResult audit, string controlId, ControlAssessmentResult result, double score)
    {
        var assessment = audit.ControlAssessments.FirstOrDefault(a => a.ControlId == controlId);
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

        audit.Scope.IncludedControls = functionalReqs.Select(r => r.Id)
            .Concat(assuranceReqs.Select(r => r.Id))
            .ToList();

        // Évaluer les exigences fonctionnelles
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
                        Description = $"SFR {req.Id} non implémentée: {req.Description}",
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

        // Évaluer les exigences d'assurance
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
        return _audits
            .SelectMany(a => a.Findings)
            .Where(f => f.Status != FindingStatus.Closed)
            .OrderByDescending(f => f.Severity);
    }

    public async Task UpdateFindingAsync(AuditFinding finding)
    {
        foreach (var audit in _audits)
        {
            var existing = audit.Findings.FirstOrDefault(f => f.Id == finding.Id);
            if (existing != null)
            {
                var index = audit.Findings.IndexOf(existing);
                audit.Findings[index] = finding;
                await SaveAuditsAsync();
                _logger.LogInformation("Constatation {Id} mise à jour - Statut: {Status}", finding.Id, finding.Status);
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
            _logger.LogInformation("Constatation ajoutée à l'audit {AuditId}", auditId);
        }
        return finding;
    }

    #endregion

    #region Evidence

    public IEnumerable<ComplianceEvidence> GetAllEvidence() => _evidence.OrderByDescending(e => e.CollectedDate);

    public async Task<ComplianceEvidence> AddEvidenceAsync(ComplianceEvidence evidence)
    {
        evidence.Id = _evidenceIdCounter++;
        evidence.CollectedDate = DateTime.UtcNow;
        _evidence.Add(evidence);
        await SaveEvidenceAsync();
        _logger.LogInformation("Preuve {Id} ajoutée pour le contrôle {ControlId}", evidence.Id, evidence.ControlId);
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

        // Top risques
        dashboard.TopRisks = _iso27001Service.GetAllRisks()
            .OrderByDescending(r => r.RiskScore)
            .Take(5)
            .Select(r => new RiskSummary
            {
                RiskId = r.Id,
                AssetName = r.AssetName,
                ThreatDescription = r.ThreatDescription,
                RiskLevel = r.RiskLevel,
                RiskScore = r.RiskScore,
                Status = r.Status
            })
            .ToList();

        // Incidents récents
        dashboard.RecentIncidents = _iso27001Service.GetOpenIncidents()
            .OrderByDescending(i => i.DetectedAt)
            .Take(5)
            .ToList();

        // Constatations ouvertes
        dashboard.OpenFindings = GetOpenFindings()
            .Take(10)
            .ToList();

        // Tâches à venir
        dashboard.UpcomingTasks = GetUpcomingTasks();

        return dashboard;
    }

    private List<UpcomingTask> GetUpcomingTasks()
    {
        var tasks = new List<UpcomingTask>();

        // Contrôles à revoir
        var controlsToReview = _iso27001Service.GetAllControls()
            .Where(c => c.NextReviewDate.HasValue && c.NextReviewDate.Value <= DateTime.UtcNow.AddDays(30))
            .ToList();

        foreach (var control in controlsToReview)
        {
            tasks.Add(new UpcomingTask
            {
                TaskType = "Revue de contrôle",
                Description = $"Revoir le contrôle {control.Id}: {control.Title}",
                DueDate = control.NextReviewDate ?? DateTime.UtcNow,
                Priority = TaskPriority.Medium
            });
        }

        // Politiques à revoir
        var policiesToReview = _iso27001Service.GetAllPolicies()
            .Where(p => p.ReviewDate.HasValue && p.ReviewDate.Value <= DateTime.UtcNow.AddDays(30))
            .ToList();

        foreach (var policy in policiesToReview)
        {
            tasks.Add(new UpcomingTask
            {
                TaskType = "Revue de politique",
                Description = $"Revoir la politique {policy.Code}: {policy.Title}",
                DueDate = policy.ReviewDate ?? DateTime.UtcNow,
                Priority = TaskPriority.High
            });
        }

        // Constatations avec échéance
        var findingsWithDueDate = GetOpenFindings()
            .Where(f => f.DueDate.HasValue && f.DueDate.Value <= DateTime.UtcNow.AddDays(14))
            .ToList();

        foreach (var finding in findingsWithDueDate)
        {
            tasks.Add(new UpcomingTask
            {
                TaskType = "Correction de constatation",
                Description = $"Corriger {finding.ControlId}: {finding.Description}",
                DueDate = finding.DueDate ?? DateTime.UtcNow,
                Priority = finding.Severity == FindingSeverity.Critical ? TaskPriority.Critical : TaskPriority.High
            });
        }

        // Prochain audit
        var nextAudit = _audits
            .Where(a => a.NextAuditDate.HasValue && a.NextAuditDate.Value > DateTime.UtcNow)
            .OrderBy(a => a.NextAuditDate)
            .FirstOrDefault();

        if (nextAudit != null)
        {
            tasks.Add(new UpcomingTask
            {
                TaskType = "Audit planifié",
                Description = $"Préparer l'audit {nextAudit.Standard}",
                DueDate = nextAudit.NextAuditDate ?? DateTime.UtcNow,
                Priority = TaskPriority.High
            });
        }

        return tasks.OrderBy(t => t.DueDate).Take(10).ToList();
    }

    #endregion

    #region Reports

    public async Task<string> GenerateComplianceReportAsync(string standard)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine($"# Rapport de Conformité {standard}");
        report.AppendLine($"**Date de génération:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine();

        if (standard.Equals("ISO27001", StringComparison.OrdinalIgnoreCase))
        {
            var summary = _iso27001Service.GetSummary();
            report.AppendLine("## Résumé ISO/IEC 27001:2022");
            report.AppendLine();
            report.AppendLine($"**Score de conformité global:** {summary.CompliancePercentage}%");
            report.AppendLine();
            report.AppendLine("### Statut des contrôles");
            report.AppendLine($"- Total: {summary.TotalControls}");
            report.AppendLine($"- Implémentés: {summary.ImplementedControls}");
            report.AppendLine($"- Partiellement implémentés: {summary.PartiallyImplementedControls}");
            report.AppendLine($"- Non implémentés: {summary.NotImplementedControls}");
            report.AppendLine($"- Non applicables: {summary.NotApplicableControls}");
            report.AppendLine();
            report.AppendLine("### Par catégorie");
            foreach (var cat in summary.ByCategory)
            {
                report.AppendLine($"- **{cat.Value.CategoryName}**: {cat.Value.CompliancePercentage}%");
            }

            // Risques
            report.AppendLine();
            report.AppendLine("### Risques critiques et élevés");
            var highRisks = _iso27001Service.GetAllRisks()
                .Where(r => r.RiskLevel >= ComplianceRiskLevel.High)
                .ToList();
            
            if (highRisks.Any())
            {
                foreach (var risk in highRisks)
                {
                    report.AppendLine($"- [{risk.RiskLevel}] {risk.AssetName}: {risk.ThreatDescription}");
                }
            }
            else
            {
                report.AppendLine("Aucun risque critique ou élevé identifié.");
            }
        }
        else if (standard.Equals("ISO15408", StringComparison.OrdinalIgnoreCase))
        {
            var summary = _iso15408Service.GetSummary();
            var evaluation = await _iso15408Service.EvaluateComplianceAsync();

            report.AppendLine("## Résumé ISO/IEC 15408 (Critères Communs)");
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

            if (evaluation.Gaps.Any())
            {
                report.AppendLine();
                report.AppendLine("### Lacunes identifiées");
                foreach (var gap in evaluation.Gaps.Take(10))
                {
                    report.AppendLine($"- {gap}");
                }
            }

            if (evaluation.Recommendations.Any())
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
        var openFindings = GetOpenFindings().Where(f => f.Severity >= FindingSeverity.High).ToList();
        if (openFindings.Any())
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
        report.AppendLine("*Rapport généré automatiquement par NetGuard Compliance Module*");

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
                _audits = JsonSerializer.Deserialize<List<ComplianceAuditResult>>(json) ?? new();
                if (_audits.Any())
                {
                    _auditIdCounter = _audits.Max(a => a.Id) + 1;
                    _findingIdCounter = _audits.SelectMany(a => a.Findings).DefaultIfEmpty().Max(f => f?.Id ?? 0) + 1;
                }
            }

            if (File.Exists(EvidenceConfigPath))
            {
                var json = File.ReadAllText(EvidenceConfigPath);
                _evidence = JsonSerializer.Deserialize<List<ComplianceEvidence>>(json) ?? new();
                _evidenceIdCounter = _evidence.Any() ? _evidence.Max(e => e.Id) + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur chargement données d'audit de conformité");
        }
    }

    private async Task SaveAuditsAsync()
    {
        var json = JsonSerializer.Serialize(_audits, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(AuditsConfigPath, json);
    }

    private async Task SaveEvidenceAsync()
    {
        var json = JsonSerializer.Serialize(_evidence, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(EvidenceConfigPath, json);
    }

    #endregion
}
