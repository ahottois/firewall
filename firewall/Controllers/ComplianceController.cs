using Microsoft.AspNetCore.Mvc;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComplianceController : ControllerBase
{
    private readonly ILogger<ComplianceController> _logger;
    private readonly IIso27001Service _iso27001Service;
    private readonly IIso15408Service _iso15408Service;
    private readonly IComplianceAuditService _auditService;

    public ComplianceController(
        ILogger<ComplianceController> logger,
        IIso27001Service iso27001Service,
        IIso15408Service iso15408Service,
        IComplianceAuditService auditService)
    {
        _logger = logger;
        _iso27001Service = iso27001Service;
        _iso15408Service = iso15408Service;
        _auditService = auditService;
    }

    #region Dashboard

    /// <summary>
    /// Obtenir le tableau de bord de conformité
    /// </summary>
    [HttpGet("dashboard")]
    public ActionResult<ComplianceDashboard> GetDashboard()
    {
        return Ok(_auditService.GetDashboard());
    }

    #endregion

    #region ISO 27001

    /// <summary>
    /// Obtenir tous les contrôles ISO 27001
    /// </summary>
    [HttpGet("iso27001/controls")]
    public ActionResult<IEnumerable<Iso27001Control>> GetIso27001Controls([FromQuery] string? category = null)
    {
        var controls = string.IsNullOrEmpty(category)
            ? _iso27001Service.GetAllControls()
            : _iso27001Service.GetControlsByCategory(category);
        return Ok(controls);
    }

    /// <summary>
    /// Obtenir un contrôle ISO 27001 spécifique
    /// </summary>
    [HttpGet("iso27001/controls/{id}")]
    public ActionResult<Iso27001Control> GetIso27001Control(string id)
    {
        var control = _iso27001Service.GetControl(id);
        if (control == null) return NotFound();
        return Ok(control);
    }

    /// <summary>
    /// Mettre à jour le statut d'un contrôle ISO 27001
    /// </summary>
    [HttpPut("iso27001/controls/{id}")]
    public async Task<IActionResult> UpdateIso27001Control(string id, [FromBody] UpdateControlRequest request)
    {
        await _iso27001Service.UpdateControlStatusAsync(id, request.Status, request.Evidence);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Obtenir le résumé ISO 27001
    /// </summary>
    [HttpGet("iso27001/summary")]
    public ActionResult<Iso27001Summary> GetIso27001Summary()
    {
        return Ok(_iso27001Service.GetSummary());
    }

    #endregion

    #region Risks

    /// <summary>
    /// Obtenir tous les risques
    /// </summary>
    [HttpGet("risks")]
    public ActionResult<IEnumerable<RiskAssessment>> GetRisks([FromQuery] ComplianceRiskLevel? level = null)
    {
        var risks = level.HasValue
            ? _iso27001Service.GetRisksByLevel(level.Value)
            : _iso27001Service.GetAllRisks();
        return Ok(risks);
    }

    /// <summary>
    /// Obtenir un risque spécifique
    /// </summary>
    [HttpGet("risks/{id}")]
    public ActionResult<RiskAssessment> GetRisk(int id)
    {
        var risk = _iso27001Service.GetRisk(id);
        if (risk == null) return NotFound();
        return Ok(risk);
    }

    /// <summary>
    /// Ajouter un nouveau risque
    /// </summary>
    [HttpPost("risks")]
    public async Task<ActionResult<RiskAssessment>> AddRisk([FromBody] RiskAssessment risk)
    {
        var created = await _iso27001Service.AddRiskAsync(risk);
        return CreatedAtAction(nameof(GetRisk), new { id = created.Id }, created);
    }

    /// <summary>
    /// Mettre à jour un risque
    /// </summary>
    [HttpPut("risks/{id}")]
    public async Task<IActionResult> UpdateRisk(int id, [FromBody] RiskAssessment risk)
    {
        risk.Id = id;
        await _iso27001Service.UpdateRiskAsync(risk);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Supprimer un risque
    /// </summary>
    [HttpDelete("risks/{id}")]
    public async Task<IActionResult> DeleteRisk(int id)
    {
        await _iso27001Service.DeleteRiskAsync(id);
        return NoContent();
    }

    #endregion

    #region Incidents

    /// <summary>
    /// Obtenir tous les incidents
    /// </summary>
    [HttpGet("incidents")]
    public ActionResult<IEnumerable<SecurityIncident>> GetIncidents([FromQuery] bool openOnly = false)
    {
        var incidents = openOnly
            ? _iso27001Service.GetOpenIncidents()
            : _iso27001Service.GetAllIncidents();
        return Ok(incidents);
    }

    /// <summary>
    /// Obtenir un incident spécifique
    /// </summary>
    [HttpGet("incidents/{id}")]
    public ActionResult<SecurityIncident> GetIncident(int id)
    {
        var incident = _iso27001Service.GetIncident(id);
        if (incident == null) return NotFound();
        return Ok(incident);
    }

    /// <summary>
    /// Créer un nouvel incident
    /// </summary>
    [HttpPost("incidents")]
    public async Task<ActionResult<SecurityIncident>> AddIncident([FromBody] SecurityIncident incident)
    {
        var created = await _iso27001Service.AddIncidentAsync(incident);
        return CreatedAtAction(nameof(GetIncident), new { id = created.Id }, created);
    }

    /// <summary>
    /// Mettre à jour un incident
    /// </summary>
    [HttpPut("incidents/{id}")]
    public async Task<IActionResult> UpdateIncident(int id, [FromBody] SecurityIncident incident)
    {
        incident.Id = id;
        await _iso27001Service.UpdateIncidentAsync(incident);
        return Ok(new { success = true });
    }

    #endregion

    #region Policies

    /// <summary>
    /// Obtenir toutes les politiques
    /// </summary>
    [HttpGet("policies")]
    public ActionResult<IEnumerable<SecurityPolicy>> GetPolicies()
    {
        return Ok(_iso27001Service.GetAllPolicies());
    }

    /// <summary>
    /// Ajouter une nouvelle politique
    /// </summary>
    [HttpPost("policies")]
    public async Task<ActionResult<SecurityPolicy>> AddPolicy([FromBody] SecurityPolicy policy)
    {
        var created = await _iso27001Service.AddPolicyAsync(policy);
        return Ok(created);
    }

    /// <summary>
    /// Mettre à jour une politique
    /// </summary>
    [HttpPut("policies/{id}")]
    public async Task<IActionResult> UpdatePolicy(int id, [FromBody] SecurityPolicy policy)
    {
        policy.Id = id;
        await _iso27001Service.UpdatePolicyAsync(policy);
        return Ok(new { success = true });
    }

    #endregion

    #region ISO 15408

    /// <summary>
    /// Obtenir le profil de protection
    /// </summary>
    [HttpGet("iso15408/protection-profile")]
    public ActionResult<ProtectionProfile> GetProtectionProfile()
    {
        var profile = _iso15408Service.GetProtectionProfile();
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    /// <summary>
    /// Obtenir la cible de sécurité
    /// </summary>
    [HttpGet("iso15408/security-target")]
    public ActionResult<SecurityTarget> GetSecurityTarget()
    {
        var target = _iso15408Service.GetSecurityTarget();
        if (target == null) return NotFound();
        return Ok(target);
    }

    /// <summary>
    /// Obtenir les exigences fonctionnelles (SFR)
    /// </summary>
    [HttpGet("iso15408/functional-requirements")]
    public ActionResult<IEnumerable<SecurityFunctionalRequirement>> GetFunctionalRequirements()
    {
        return Ok(_iso15408Service.GetFunctionalRequirements());
    }

    /// <summary>
    /// Mettre à jour une exigence fonctionnelle
    /// </summary>
    [HttpPut("iso15408/functional-requirements/{id}")]
    public async Task<IActionResult> UpdateFunctionalRequirement(string id, [FromBody] SecurityFunctionalRequirement requirement)
    {
        requirement.Id = id;
        await _iso15408Service.UpdateFunctionalRequirementAsync(requirement);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Obtenir les exigences d'assurance (SAR)
    /// </summary>
    [HttpGet("iso15408/assurance-requirements")]
    public ActionResult<IEnumerable<SecurityAssuranceRequirement>> GetAssuranceRequirements()
    {
        return Ok(_iso15408Service.GetAssuranceRequirements());
    }

    /// <summary>
    /// Mettre à jour une exigence d'assurance
    /// </summary>
    [HttpPut("iso15408/assurance-requirements/{id}")]
    public async Task<IActionResult> UpdateAssuranceRequirement(string id, [FromBody] SecurityAssuranceRequirement requirement)
    {
        requirement.Id = id;
        await _iso15408Service.UpdateAssuranceRequirementAsync(requirement);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Obtenir les objectifs de sécurité
    /// </summary>
    [HttpGet("iso15408/objectives")]
    public ActionResult<IEnumerable<SecurityObjective>> GetSecurityObjectives()
    {
        return Ok(_iso15408Service.GetSecurityObjectives());
    }

    /// <summary>
    /// Obtenir les menaces identifiées
    /// </summary>
    [HttpGet("iso15408/threats")]
    public ActionResult<IEnumerable<ThreatDefinition>> GetThreats()
    {
        return Ok(_iso15408Service.GetThreats());
    }

    /// <summary>
    /// Ajouter une nouvelle menace
    /// </summary>
    [HttpPost("iso15408/threats")]
    public async Task<ActionResult<ThreatDefinition>> AddThreat([FromBody] ThreatDefinition threat)
    {
        var created = await _iso15408Service.AddThreatAsync(threat);
        return Ok(created);
    }

    /// <summary>
    /// Évaluer la conformité ISO 15408
    /// </summary>
    [HttpGet("iso15408/evaluate")]
    public async Task<ActionResult<EvaluationResult>> EvaluateIso15408()
    {
        return Ok(await _iso15408Service.EvaluateComplianceAsync());
    }

    /// <summary>
    /// Obtenir le résumé ISO 15408
    /// </summary>
    [HttpGet("iso15408/summary")]
    public ActionResult<Iso15408Summary> GetIso15408Summary()
    {
        return Ok(_iso15408Service.GetSummary());
    }

    #endregion

    #region Audits

    /// <summary>
    /// Obtenir tous les audits
    /// </summary>
    [HttpGet("audits")]
    public ActionResult<IEnumerable<ComplianceAuditResult>> GetAudits()
    {
        return Ok(_auditService.GetAllAudits());
    }

    /// <summary>
    /// Obtenir un audit spécifique
    /// </summary>
    [HttpGet("audits/{id}")]
    public ActionResult<ComplianceAuditResult> GetAudit(int id)
    {
        var audit = _auditService.GetAudit(id);
        if (audit == null) return NotFound();
        return Ok(audit);
    }

    /// <summary>
    /// Lancer un audit automatisé
    /// </summary>
    [HttpPost("audits/run")]
    public async Task<ActionResult<ComplianceAuditResult>> RunAudit([FromQuery] string standard)
    {
        if (string.IsNullOrEmpty(standard))
        {
            return BadRequest("Le paramètre 'standard' est requis (ISO27001 ou ISO15408)");
        }

        var audit = await _auditService.RunAutomatedAuditAsync(standard);
        return Ok(audit);
    }

    /// <summary>
    /// Obtenir les constatations ouvertes
    /// </summary>
    [HttpGet("findings/open")]
    public ActionResult<IEnumerable<AuditFinding>> GetOpenFindings()
    {
        return Ok(_auditService.GetOpenFindings());
    }

    /// <summary>
    /// Mettre à jour une constatation
    /// </summary>
    [HttpPut("findings/{id}")]
    public async Task<IActionResult> UpdateFinding(int id, [FromBody] AuditFinding finding)
    {
        finding.Id = id;
        await _auditService.UpdateFindingAsync(finding);
        return Ok(new { success = true });
    }

    #endregion

    #region Evidence

    /// <summary>
    /// Obtenir toutes les preuves
    /// </summary>
    [HttpGet("evidence")]
    public ActionResult<IEnumerable<ComplianceEvidence>> GetEvidence()
    {
        return Ok(_auditService.GetAllEvidence());
    }

    /// <summary>
    /// Ajouter une nouvelle preuve
    /// </summary>
    [HttpPost("evidence")]
    public async Task<ActionResult<ComplianceEvidence>> AddEvidence([FromBody] ComplianceEvidence evidence)
    {
        var created = await _auditService.AddEvidenceAsync(evidence);
        return Ok(created);
    }

    #endregion

    #region Reports

    /// <summary>
    /// Générer un rapport de conformité
    /// </summary>
    [HttpGet("reports/{standard}")]
    public async Task<ActionResult<string>> GenerateReport(string standard)
    {
        var report = await _auditService.GenerateComplianceReportAsync(standard);
        return Ok(new { report });
    }

    #endregion
}

public class UpdateControlRequest
{
    public Iso27001ControlStatus Status { get; set; }
    public string? Evidence { get; set; }
}
