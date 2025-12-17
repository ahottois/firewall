using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IAnomalyDetectionService
{
    Task AnalyzePacketAsync(PacketCapturedEventArgs packet);
    void Reset();
}

public class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly ILogger<AnomalyDetectionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly AppSettings _settings;

    // Tracking pour la détection de port scan
    private readonly ConcurrentDictionary<string, PortScanTracker> _portScanTrackers = new();
    
    // Tracking pour la détection ARP spoofing
    private readonly ConcurrentDictionary<string, string> _arpTable = new();
    
    // Tracking du volume de trafic
    private readonly ConcurrentDictionary<string, TrafficVolumeTracker> _trafficTrackers = new();

    public AnomalyDetectionService(
        ILogger<AnomalyDetectionService> logger,
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _settings = settings.Value;
    }

    public async Task AnalyzePacketAsync(PacketCapturedEventArgs packet)
    {
        var tasks = new List<Task>
        {
            CheckPortScanAsync(packet),
            CheckArpSpoofingAsync(packet),
            CheckSuspiciousPortsAsync(packet),
            CheckTrafficVolumeAsync(packet),
            CheckMalformedPacketAsync(packet)
        };

        await Task.WhenAll(tasks);
    }

    private async Task CheckPortScanAsync(PacketCapturedEventArgs packet)
    {
        if (string.IsNullOrEmpty(packet.SourceIp) || !packet.DestinationPort.HasValue)
            return;

        var key = packet.SourceIp;
        var now = DateTime.UtcNow;

        var tracker = _portScanTrackers.GetOrAdd(key, _ => new PortScanTracker());
        
        bool shouldAlert = false;
        int uniquePorts = 0;

        lock (tracker)
        {
            // Nettoyer les anciennes entrees
            var cutoff = now.AddSeconds(-_settings.PortScanTimeWindowSeconds);
            tracker.PortsAccessed.RemoveAll(p => p.Timestamp < cutoff);
            
            // Ajouter le nouveau port
            tracker.PortsAccessed.Add(new PortAccess
            {
                Port = packet.DestinationPort.Value,
                Timestamp = now
            });

            // Verifier si c'est un port scan
            uniquePorts = tracker.PortsAccessed
                .Select(p => p.Port)
                .Distinct()
                .Count();

            if (uniquePorts >= _settings.PortScanThreshold && !tracker.AlertSent)
            {
                shouldAlert = true;
                // We mark it as sent here to avoid multiple threads triggering it, 
                // but we might revert if DB check says it's already active (handled outside lock)
                tracker.AlertSent = true; 
            }
        }

        if (shouldAlert)
        {
            // Check DB for active alert (outside lock)
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            if (await alertRepo.HasActiveAlertAsync(packet.SourceMac, AlertType.PortScan))
            {
                return;
            }

            _ = CreateAlertAsync(new NetworkAlert
            {
                Type = AlertType.PortScan,
                Severity = AlertSeverity.High,
                Title = "Port Scan Detected",
                Message = $"L'adresse {packet.SourceIp} a scanne {uniquePorts} ports differents en {_settings.PortScanTimeWindowSeconds} secondes",
                SourceIp = packet.SourceIp,
                SourceMac = packet.SourceMac
            });
        }
    }

    private async Task CheckArpSpoofingAsync(PacketCapturedEventArgs packet)
    {
        if (packet.Protocol != "ARP" || string.IsNullOrEmpty(packet.SourceIp))
            return;

        var existingMac = _arpTable.GetOrAdd(packet.SourceIp, packet.SourceMac);

        if (existingMac != packet.SourceMac)
        {
            // Check DB for active alert
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            if (await alertRepo.HasActiveAlertAsync(packet.SourceMac, AlertType.ArpSpoofing))
            {
                return;
            }

            _logger.LogWarning("Possible ARP Spoofing: IP {Ip} was {OldMac}, now {NewMac}",
                packet.SourceIp, existingMac, packet.SourceMac);

            await CreateAlertAsync(new NetworkAlert
            {
                Type = AlertType.ArpSpoofing,
                Severity = AlertSeverity.Critical,
                Title = "ARP Spoofing Detected",
                Message = $"L'adresse IP {packet.SourceIp} a change de MAC: {existingMac} -> {packet.SourceMac}. Possible attaque Man-in-the-Middle!",
                SourceIp = packet.SourceIp,
                SourceMac = packet.SourceMac
            });

            // Mettre  jour la table ARP
            _arpTable[packet.SourceIp] = packet.SourceMac;
        }
    }

    private async Task CheckSuspiciousPortsAsync(PacketCapturedEventArgs packet)
    {
        if (!packet.DestinationPort.HasValue)
            return;

        if (_settings.SuspiciousPorts.Contains(packet.DestinationPort.Value))
        {
            // Limiter les alertes pour viter le spam
            var key = $"{packet.SourceMac}-{packet.DestinationPort}";
            var tracker = _trafficTrackers.GetOrAdd(key, _ => new TrafficVolumeTracker());
            
            if ((DateTime.UtcNow - tracker.LastAlertTime).TotalMinutes < 5)
                return;

            tracker.LastAlertTime = DateTime.UtcNow;

            await CreateAlertAsync(new NetworkAlert
            {
                Type = AlertType.SuspiciousTraffic,
                Severity = AlertSeverity.Medium,
                Title = "Suspicious Port Access",
                Message = $"Trafic detecte vers le port suspect {packet.DestinationPort} ({GetPortDescription(packet.DestinationPort.Value)})",
                SourceIp = packet.SourceIp,
                SourceMac = packet.SourceMac,
                DestinationIp = packet.DestinationIp,
                DestinationPort = packet.DestinationPort,
                Protocol = packet.Protocol
            });
        }
    }

    private async Task CheckTrafficVolumeAsync(PacketCapturedEventArgs packet)
    {
        var key = packet.SourceMac;
        var now = DateTime.UtcNow;
        
        var tracker = _trafficTrackers.GetOrAdd(key, _ => new TrafficVolumeTracker());
        
        lock (tracker)
        {
            tracker.BytesInWindow += packet.PacketSize;
            tracker.PacketsInWindow++;
            
            // Vrifier toutes les 10 secondes
            if ((now - tracker.WindowStart).TotalSeconds >= 10)
            {
                var bytesPerSecond = tracker.BytesInWindow / 10.0;
                var packetsPerSecond = tracker.PacketsInWindow / 10.0;
                
                // Seuils d'alerte (ajustables)
                if (bytesPerSecond > 10_000_000 || packetsPerSecond > 1000) // 10 MB/s ou 1000 pps
                {
                    if ((now - tracker.LastAlertTime).TotalMinutes >= 1)
                    {
                        tracker.LastAlertTime = now;
                        _ = CreateAlertAsync(new NetworkAlert
                        {
                            Type = AlertType.HighTrafficVolume,
                            Severity = AlertSeverity.Medium,
                            Title = "High Traffic Volume",
                            Message = $"Trafic eleve detecte: {bytesPerSecond / 1_000_000:F2} MB/s, {packetsPerSecond:F0} paquets/s",
                            SourceMac = packet.SourceMac,
                            SourceIp = packet.SourceIp
                        });
                    }
                }
                
                // Rinitialiser la fentre
                tracker.BytesInWindow = 0;
                tracker.PacketsInWindow = 0;
                tracker.WindowStart = now;
            }
        }
    }

    private async Task CheckMalformedPacketAsync(PacketCapturedEventArgs packet)
    {
        // Vrifications basiques de paquets malforms
        var issues = new List<string>();

        if (packet.SourceMac == "00:00:00:00:00:00")
            issues.Add("Source MAC is all zeros");
        
        if (packet.SourceMac == "FF:FF:FF:FF:FF:FF")
            issues.Add("Source MAC is broadcast");
        
        if (packet.SourceIp == "0.0.0.0" && packet.Protocol != "ARP")
            issues.Add("Invalid source IP");

        if (issues.Any())
        {
            await CreateAlertAsync(new NetworkAlert
            {
                Type = AlertType.MalformedPacket,
                Severity = AlertSeverity.Low,
                Title = "Malformed Packet Detected",
                Message = string.Join(", ", issues),
                SourceMac = packet.SourceMac,
                SourceIp = packet.SourceIp,
                Protocol = packet.Protocol
            });
        }
    }

    private async Task CreateAlertAsync(NetworkAlert alert)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

            // Lier l'alerte à un appareil si possible
            if (!string.IsNullOrEmpty(alert.SourceMac))
            {
                var device = await deviceRepo.GetByMacAddressAsync(alert.SourceMac);
                if (device != null)
                {
                    alert.DeviceId = device.Id;
                }
            }

            await alertRepo.AddAsync(alert);
            
            _logger.LogWarning("[{Severity}] {Type}: {Title}", alert.Severity, alert.Type, alert.Title);
            
            await _notificationService.SendAlertAsync(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create alert");
        }
    }

    private static string GetPortDescription(int port) => port switch
    {
        22 => "SSH",
        23 => "Telnet",
        135 => "RPC",
        139 => "NetBIOS",
        445 => "SMB",
        3389 => "RDP",
        _ => "Unknown"
    };

    public void Reset()
    {
        _portScanTrackers.Clear();
        _arpTable.Clear();
        _trafficTrackers.Clear();
    }

    private class PortScanTracker
    {
        public List<PortAccess> PortsAccessed { get; } = new();
        public bool AlertSent { get; set; }
    }

    private class PortAccess
    {
        public int Port { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class TrafficVolumeTracker
    {
        public long BytesInWindow { get; set; }
        public int PacketsInWindow { get; set; }
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public DateTime LastAlertTime { get; set; } = DateTime.MinValue;
    }
}
