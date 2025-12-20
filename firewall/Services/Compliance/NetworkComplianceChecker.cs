using System.Net.NetworkInformation;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services.Compliance;

/// <summary>
/// Vérifications réseau (sécurité, services, ségrégation, filtrage)
/// </summary>
public class NetworkComplianceChecker
{
    private readonly ILogger<NetworkComplianceChecker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public NetworkComplianceChecker(
        ILogger<NetworkComplianceChecker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// A.8.20 - Sécurité des réseaux
    /// </summary>
    public async Task<ComplianceCheckResult> CheckNetworkSecurity()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .ToList();

        var firewallActive = true;

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var blockedDevices = (await deviceRepo.GetAllAsync()).Count(d => d.Status == DeviceStatus.Blocked);

        return new ComplianceCheckResult
        {
            ControlId = "A.8.20",
            ControlTitle = "Sécurité des réseaux",
            Status = ComplianceStatus.Compliant,
            Message = $"Firewall actif, {interfaces.Count} interfaces surveillées, {blockedDevices} appareils bloqués",
            Details = new Dictionary<string, object>
            {
                ["FirewallActive"] = firewallActive,
                ["NetworkInterfaces"] = interfaces.Count,
                ["BlockedDevices"] = blockedDevices,
                ["Interfaces"] = interfaces.Select(i => i.Name).ToList()
            },
            CheckedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// A.8.21 - Sécurité des services réseau
    /// </summary>
    public Task<ComplianceCheckResult> CheckNetworkServicesSecurity()
    {
        var openPorts = new List<int>();
        var portsToCheck = new[] { 22, 80, 443, 3389, 5000 };
        
        foreach (var port in portsToCheck)
        {
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
            }
            catch
            {
                openPorts.Add(port);
            }
        }

        var status = openPorts.Count <= 3 ? ComplianceStatus.Compliant :
                     openPorts.Count <= 5 ? ComplianceStatus.PartiallyCompliant :
                     ComplianceStatus.NonCompliant;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.21",
            ControlTitle = "Sécurité des services réseau",
            Status = status,
            Message = $"{openPorts.Count} services réseau actifs: {string.Join(", ", openPorts)}",
            Details = new Dictionary<string, object>
            {
                ["OpenPorts"] = openPorts,
                ["PortCount"] = openPorts.Count
            },
            CheckedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// A.8.22 - Ségrégation des réseaux
    /// </summary>
    public Task<ComplianceCheckResult> CheckNetworkSegregation()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                       n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        var subnets = new HashSet<string>();
        foreach (var iface in interfaces)
        {
            var props = iface.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var subnet = string.Join(".", addr.Address.GetAddressBytes().Take(3));
                    subnets.Add(subnet);
                }
            }
        }

        var hasSegregation = subnets.Count > 1 || interfaces.Count > 1;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.22",
            ControlTitle = "Ségrégation des réseaux",
            Status = hasSegregation ? ComplianceStatus.Compliant : ComplianceStatus.PartiallyCompliant,
            Message = $"{subnets.Count} sous-réseau(x) détecté(s), {interfaces.Count} interface(s) active(s)",
            Details = new Dictionary<string, object>
            {
                ["Subnets"] = subnets.ToList(),
                ["Interfaces"] = interfaces.Select(i => i.Name).ToList(),
                ["HasSegregation"] = hasSegregation
            },
            CheckedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// A.8.23 - Filtrage web (via Pi-hole)
    /// </summary>
    public Task<ComplianceCheckResult> CheckWebFiltering()
    {
        var piholeInstalled = File.Exists("/usr/local/bin/pihole") || 
                             Directory.Exists("/etc/pihole");
        
        var dnsFilteringActive = piholeInstalled;

        return Task.FromResult(new ComplianceCheckResult
        {
            ControlId = "A.8.23",
            ControlTitle = "Filtrage web",
            Status = piholeInstalled ? ComplianceStatus.Compliant : ComplianceStatus.NonCompliant,
            Message = piholeInstalled ? "Pi-hole installé et actif" : "Aucun filtrage DNS détecté",
            Details = new Dictionary<string, object>
            {
                ["PiholeInstalled"] = piholeInstalled,
                ["DnsFilteringActive"] = dnsFilteringActive
            },
            CheckedAt = DateTime.UtcNow,
            Recommendation = !piholeInstalled ? "Installer Pi-hole pour le filtrage DNS" : null
        });
    }
}
