namespace NetworkFirewall.Services.Compliance;

/// <summary>
/// Modèles de résultats de conformité
/// </summary>
public class ComplianceCheckResult
{
    public string ControlId { get; set; } = string.Empty;
    public string ControlTitle { get; set; } = string.Empty;
    public ComplianceStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
    public DateTime CheckedAt { get; set; }
    public string? Recommendation { get; set; }
}

public class RealComplianceSummary
{
    public int TotalChecks { get; set; }
    public int Compliant { get; set; }
    public int PartiallyCompliant { get; set; }
    public int NonCompliant { get; set; }
    public int NotVerifiable { get; set; }
    public int Errors { get; set; }
    public double CompliancePercentage => TotalChecks > 0 ? 
        Math.Round((Compliant + PartiallyCompliant * 0.5) / TotalChecks * 100, 2) : 0;
    public List<ComplianceCheckResult> CheckResults { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

public enum ComplianceStatus
{
    Compliant,
    PartiallyCompliant,
    NonCompliant,
    NotVerifiable,
    Error
}
