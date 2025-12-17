using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IThreatIntelligenceService
{
    Task<ThreatInfo?> CheckIpReputationAsync(string ipAddress);
    Task<ThreatInfo?> CheckDomainAsync(string domain);
    bool IsKnownMaliciousIp(string ipAddress);
    bool IsKnownMaliciousPort(int port);
    Task UpdateThreatFeedsAsync();
    ThreatStats GetThreatStats();
    IEnumerable<ThreatEvent> GetRecentThreats(int count = 50);
}

public class ThreatIntelligenceService : IThreatIntelligenceService
{
    private readonly ILogger<ThreatIntelligenceService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly AppSettings _settings;

    // Threat databases (in-memory for speed)
    private readonly ConcurrentDictionary<string, ThreatInfo> _maliciousIps = new();
    private readonly ConcurrentDictionary<string, ThreatInfo> _maliciousDomains = new();
    private readonly ConcurrentQueue<ThreatEvent> _recentThreats = new();
    private readonly HashSet<int> _maliciousPorts = new();
    
    // Known malicious IP ranges (simplified - in production use proper CIDR matching)
    private readonly List<string> _maliciousRanges = new();
    
    // Stats
    private long _totalChecks;
    private long _threatsDetected;
    private long _blockedConnections;
    private DateTime _lastFeedUpdate = DateTime.MinValue;

    // Known bad ports (commonly used by malware)
    private static readonly int[] KnownBadPorts = new[]
    {
        4444,   // Metasploit default
        5555,   // Android ADB (often exploited)
        6666, 6667, 6668, 6669, // IRC (botnet C&C)
        31337,  // Back Orifice
        12345, 12346, // NetBus
        20034,  // NetBus Pro
        27374,  // SubSeven
        1080,   // SOCKS proxy (often abused)
        3128,   // Squid proxy (often abused)
        8080,   // HTTP proxy (often abused)
        9001, 9030, 9050, 9051, // Tor
        4145,   // SOCKS proxy
        1433,   // SQL Server (brute force target)
        3306,   // MySQL (brute force target)
        5432,   // PostgreSQL (brute force target)
        27017,  // MongoDB (often unsecured)
        6379,   // Redis (often unsecured)
        11211,  // Memcached (amplification attacks)
    };

    // Known malicious user agents
    private static readonly string[] SuspiciousUserAgents = new[]
    {
        "masscan", "zgrab", "nmap", "nikto", "sqlmap", "dirbuster",
        "gobuster", "wfuzz", "hydra", "medusa", "burp", "zap"
    };

    public ThreatIntelligenceService(
        ILogger<ThreatIntelligenceService> logger,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _settings = settings.Value;

        // Initialize known bad ports
        foreach (var port in KnownBadPorts)
        {
            _maliciousPorts.Add(port);
        }

        // Add suspicious ports from settings
        foreach (var port in _settings.SuspiciousPorts)
        {
            _maliciousPorts.Add(port);
        }

        // Initialize with some known malicious IPs (example - in production use threat feeds)
        InitializeKnownThreats();
    }

    private void InitializeKnownThreats()
    {
        // Known malicious IP ranges (examples - these are commonly blocked)
        var knownBadIps = new[]
        {
            // Shodan scanners
            "71.6.135.131", "71.6.165.200", "71.6.167.142",
            "66.240.192.138", "66.240.236.119",
            // Censys scanners  
            "162.142.125.0/24", "167.94.138.0/24", "167.94.145.0/24",
            // Known botnet C&C (examples)
            "185.220.101.0/24", "185.220.102.0/24"
        };

        foreach (var ip in knownBadIps)
        {
            if (ip.Contains('/'))
            {
                _maliciousRanges.Add(ip.Split('/')[0]);
            }
            else
            {
                _maliciousIps.TryAdd(ip, new ThreatInfo
                {
                    IpAddress = ip,
                    ThreatType = ThreatType.Scanner,
                    ThreatLevel = ThreatLevel.High,
                    Description = "Known scanner/malicious host",
                    Source = "Built-in",
                    LastSeen = DateTime.UtcNow
                });
            }
        }
    }

