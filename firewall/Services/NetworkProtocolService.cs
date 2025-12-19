using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INetworkProtocolService
{
    // IP Configuration
    Task<IEnumerable<IpConfiguration>> GetIpConfigurationsAsync();
    Task<IpConfiguration?> GetIpConfigurationAsync(string interfaceName);
    Task UpdateIpConfigurationAsync(string interfaceName, IpConfiguration config);
    Task<IpStatistics> GetIpStatisticsAsync();
    
    // ICMP
    Task<PingResult> PingAsync(PingRequest request);
    Task<PingStatistics> PingMultipleAsync(PingRequest request);
    Task<TracerouteResult> TracerouteAsync(TracerouteRequest request);
    
    // Routing
    Task<IEnumerable<RouteEntry>> GetRoutingTableAsync();
    Task<RouteEntry> AddRouteAsync(RouteEntryDto route);
    Task DeleteRouteAsync(int id);
    Task<RipConfig> GetRipConfigAsync();
    Task UpdateRipConfigAsync(RipConfig config);
    Task<OspfConfig> GetOspfConfigAsync();
    Task UpdateOspfConfigAsync(OspfConfig config);
    Task<IEnumerable<OspfNeighbor>> GetOspfNeighborsAsync();
    Task<BgpConfig> GetBgpConfigAsync();
    Task UpdateBgpConfigAsync(BgpConfig config);
    Task<IEnumerable<BgpNeighbor>> GetBgpNeighborsAsync();
}

public class NetworkProtocolService : INetworkProtocolService
{
    private readonly ILogger<NetworkProtocolService> _logger;
    private readonly List<RouteEntry> _staticRoutes = new();
    private RipConfig _ripConfig = new();
    private OspfConfig _ospfConfig = new();
    private BgpConfig _bgpConfig = new();
    
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const string ConfigPath = "routing_config.json";
    private int _routeIdCounter = 1;

    public NetworkProtocolService(ILogger<NetworkProtocolService> logger)
    {
        _logger = logger;
        LoadConfig();
    }

    #region IP Configuration

    public async Task<IEnumerable<IpConfiguration>> GetIpConfigurationsAsync()
    {
        var configs = new List<IpConfiguration>();
        
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
                
            var props = nic.GetIPProperties();
            var config = new IpConfiguration
            {
                InterfaceName = nic.Name,
                IPv4Enabled = nic.Supports(NetworkInterfaceComponent.IPv4),
                IPv6Enabled = nic.Supports(NetworkInterfaceComponent.IPv6),
                MTU = IsLinux ? await GetMtuAsync(nic.Name) : 1500
            };

            // IPv4
            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                config.IPv4Address = ipv4.Address.ToString();
                config.IPv4SubnetMask = ipv4.IPv4Mask?.ToString();
            }
            
            var gw4 = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
            if (gw4 != null)
                config.IPv4Gateway = gw4.Address.ToString();

