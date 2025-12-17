using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IDnsBlocklistService
{
    Task RefreshListsAsync();
    bool IsBlocked(string domain, out string category);
    int TotalBlockedDomains { get; }
    Dictionary<string, int> GetStats();
}

public class DnsBlocklistService : IDnsBlocklistService
{
    private readonly ILogger<DnsBlocklistService> _logger;
    private readonly AppSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    
    // Domain -> Category
    private Dictionary<string, string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public int TotalBlockedDomains => _blockedDomains.Count;

    public DnsBlocklistService(
        ILogger<DnsBlocklistService> logger,
        IOptions<AppSettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
    }

    public bool IsBlocked(string domain, out string category)
    {
        // Remove trailing dot if present
        if (domain.EndsWith(".")) domain = domain[..^1];

        lock (_lock)
        {
            if (_blockedDomains.TryGetValue(domain, out var cat))
            {
                category = cat;
                return true;
            }
        }

        category = string.Empty;
        return false;
    }

    public async Task RefreshListsAsync()
    {
        _logger.LogInformation("Refreshing DNS blocklists...");
        var newBlockedDomains = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var client = _httpClientFactory.CreateClient();

        foreach (var source in _settings.Dns.Blocklists.Where(b => b.Enabled))
        {
            try
            {
                _logger.LogInformation("Downloading blocklist: {Name}", source.Name);
                var content = await client.GetStringAsync(source.Url);
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                int count = 0;
                foreach (var line in lines)
                {
                    var cleanLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith("#")) continue;

                    // Parse hosts file format: 0.0.0.0 domain.com
                    var parts = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var domain = parts[1].Trim();
                        if (!newBlockedDomains.ContainsKey(domain))
                        {
                            newBlockedDomains[domain] = source.Category;
                            count++;
                        }
                    }
                }
                _logger.LogInformation("Loaded {Count} domains from {Name}", count, source.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download blocklist: {Name}", source.Name);
            }
        }

        lock (_lock)
        {
            _blockedDomains = newBlockedDomains;
        }

        _logger.LogInformation("Total blocked domains: {Count}", _blockedDomains.Count);
    }

    public Dictionary<string, int> GetStats()
    {
        lock (_lock)
        {
            return _blockedDomains.GroupBy(x => x.Value)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