    public async Task<ThreatInfo?> CheckIpReputationAsync(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return null;

        Interlocked.Increment(ref _totalChecks);

        // Check local cache first
        if (_maliciousIps.TryGetValue(ipAddress, out var cached))
        {
            cached.LastSeen = DateTime.UtcNow;
            RecordThreatEvent(cached);
            return cached;
        }

        // Check if in malicious range
        foreach (var range in _maliciousRanges)
        {
            if (ipAddress.StartsWith(range.Substring(0, range.LastIndexOf('.'))))
            {
                var threat = new ThreatInfo
                {
                    IpAddress = ipAddress,
                    ThreatType = ThreatType.Scanner,
                    ThreatLevel = ThreatLevel.Medium,
                    Description = "IP in known malicious range",
                    Source = "Built-in",
                    LastSeen = DateTime.UtcNow
                };
                _maliciousIps.TryAdd(ipAddress, threat);
                RecordThreatEvent(threat);
                return threat;
            }
        }

        // Check external threat intelligence (AbuseIPDB API - requires API key)
        // This is optional and requires configuration
        if (!string.IsNullOrEmpty(_settings.AbuseIpDbApiKey))
        {
            try
            {
                var externalThreat = await CheckAbuseIpDbAsync(ipAddress);
                if (externalThreat != null && externalThreat.ThreatLevel >= ThreatLevel.Medium)
                {
                    _maliciousIps.TryAdd(ipAddress, externalThreat);
                    RecordThreatEvent(externalThreat);
                    return externalThreat;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking AbuseIPDB for {Ip}", ipAddress);
            }
        }

        return null;
    }

    private async Task<ThreatInfo?> CheckAbuseIpDbAsync(string ipAddress)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Key", _settings.AbuseIpDbApiKey);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var response = await client.GetAsync($"https://api.abuseipdb.com/api/v2/check?ipAddress={ipAddress}");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AbuseIpDbResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result?.Data?.AbuseConfidenceScore > 50)
        {
            return new ThreatInfo
            {
                IpAddress = ipAddress,
                ThreatType = ThreatType.Malicious,
                ThreatLevel = result.Data.AbuseConfidenceScore > 80 ? ThreatLevel.Critical : ThreatLevel.High,
                Description = $"AbuseIPDB score: {result.Data.AbuseConfidenceScore}%, Reports: {result.Data.TotalReports}",
                Source = "AbuseIPDB",
                ConfidenceScore = result.Data.AbuseConfidenceScore,
                LastSeen = DateTime.UtcNow
            };
        }

        return null;
    }

    public async Task<ThreatInfo?> CheckDomainAsync(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return null;

        domain = domain.ToLowerInvariant();

        if (_maliciousDomains.TryGetValue(domain, out var cached))
        {
            cached.LastSeen = DateTime.UtcNow;
            return cached;
        }

        // Check for suspicious TLDs
        var suspiciousTlds = new[] { ".tk", ".ml", ".ga", ".cf", ".gq", ".xyz", ".top", ".work", ".click" };
        if (suspiciousTlds.Any(tld => domain.EndsWith(tld)))
        {
            return new ThreatInfo
            {
                Domain = domain,
                ThreatType = ThreatType.SuspiciousDomain,
                ThreatLevel = ThreatLevel.Low,
                Description = "Suspicious TLD often used for malware",
                Source = "Built-in"
            };
        }

        // Check for IP-like domains (often malicious)
        if (System.Text.RegularExpressions.Regex.IsMatch(domain, @"^\d+\.\d+\.\d+\.\d+$"))
        {
            return await CheckIpReputationAsync(domain);
        }

        return null;
    }

    public bool IsKnownMaliciousIp(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return false;

        if (_maliciousIps.ContainsKey(ipAddress)) return true;

        foreach (var range in _maliciousRanges)
        {
            if (ipAddress.StartsWith(range.Substring(0, Math.Min(range.Length, range.LastIndexOf('.') + 1))))
                return true;
        }

        return false;
    }

    public bool IsKnownMaliciousPort(int port)
    {
        return _maliciousPorts.Contains(port);
    }

    public async Task UpdateThreatFeedsAsync()
    {
        _logger.LogInformation("Updating threat intelligence feeds...");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Fetch from Feodo Tracker (banking trojans C&C)
            await FetchFeodoTrackerAsync(client);

            // Fetch from URLhaus (malware URLs)
            await FetchUrlhausAsync(client);

            _lastFeedUpdate = DateTime.UtcNow;
            _logger.LogInformation("Threat feeds updated. Total malicious IPs: {Count}", _maliciousIps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating threat feeds");
        }
    }

