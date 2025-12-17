using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IScanSessionService
{
    Task<ScanSession> StartSessionAsync(ScanType type, int totalItems = 0);
    Task UpdateProgressAsync(int sessionId, int itemsScanned);
    Task CompleteSessionAsync(int sessionId, string summary);
    Task FailSessionAsync(int sessionId, string error);
    Task MarkInterruptedSessionsAsync();
}

public class ScanSessionService : IScanSessionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanSessionService> _logger;

    public ScanSessionService(IServiceScopeFactory scopeFactory, ILogger<ScanSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ScanSession> StartSessionAsync(ScanType type, int totalItems = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var session = new ScanSession
        {
            Type = type,
            StartTime = DateTime.UtcNow,
            Status = NetworkFirewall.Models.ScanStatus.Running,
            ItemsTotal = totalItems,
            ItemsScanned = 0
        };

        context.ScanSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    public async Task UpdateProgressAsync(int sessionId, int itemsScanned)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var session = await context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.ItemsScanned = itemsScanned;
            await context.SaveChangesAsync();
        }
    }

    public async Task CompleteSessionAsync(int sessionId, string summary)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var session = await context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = NetworkFirewall.Models.ScanStatus.Completed;
            session.EndTime = DateTime.UtcNow;
            session.ResultSummary = summary;
            await context.SaveChangesAsync();
        }
    }

    public async Task FailSessionAsync(int sessionId, string error)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var session = await context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = NetworkFirewall.Models.ScanStatus.Failed;
            session.EndTime = DateTime.UtcNow;
            session.ResultSummary = error;
            await context.SaveChangesAsync();
        }
    }

    public async Task MarkInterruptedSessionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var interruptedSessions = await context.ScanSessions
            .Where(s => s.Status == NetworkFirewall.Models.ScanStatus.Running)
            .ToListAsync();

        if (interruptedSessions.Any())
        {
            foreach (var session in interruptedSessions)
            {
                session.Status = NetworkFirewall.Models.ScanStatus.Interrupted;
                session.EndTime = DateTime.UtcNow;
                session.ResultSummary = "Scan interrupted by system shutdown or crash";
            }
            await context.SaveChangesAsync();
            _logger.LogInformation("Marked {Count} scan sessions as interrupted", interruptedSessions.Count);
        }
    }
}