            // IPv6
            var ipv6 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && 
                                    !a.Address.IsIPv6LinkLocal);
            if (ipv6 != null)
            {
                config.IPv6Address = ipv6.Address.ToString();
                config.IPv6PrefixLength = ipv6.PrefixLength;
            }
            
            var gw6 = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetworkV6);
            if (gw6 != null)
                config.IPv6Gateway = gw6.Address.ToString();

            // DNS
            config.DnsServers = props.DnsAddresses.Select(d => d.ToString()).ToList();
            
            // DHCP
            if (props.GetIPv4Properties() != null)
                config.IPv4DHCP = props.GetIPv4Properties().IsDhcpEnabled;

            configs.Add(config);
        }

        return configs;
    }

    public async Task<IpConfiguration?> GetIpConfigurationAsync(string interfaceName)
    {
        var configs = await GetIpConfigurationsAsync();
        return configs.FirstOrDefault(c => c.InterfaceName.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateIpConfigurationAsync(string interfaceName, IpConfiguration config)
    {
        if (!IsLinux)
        {
            _logger.LogWarning("IP configuration modification only supported on Linux");
            return;
        }

        try
        {
            // IPv4
            if (config.IPv4Enabled && !config.IPv4DHCP && !string.IsNullOrEmpty(config.IPv4Address))
            {
                // Supprimer les adresses existantes
                await ExecuteCommandAsync("ip", $"addr flush dev {interfaceName}");
                
                // Ajouter la nouvelle adresse
                var prefix = SubnetMaskToCidr(config.IPv4SubnetMask ?? "255.255.255.0");
                await ExecuteCommandAsync("ip", $"addr add {config.IPv4Address}/{prefix} dev {interfaceName}");
                
                // Configurer la passerelle
                if (!string.IsNullOrEmpty(config.IPv4Gateway))
                {
                    await ExecuteCommandAsync("ip", $"route add default via {config.IPv4Gateway} dev {interfaceName}");
                }
            }
            else if (config.IPv4DHCP)
            {
                // Activer DHCP
                await ExecuteCommandAsync("dhclient", $"-r {interfaceName}");
                await ExecuteCommandAsync("dhclient", interfaceName);
            }

            // IPv6
            if (config.IPv6Enabled && !config.IPv6SLAAC && !string.IsNullOrEmpty(config.IPv6Address))
            {
                await ExecuteCommandAsync("ip", $"-6 addr add {config.IPv6Address}/{config.IPv6PrefixLength} dev {interfaceName}");
                
                if (!string.IsNullOrEmpty(config.IPv6Gateway))
                {
                    await ExecuteCommandAsync("ip", $"-6 route add default via {config.IPv6Gateway} dev {interfaceName}");
                }
            }

            // MTU
            if (config.MTU > 0 && config.MTU != 1500)
            {
                await ExecuteCommandAsync("ip", $"link set dev {interfaceName} mtu {config.MTU}");
            }

            _logger.LogInformation("IP configuration updated for {Interface}", interfaceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update IP configuration for {Interface}", interfaceName);
            throw;
        }
    }

    public Task<IpStatistics> GetIpStatisticsAsync()
    {
        var stats = new IpStatistics { LastUpdated = DateTime.UtcNow };

        try
        {
            var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();
            var ipStats = ipGlobalProps.GetIPv4GlobalStatistics();
            
            stats.PacketsReceived = ipStats.ReceivedPackets;
            stats.PacketsSent = ipStats.OutputPacketRequests;
            stats.PacketsDropped = ipStats.ReceivedPacketsDiscarded;
            stats.Errors = ipStats.ReceivedPacketsWithAddressErrors + ipStats.ReceivedPacketsWithHeadersErrors;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get IP statistics");
        }

        return Task.FromResult(stats);
    }

    private async Task<int> GetMtuAsync(string interfaceName)
    {
        try
        {
            var result = await ExecuteCommandAsync("cat", $"/sys/class/net/{interfaceName}/mtu");
            if (int.TryParse(result.Trim(), out var mtu))
                return mtu;
        }
        catch { }
        return 1500;
    }

    private int SubnetMaskToCidr(string subnetMask)
    {
        if (!IPAddress.TryParse(subnetMask, out var ip))
            return 24;
            
        var bytes = ip.GetAddressBytes();
        var bits = 0;
        foreach (var b in bytes)
        {
            for (var i = 7; i >= 0; i--)
            {
                if ((b & (1 << i)) != 0)
                    bits++;
                else
                    return bits;
            }
        }
        return bits;
    }

    #endregion

    #region ICMP

    public async Task<PingResult> PingAsync(PingRequest request)
    {
        var result = new PingResult
        {
            TargetHost = request.Host,
            BufferSize = request.BufferSize,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            using var ping = new Ping();
            var options = new PingOptions(request.TTL, true);
            var buffer = new byte[request.BufferSize];
            new Random().NextBytes(buffer);

            var reply = await ping.SendPingAsync(request.Host, request.Timeout, buffer, options);
            
            result.Success = reply.Status == IPStatus.Success;
            result.ResolvedAddress = reply.Address?.ToString();
            result.RoundtripTime = reply.RoundtripTime;
            result.TTL = reply.Options?.Ttl ?? 0;
            
            if (!result.Success)
            {
                result.ErrorMessage = reply.Status.ToString();
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<PingStatistics> PingMultipleAsync(PingRequest request)
    {
        var stats = new PingStatistics { Host = request.Host };
        var roundtrips = new List<long>();

        for (int i = 0; i < request.Count; i++)
        {
            var result = await PingAsync(request);
            stats.Results.Add(result);
            stats.PacketsSent++;

            if (result.Success)
            {
                stats.PacketsReceived++;
                roundtrips.Add(result.RoundtripTime);
            }
            else
            {
                stats.PacketsLost++;
            }

            if (i < request.Count - 1)
                await Task.Delay(500); // Délai entre les pings
        }

        if (roundtrips.Any())
        {
            stats.MinRoundtrip = roundtrips.Min();
            stats.MaxRoundtrip = roundtrips.Max();
            stats.AvgRoundtrip = (long)roundtrips.Average();
        }

        return stats;
    }

    public async Task<TracerouteResult> TracerouteAsync(TracerouteRequest request)
    {
        var result = new TracerouteResult
        {
            TargetHost = request.Host,
            MaxHops = request.MaxHops,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Résoudre l'adresse
            var addresses = await Dns.GetHostAddressesAsync(request.Host);
            var targetAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            
            if (targetAddress == null)
            {
                return result;
            }
            
            result.ResolvedAddress = targetAddress.ToString();

            using var ping = new Ping();
            var buffer = new byte[32];

            for (int ttl = 1; ttl <= request.MaxHops; ttl++)
            {
                var hop = new TracerouteHop { HopNumber = ttl };
                var times = new List<long?>();

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var options = new PingOptions(ttl, true);
                        var reply = await ping.SendPingAsync(targetAddress, request.Timeout, buffer, options);

                        if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired)
                        {
                            hop.Address = reply.Address?.ToString();
                            times.Add(reply.RoundtripTime);
                            
                            // Tenter de résoudre le hostname
                            if (reply.Address != null && hop.Hostname == null)
                            {
                                try
                                {
                                    var hostEntry = await Dns.GetHostEntryAsync(reply.Address);
                                    hop.Hostname = hostEntry.HostName;
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            times.Add(null);
                        }
                    }
                    catch
                    {
                        times.Add(null);
                    }
                }

                hop.RoundtripTime1 = times.Count > 0 ? times[0] : null;
                hop.RoundtripTime2 = times.Count > 1 ? times[1] : null;
                hop.RoundtripTime3 = times.Count > 2 ? times[2] : null;
                hop.TimedOut = times.All(t => t == null);

                result.Hops.Add(hop);

                // Vérifier si on a atteint la destination
                if (hop.Address == result.ResolvedAddress)
                {
                    result.Completed = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Traceroute failed for {Host}", request.Host);
        }

        return result;
    }

    #endregion

    #region Routing

    public async Task<IEnumerable<RouteEntry>> GetRoutingTableAsync()
    {
        var routes = new List<RouteEntry>();

        if (IsLinux)
        {
            try
            {
                var output = await ExecuteCommandAsync("ip", "route show");
                routes.AddRange(ParseLinuxRoutes(output));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get routing table");
            }
        }
        else
        {
            // Windows - utiliser netstat ou PowerShell
            try
            {
                var output = await ExecuteCommandAsync("route", "print -4");
                routes.AddRange(ParseWindowsRoutes(output));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get Windows routing table");
            }
        }

        // Ajouter les routes statiques configurées
        routes.AddRange(_staticRoutes.Where(r => r.IsActive));

        return routes.OrderBy(r => r.Metric).ThenBy(r => r.PrefixLength);
    }

    private IEnumerable<RouteEntry> ParseLinuxRoutes(string output)
    {
        var routes = new List<RouteEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            try
            {
                var route = new RouteEntry { Protocol = RoutingProtocol.Static };
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts[0] == "default")
                {
                    route.Destination = "0.0.0.0";
                    route.SubnetMask = "0.0.0.0";
                    route.PrefixLength = 0;
                    route.IsDefault = true;
                }
                else if (parts[0].Contains('/'))
                {
                    var cidr = parts[0].Split('/');
                    route.Destination = cidr[0];
                    route.PrefixLength = int.Parse(cidr[1]);
                    route.SubnetMask = CidrToSubnetMask(route.PrefixLength);
                }
                else
                {
                    route.Destination = parts[0];
                    route.PrefixLength = 32;
                    route.SubnetMask = "255.255.255.255";
                }

                for (int i = 1; i < parts.Length - 1; i++)
                {
                    switch (parts[i])
                    {
                        case "via":
                            route.Gateway = parts[i + 1];
                            break;
                        case "dev":
                            route.Interface = parts[i + 1];
                            break;
                        case "metric":
                            if (int.TryParse(parts[i + 1], out var metric))
                                route.Metric = metric;
                            break;
                        case "proto":
                            route.Protocol = parts[i + 1] switch
                            {
                                "kernel" => RoutingProtocol.Static,
                                "static" => RoutingProtocol.Static,
                                "rip" => RoutingProtocol.RIP,
                                "ospf" => RoutingProtocol.OSPF,
                                "bgp" => RoutingProtocol.BGP,
                                _ => RoutingProtocol.Static
                            };
                            break;
                    }
                }

                routes.Add(route);
            }
            catch { }
        }

        return routes;
    }

    private IEnumerable<RouteEntry> ParseWindowsRoutes(string output)
    {
        var routes = new List<RouteEntry>();
        // Implémentation simplifiée pour Windows
        return routes;
    }

    private string CidrToSubnetMask(int cidr)
    {
        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    public async Task<RouteEntry> AddRouteAsync(RouteEntryDto dto)
    {
        var route = new RouteEntry
        {
            Id = _routeIdCounter++,
            Destination = dto.Destination,
            PrefixLength = dto.PrefixLength,
            SubnetMask = CidrToSubnetMask(dto.PrefixLength),
            Gateway = dto.Gateway,
            Interface = dto.Interface,
            Metric = dto.Metric,
            Protocol = RoutingProtocol.Static,
            IsActive = true
        };

        if (IsLinux)
        {
            await ExecuteCommandAsync("ip", 
                $"route add {route.Destination}/{route.PrefixLength} via {route.Gateway} dev {route.Interface} metric {route.Metric}");
        }

        _staticRoutes.Add(route);
        SaveConfig();

        _logger.LogInformation("Route added: {Dest}/{Prefix} via {Gateway}", 
            route.Destination, route.PrefixLength, route.Gateway);

        return route;
    }

    public async Task DeleteRouteAsync(int id)
    {
        var route = _staticRoutes.FirstOrDefault(r => r.Id == id);
        if (route == null)
            throw new KeyNotFoundException($"Route {id} not found");

        if (IsLinux)
        {
            await ExecuteCommandAsync("ip", 
                $"route del {route.Destination}/{route.PrefixLength}");
        }

        _staticRoutes.Remove(route);
        SaveConfig();

        _logger.LogInformation("Route deleted: {Dest}/{Prefix}", route.Destination, route.PrefixLength);
    }

    #endregion

    #region RIP

    public Task<RipConfig> GetRipConfigAsync() => Task.FromResult(_ripConfig);

    public async Task UpdateRipConfigAsync(RipConfig config)
    {
        _ripConfig = config;
        SaveConfig();

        if (IsLinux && config.Enabled)
        {
            // Générer la config FRR/Quagga
            await GenerateRipConfigAsync(config);
        }

        _logger.LogInformation("RIP configuration updated (Enabled: {Enabled})", config.Enabled);
    }

    private async Task GenerateRipConfigAsync(RipConfig config)
    {
        var ripConf = $@"!
router rip
 version {config.Version}
 timers basic {config.UpdateInterval} {config.TimeoutInterval} {config.GarbageCollectionInterval}
{string.Join("\n", config.Networks.Select(n => $" network {n}"))}
{string.Join("\n", config.PassiveInterfaces.Select(i => $" passive-interface {i}"))}
{(config.SplitHorizon ? "" : " no split-horizon")}
!
";

        try
        {
            await File.WriteAllTextAsync("/etc/frr/ripd.conf", ripConf);
            await ExecuteCommandAsync("systemctl", "reload frr");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply RIP configuration");
        }
    }

    #endregion

    #region OSPF

    public Task<OspfConfig> GetOspfConfigAsync() => Task.FromResult(_ospfConfig);

    public async Task UpdateOspfConfigAsync(OspfConfig config)
    {
        _ospfConfig = config;
        SaveConfig();

        if (IsLinux && config.Enabled)
        {
            await GenerateOspfConfigAsync(config);
        }

        _logger.LogInformation("OSPF configuration updated (Enabled: {Enabled})", config.Enabled);
    }

    private async Task GenerateOspfConfigAsync(OspfConfig config)
    {
        var ospfConf = $@"!
router ospf
 ospf router-id {config.RouterId}
{string.Join("\n", config.Networks.Select(n => $" network {n.Network} {n.Wildcard} area {n.AreaId}"))}
{(config.AutoCost ? $" auto-cost reference-bandwidth {config.ReferenceBandwidth}" : "")}
!
";

        try
        {
            await File.WriteAllTextAsync("/etc/frr/ospfd.conf", ospfConf);
            await ExecuteCommandAsync("systemctl", "reload frr");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply OSPF configuration");
        }
    }

    public async Task<IEnumerable<OspfNeighbor>> GetOspfNeighborsAsync()
    {
        var neighbors = new List<OspfNeighbor>();

        if (IsLinux && _ospfConfig.Enabled)
        {
            try
            {
                var output = await ExecuteCommandAsync("vtysh", "-c 'show ip ospf neighbor'");
                // Parser la sortie (simplifiée ici)
            }
            catch { }
        }

        return neighbors;
    }

    #endregion

    #region BGP

    public Task<BgpConfig> GetBgpConfigAsync() => Task.FromResult(_bgpConfig);

    public async Task UpdateBgpConfigAsync(BgpConfig config)
    {
        _bgpConfig = config;
        SaveConfig();

        if (IsLinux && config.Enabled)
        {
            await GenerateBgpConfigAsync(config);
        }

        _logger.LogInformation("BGP configuration updated (Enabled: {Enabled}, AS: {AS})", 
            config.Enabled, config.LocalAS);
    }

    private async Task GenerateBgpConfigAsync(BgpConfig config)
    {
        var bgpConf = $@"!
router bgp {config.LocalAS}
 bgp router-id {config.RouterId}
{string.Join("\n", config.Neighbors.Where(n => n.Enabled).Select(n => $@" neighbor {n.Address} remote-as {n.RemoteAS}
{(string.IsNullOrEmpty(n.Description) ? "" : $" neighbor {n.Address} description {n.Description}")}"))}
{string.Join("\n", config.Networks.Select(n => $" network {n.Prefix}/{n.PrefixLength}"))}
!
";

        try
        {
            await File.WriteAllTextAsync("/etc/frr/bgpd.conf", bgpConf);
            await ExecuteCommandAsync("systemctl", "reload frr");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply BGP configuration");
        }
    }

    public async Task<IEnumerable<BgpNeighbor>> GetBgpNeighborsAsync()
    {
        // Retourner les voisins configurés avec leur état
        foreach (var neighbor in _bgpConfig.Neighbors)
        {
            // En production, interroger FRR pour l'état réel
            neighbor.State = BgpNeighborState.Idle;
        }
        
        return await Task.FromResult(_bgpConfig.Neighbors.AsEnumerable());
    }

    #endregion

    #region Configuration Persistence

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var data = JsonSerializer.Deserialize<RoutingConfigData>(json);
                if (data != null)
                {
                    _staticRoutes.AddRange(data.StaticRoutes ?? new List<RouteEntry>());
                    _ripConfig = data.RipConfig ?? new RipConfig();
                    _ospfConfig = data.OspfConfig ?? new OspfConfig();
                    _bgpConfig = data.BgpConfig ?? new BgpConfig();
                    _routeIdCounter = _staticRoutes.Any() ? _staticRoutes.Max(r => r.Id) + 1 : 1;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load routing configuration");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var data = new RoutingConfigData
            {
                StaticRoutes = _staticRoutes,
                RipConfig = _ripConfig,
                OspfConfig = _ospfConfig,
                BgpConfig = _bgpConfig
            };
            
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save routing configuration");
        }
    }

    private class RoutingConfigData
    {
        public List<RouteEntry>? StaticRoutes { get; set; }
        public RipConfig? RipConfig { get; set; }
        public OspfConfig? OspfConfig { get; set; }
        public BgpConfig? BgpConfig { get; set; }
    }

    #endregion

    #region Helpers

    private async Task<string> ExecuteCommandAsync(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Command execution failed: {Cmd} {Args}", command, arguments);
            return string.Empty;
        }
    }

    #endregion
}
