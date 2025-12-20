using System.Linq;
using BenchmarkDotNet.Attributes;
using NetworkFirewall.Services;
using NetworkFirewall.Models;
using Microsoft.Extensions.Logging.Abstractions;

[MemoryDiagnoser]
public class ComplianceServiceBenchmarks
{
    private Iso27001Service _iso27001Service = null!;
    private Iso15408Service _iso15408Service = null!;
    private ComplianceAuditService _auditService = null!;

    [GlobalSetup]
    public void Setup()
    {
        _iso27001Service = new Iso27001Service(NullLogger<Iso27001Service>.Instance);
        _iso15408Service = new Iso15408Service(NullLogger<Iso15408Service>.Instance);
        _auditService = new ComplianceAuditService(
            NullLogger<ComplianceAuditService>.Instance,
            _iso27001Service,
            _iso15408Service);
    }

    [Benchmark]
    public void GetAllControls()
    {
        var controls = _iso27001Service.GetAllControls().ToList();
    }

    [Benchmark]
    public void GetSummary()
    {
        var summary = _iso27001Service.GetSummary();
    }

    [Benchmark]
    public void GetDashboard()
    {
        var dashboard = _auditService.GetDashboard();
    }

    [Benchmark]
    public void GetControlsByCategory()
    {
        var controls = _iso27001Service.GetControlsByCategory("A.8").ToList();
    }

    [Benchmark]
    public void GetOpenFindings()
    {
        var findings = _auditService.GetOpenFindings().ToList();
    }

    [Benchmark]
    public void GetAllAudits()
    {
        var audits = _auditService.GetAllAudits().ToList();
    }
}