    private async Task FetchFeodoTrackerAsync(HttpClient client)
    {
        try
        {
            var response = await client.GetStringAsync("https://feodotracker.abuse.ch/downloads/ipblocklist.txt");
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

                var ip = line.Trim();
                if (IPAddress.TryParse(ip, out _))
                {
                    _maliciousIps.TryAdd(ip, new ThreatInfo
                    {
                        IpAddress = ip,
                        ThreatType = ThreatType.BotnetCC,
                        ThreatLevel = ThreatLevel.Critical,
                        Description = "Feodo Tracker - Banking Trojan C&C",
                        Source = "FeodoTracker"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error fetching Feodo Tracker feed");
        }
    }

    private async Task FetchUrlhausAsync(HttpClient client)
    {
        try
        {
            var response = await client.GetStringAsync("https://urlhaus.abuse.ch/downloads/text_online/");
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var uri = new Uri(line.Trim());
                    var host = uri.Host;

                    if (IPAddress.TryParse(host, out _))
                    {
                        _maliciousIps.TryAdd(host, new ThreatInfo
                        {
                            IpAddress = host,
                            ThreatType = ThreatType.MalwareHost,
                            ThreatLevel = ThreatLevel.High,
                            Description = "URLhaus - Malware distribution",
                            Source = "URLhaus"
                        });
                    }
                    else
                    {
                        _maliciousDomains.TryAdd(host.ToLowerInvariant(), new ThreatInfo
                        {
                            Domain = host,
                            ThreatType = ThreatType.MalwareHost,
                            ThreatLevel = ThreatLevel.High,
                            Description = "URLhaus - Malware distribution",
                            Source = "URLhaus"
                        });
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error fetching URLhaus feed");
        }
    }

    private void RecordThreatEvent(ThreatInfo threat)
    {
        Interlocked.Increment(ref _threatsDetected);

        var evt = new ThreatEvent
        {
            Timestamp = DateTime.UtcNow,
            ThreatInfo = threat
        };

        _recentThreats.Enqueue(evt);

        // Keep only last 1000 events
        while (_recentThreats.Count > 1000)
        {
            _recentThreats.TryDequeue(out _);
        }

        // Send alert for high-level threats
        if (threat.ThreatLevel >= ThreatLevel.High)
        {
            _ = SendThreatAlertAsync(threat);
        }
    }

    private async Task SendThreatAlertAsync(ThreatInfo threat)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

            var alert = new NetworkAlert
            {
                Type = AlertType.ThreatDetected,
                Severity = threat.ThreatLevel == ThreatLevel.Critical ? AlertSeverity.Critical : AlertSeverity.High,
                Title = $"Threat Detected: {threat.ThreatType}",
                Message = $"Malicious {(threat.IpAddress != null ? "IP" : "Domain")}: {threat.IpAddress ?? threat.Domain}. {threat.Description}",
                SourceIp = threat.IpAddress
            };

            await alertRepo.AddAsync(alert);
            await _notificationService.SendAlertAsync(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending threat alert");
        }
    }

    public ThreatStats GetThreatStats()
    {
        return new ThreatStats
        {
            TotalChecks = Interlocked.Read(ref _totalChecks),
            ThreatsDetected = Interlocked.Read(ref _threatsDetected),
            BlockedConnections = Interlocked.Read(ref _blockedConnections),
            MaliciousIpsInDatabase = _maliciousIps.Count,
            MaliciousDomainsInDatabase = _maliciousDomains.Count,
            LastFeedUpdate = _lastFeedUpdate,
            ThreatsByType = _recentThreats
                .GroupBy(t => t.ThreatInfo.ThreatType)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ThreatsByLevel = _recentThreats
                .GroupBy(t => t.ThreatInfo.ThreatLevel)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }

    public IEnumerable<ThreatEvent> GetRecentThreats(int count = 50)
    {
        return _recentThreats.Reverse().Take(count);
    }
}

// Models
public class ThreatInfo
{
    public string? IpAddress { get; set; }
    public string? Domain { get; set; }
    public ThreatType ThreatType { get; set; }
    public ThreatLevel ThreatLevel { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public class ThreatEvent
{
    public DateTime Timestamp { get; set; }
    public ThreatInfo ThreatInfo { get; set; } = new();
}

public class ThreatStats
{
    public long TotalChecks { get; set; }
    public long ThreatsDetected { get; set; }
    public long BlockedConnections { get; set; }
    public int MaliciousIpsInDatabase { get; set; }
    public int MaliciousDomainsInDatabase { get; set; }
    public DateTime LastFeedUpdate { get; set; }
    public Dictionary<string, int> ThreatsByType { get; set; } = new();
    public Dictionary<string, int> ThreatsByLevel { get; set; } = new();
}

public enum ThreatType
{
    Unknown,
    Scanner,
    BotnetCC,
    MalwareHost,
    Phishing,
    Spam,
    Malicious,
    SuspiciousDomain,
    BruteForce
}

public enum ThreatLevel
{
    None,
    Low,
    Medium,
    High,
    Critical
}

// AbuseIPDB Response
public class AbuseIpDbResponse
{
    public AbuseIpDbData? Data { get; set; }
}

public class AbuseIpDbData
{
    public int AbuseConfidenceScore { get; set; }
    public int TotalReports { get; set; }
}
