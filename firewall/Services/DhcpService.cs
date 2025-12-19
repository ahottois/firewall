using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IDhcpService
{
    DhcpConfig GetConfig();
    void UpdateConfig(DhcpConfig config);
    IEnumerable<DhcpLease> GetLeases();
    bool ReleaseLease(string macAddress);
    bool AddStaticReservation(DhcpStaticReservation reservation);
    bool RemoveStaticReservation(string macAddress);
    DhcpServerStatus GetStatus();
}

public class DhcpServerStatus
{
    public bool IsRunning { get; set; }
    public bool IsEnabled { get; set; }
    public string? ListeningInterface { get; set; }
    public string? ServerIp { get; set; }
    public int ActiveLeases { get; set; }
    public int AvailableIps { get; set; }
    public int TotalIps { get; set; }
    public DateTime? LastPacketReceived { get; set; }
    public string? LastError { get; set; }
}

public class DhcpService : BackgroundService, IDhcpService
{
    private readonly ILogger<DhcpService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private DhcpConfig _config = new();
    private readonly ConcurrentDictionary<string, DhcpLease> _leases = new();
    private readonly ConcurrentDictionary<string, DhcpLease> _pendingOffers = new(); // IP -> Lease en attente
    private UdpClient? _udpServer;
    private uint _serverIp;
    private bool _isRunning;
    private DateTime? _lastPacketReceived;
    private string? _lastError;
    
    private const int DhcpServerPort = 67;
    private const int DhcpClientPort = 68;
    private const string ConfigPath = "dhcp_config.json";
    private const string LeasesPath = "dhcp_leases.json";
    private static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(60);

    public DhcpService(ILogger<DhcpService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        LoadData();
    }

    public DhcpConfig GetConfig() => _config;

    public void UpdateConfig(DhcpConfig config)
    {
        _config = config;
        SaveConfig();
        
        // Si désactivé, arrêter le serveur
        if (!_config.Enabled)
        {
            StopServer();
        }
    }

    public IEnumerable<DhcpLease> GetLeases()
    {
        // Retourner les baux actifs et non expirés
        return _leases.Values
            .Where(l => l.Expiration > DateTime.UtcNow)
            .OrderBy(l => l.IpAddress);
    }

    public bool ReleaseLease(string macAddress)
    {
        var normalizedMac = NormalizeMac(macAddress);
        if (_leases.TryRemove(normalizedMac, out var lease))
        {
            _logger.LogInformation("DHCP: Bail libéré manuellement pour {Mac} ({Ip})", normalizedMac, lease.IpAddress);
            SaveLeases();
            return true;
        }
        return false;
    }

    public bool AddStaticReservation(DhcpStaticReservation reservation)
    {
        reservation.MacAddress = NormalizeMac(reservation.MacAddress);
        
        // Vérifier que l'IP est dans la plage
        var ip = DhcpPacket.StringToIp(reservation.IpAddress);
        var rangeStart = DhcpPacket.StringToIp(_config.RangeStart);
        var rangeEnd = DhcpPacket.StringToIp(_config.RangeEnd);
        
        if (ip < rangeStart || ip > rangeEnd)
        {
            _logger.LogWarning("DHCP: Réservation refusée - IP {Ip} hors de la plage", reservation.IpAddress);
            return false;
        }
        
        // Supprimer si existe déjà
        _config.StaticReservations.RemoveAll(r => 
            NormalizeMac(r.MacAddress) == reservation.MacAddress ||
            r.IpAddress == reservation.IpAddress);
        
        _config.StaticReservations.Add(reservation);
        SaveConfig();
        
        _logger.LogInformation("DHCP: Réservation statique ajoutée {Mac} -> {Ip}", 
            reservation.MacAddress, reservation.IpAddress);
        return true;
    }

    public bool RemoveStaticReservation(string macAddress)
    {
        var normalizedMac = NormalizeMac(macAddress);
        var removed = _config.StaticReservations.RemoveAll(r => NormalizeMac(r.MacAddress) == normalizedMac);
        
        if (removed > 0)
        {
            SaveConfig();
            _logger.LogInformation("DHCP: Réservation statique supprimée pour {Mac}", normalizedMac);
            return true;
        }
        return false;
    }

    public DhcpServerStatus GetStatus()
    {
        var rangeStart = DhcpPacket.StringToIp(_config.RangeStart);
        var rangeEnd = DhcpPacket.StringToIp(_config.RangeEnd);
        var totalIps = (int)(rangeEnd - rangeStart + 1);
        var usedIps = _leases.Values.Count(l => l.Expiration > DateTime.UtcNow);
        
        return new DhcpServerStatus
        {
            IsRunning = _isRunning,
            IsEnabled = _config.Enabled,
            ListeningInterface = _config.NetworkInterface,
            ServerIp = _config.ServerIdentifier,
            ActiveLeases = usedIps,
            AvailableIps = totalIps - usedIps - _config.StaticReservations.Count,
            TotalIps = totalIps,
            LastPacketReceived = _lastPacketReceived,
            LastError = _lastError
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DHCP Service démarré");
        
        // Nettoyer les baux expirés périodiquement
        _ = CleanupExpiredLeasesAsync(stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.Enabled)
            {
                try
                {
                    if (!_isRunning)
                    {
                        await StartServerAsync();
                    }
                    
                    if (_udpServer != null)
                    {
                        var result = await _udpServer.ReceiveAsync(stoppingToken);
                        _lastPacketReceived = DateTime.UtcNow;
                        
                        await ProcessPacketAsync(result.Buffer, result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    _lastError = "Port 67 déjà utilisé (un autre serveur DHCP est actif?)";
                    _logger.LogError("DHCP: {Error}", _lastError);
                    _isRunning = false;
                    await Task.Delay(30000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _logger.LogError(ex, "DHCP: Erreur");
                    await Task.Delay(5000, stoppingToken);
                }
            }
            else
            {
                if (_isRunning)
                {
                    StopServer();
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
        
        StopServer();
        _logger.LogInformation("DHCP Service arrêté");
    }

    private Task StartServerAsync()
    {
        try
        {
            // Trouver l'IP du serveur
            _serverIp = GetServerIp();
            if (_serverIp == 0)
            {
                _lastError = "Impossible de déterminer l'IP du serveur";
                _logger.LogError("DHCP: {Error}", _lastError);
                return Task.CompletedTask;
            }
            
            _config.ServerIdentifier = DhcpPacket.IpToString(_serverIp);
            
            // Créer le socket UDP
            _udpServer = new UdpClient();
            _udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, DhcpServerPort));
            _udpServer.EnableBroadcast = true;
            
            _isRunning = true;
            _lastError = null;
            
            _logger.LogInformation("DHCP: Serveur démarré sur {Ip}:{Port}, plage {Start}-{End}",
                _config.ServerIdentifier, DhcpServerPort, _config.RangeStart, _config.RangeEnd);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(ex, "DHCP: Erreur démarrage serveur");
            _isRunning = false;
        }
        
        return Task.CompletedTask;
    }

    private void StopServer()
    {
        try
        {
            _udpServer?.Close();
            _udpServer?.Dispose();
        }
        catch { }
        
        _udpServer = null;
        _isRunning = false;
        _logger.LogInformation("DHCP: Serveur arrêté");
    }

    private uint GetServerIp()
    {
        // Si une IP est configurée, l'utiliser
        if (!string.IsNullOrEmpty(_config.ServerIdentifier))
        {
            try
            {
                return DhcpPacket.StringToIp(_config.ServerIdentifier);
            }
            catch { }
        }
        
        // Sinon, trouver l'IP de l'interface configurée ou la première interface active
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            
            // Si une interface est configurée, ne chercher que celle-là
            if (!string.IsNullOrEmpty(_config.NetworkInterface) &&
                !ni.Name.Equals(_config.NetworkInterface, StringComparison.OrdinalIgnoreCase))
                continue;
            
            // Ignorer les interfaces Docker
            if (ni.Name.StartsWith("docker", StringComparison.OrdinalIgnoreCase) ||
                ni.Name.StartsWith("br-", StringComparison.OrdinalIgnoreCase) ||
                ni.Name.StartsWith("veth", StringComparison.OrdinalIgnoreCase))
                continue;
            
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ip = addr.Address.ToString();
                    // Vérifier que l'IP est dans le même sous-réseau que la plage DHCP
                    var serverIp = DhcpPacket.StringToIp(ip);
                    var rangeStart = DhcpPacket.StringToIp(_config.RangeStart);
                    var mask = DhcpPacket.StringToIp(_config.SubnetMask);
                    
                    if ((serverIp & mask) == (rangeStart & mask))
                    {
                        _config.NetworkInterface = ni.Name;
                        return serverIp;
                    }
                }
            }
        }
        
        return 0;
    }

    private async Task ProcessPacketAsync(byte[] data, IPEndPoint remoteEndPoint)
    {
        var packet = DhcpPacketHandler.Parse(data);
        if (packet == null)
        {
            _logger.LogDebug("DHCP: Paquet invalide reçu de {Remote}", remoteEndPoint);
            return;
        }
        
        var messageType = packet.GetMessageType();
        if (messageType == null)
        {
            _logger.LogDebug("DHCP: Paquet sans type de message de {Remote}", remoteEndPoint);
            return;
        }
        
        var clientMac = packet.GetClientMac();
        _logger.LogInformation("DHCP: {Type} reçu de {Mac}", messageType, clientMac);
        
        switch (messageType)
        {
            case DhcpMessageType.Discover:
                await HandleDiscoverAsync(packet, clientMac);
                break;
                
            case DhcpMessageType.Request:
                await HandleRequestAsync(packet, clientMac);
                break;
                
            case DhcpMessageType.Release:
                HandleRelease(packet, clientMac);
                break;
                
            case DhcpMessageType.Decline:
                HandleDecline(packet, clientMac);
                break;
                
            case DhcpMessageType.Inform:
                await HandleInformAsync(packet, clientMac);
                break;
        }
    }

    private async Task HandleDiscoverAsync(DhcpPacket request, string clientMac)
    {
        // 1. Chercher une réservation statique
        var reservation = _config.StaticReservations
            .FirstOrDefault(r => NormalizeMac(r.MacAddress) == clientMac);
        
        uint offeredIp;
        
        if (reservation != null)
        {
            offeredIp = DhcpPacket.StringToIp(reservation.IpAddress);
            _logger.LogInformation("DHCP: Réservation statique trouvée pour {Mac}: {Ip}", clientMac, reservation.IpAddress);
        }
        else
        {
            // 2. Chercher un bail existant pour ce client
            if (_leases.TryGetValue(clientMac, out var existingLease))
            {
                offeredIp = DhcpPacket.StringToIp(existingLease.IpAddress);
                _logger.LogInformation("DHCP: Bail existant trouvé pour {Mac}: {Ip}", clientMac, existingLease.IpAddress);
            }
            else
            {
                // 3. Chercher l'IP demandée si disponible
                var requestedIp = request.GetRequestedIp();
                if (requestedIp.HasValue && IsIpAvailable(requestedIp.Value, clientMac))
                {
                    offeredIp = requestedIp.Value;
                    _logger.LogInformation("DHCP: IP demandée {Ip} disponible pour {Mac}", 
                        DhcpPacket.IpToString(offeredIp), clientMac);
                }
                else
                {
                    // 4. Allouer une nouvelle IP
                    offeredIp = AllocateNewIp(clientMac);
                    if (offeredIp == 0)
                    {
                        _logger.LogWarning("DHCP: Plus d'IP disponibles pour {Mac}", clientMac);
                        return; // Pas d'IP disponible, ne pas répondre
                    }
                    _logger.LogInformation("DHCP: Nouvelle IP allouée pour {Mac}: {Ip}", 
                        clientMac, DhcpPacket.IpToString(offeredIp));
                }
            }
        }
        
        // Créer une offre en attente
        var pendingLease = new DhcpLease
        {
            MacAddress = clientMac,
            IpAddress = DhcpPacket.IpToString(offeredIp),
            Hostname = request.GetHostname() ?? string.Empty,
            LeaseStart = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.Add(OfferTimeout),
            State = DhcpLeaseState.Offered
        };
        _pendingOffers[DhcpPacket.IpToString(offeredIp)] = pendingLease;
        
        // Construire et envoyer OFFER
        var offer = DhcpPacketHandler.CreateOffer(request, offeredIp, _serverIp, _config);
        await SendResponseAsync(offer, request);
        
        _logger.LogInformation("DHCP: OFFER envoyé à {Mac} pour {Ip}", clientMac, DhcpPacket.IpToString(offeredIp));
    }

    private async Task HandleRequestAsync(DhcpPacket request, string clientMac)
    {
        // Vérifier le Server Identifier (si présent, doit correspondre à notre serveur)
        var serverIdOption = request.GetServerIdentifier();
        if (serverIdOption.HasValue && serverIdOption.Value != _serverIp)
        {
            _logger.LogDebug("DHCP: REQUEST ignoré - destiné à un autre serveur");
            return;
        }
        
        // Obtenir l'IP demandée
        uint requestedIp = request.GetRequestedIp() ?? request.CiAddr;
        if (requestedIp == 0)
        {
            _logger.LogWarning("DHCP: REQUEST sans IP valide de {Mac}", clientMac);
            var nak = DhcpPacketHandler.CreateNak(request, _serverIp);
            await SendResponseAsync(nak, request);
            return;
        }
        
        var requestedIpStr = DhcpPacket.IpToString(requestedIp);
        
        // Vérifier la validité de la demande
        bool isValid = false;
        
        // 1. Vérifier les réservations statiques
        var reservation = _config.StaticReservations
            .FirstOrDefault(r => NormalizeMac(r.MacAddress) == clientMac);
        
        if (reservation != null)
        {
            isValid = reservation.IpAddress == requestedIpStr;
            if (!isValid)
            {
                _logger.LogWarning("DHCP: {Mac} demande {Ip} mais a une réservation pour {Reserved}",
                    clientMac, requestedIpStr, reservation.IpAddress);
            }
        }
        else
        {
            // 2. Vérifier si on a fait une offre pour cette IP
            if (_pendingOffers.TryGetValue(requestedIpStr, out var pendingLease) &&
                pendingLease.MacAddress == clientMac)
            {
                isValid = true;
            }
            // 3. Vérifier si le client a déjà un bail pour cette IP
            else if (_leases.TryGetValue(clientMac, out var existingLease) &&
                     existingLease.IpAddress == requestedIpStr)
            {
                isValid = true;
            }
            // 4. L'IP est-elle dans notre plage et disponible?
            else if (IsIpInRange(requestedIp) && IsIpAvailable(requestedIp, clientMac))
            {
                isValid = true;
            }
        }
        
        if (!isValid)
        {
            _logger.LogWarning("DHCP: REQUEST refusé pour {Mac} - IP {Ip} non valide", clientMac, requestedIpStr);
            var nak = DhcpPacketHandler.CreateNak(request, _serverIp);
            await SendResponseAsync(nak, request);
            return;
        }
        
        // Créer ou mettre à jour le bail
        var lease = new DhcpLease
        {
            MacAddress = clientMac,
            IpAddress = requestedIpStr,
            Hostname = request.GetHostname() ?? string.Empty,
            LeaseStart = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(_config.LeaseTimeMinutes),
            State = DhcpLeaseState.Active
        };
        
        _leases[clientMac] = lease;
        _pendingOffers.TryRemove(requestedIpStr, out _);
        SaveLeases();
        
        // Envoyer ACK
        var ack = DhcpPacketHandler.CreateAck(request, requestedIp, _serverIp, _config);
        await SendResponseAsync(ack, request);
        
        _logger.LogInformation("DHCP: ACK envoyé à {Mac} ({Hostname}) pour {Ip}, bail jusqu'à {Expiration}",
            clientMac, lease.Hostname, requestedIpStr, lease.Expiration.ToLocalTime());
        
        // Notifier le service de découverte d'appareils
        NotifyDeviceDiscovered(lease);
    }

    private void HandleRelease(DhcpPacket request, string clientMac)
    {
        if (_leases.TryRemove(clientMac, out var lease))
        {
            lease.State = DhcpLeaseState.Released;
            SaveLeases();
            _logger.LogInformation("DHCP: Bail libéré pour {Mac} ({Ip})", clientMac, lease.IpAddress);
        }
    }

    private void HandleDecline(DhcpPacket request, string clientMac)
    {
        var declinedIp = request.GetRequestedIp() ?? request.CiAddr;
        _logger.LogWarning("DHCP: DECLINE reçu de {Mac} pour {Ip} - conflit d'adresse possible",
            clientMac, DhcpPacket.IpToString(declinedIp));
        
        // Marquer l'IP comme non utilisable temporairement
        // (Dans une implémentation complète, on maintiendrait une liste noire temporaire)
    }

    private async Task HandleInformAsync(DhcpPacket request, string clientMac)
    {
        // INFORM: le client a déjà une IP, il veut juste les paramètres réseau
        var ack = DhcpPacketHandler.CreateAck(request, request.CiAddr, _serverIp, _config);
        await SendResponseAsync(ack, request);
        
        _logger.LogInformation("DHCP: ACK (INFORM) envoyé à {Mac}", clientMac);
    }

    private async Task SendResponseAsync(DhcpPacket response, DhcpPacket request)
    {
        var data = DhcpPacketHandler.Build(response);
        
        // Déterminer l'adresse de destination
        IPEndPoint destination;
        
        if (request.GiAddr != 0)
        {
            // Via relay agent
            destination = new IPEndPoint(new IPAddress(DhcpPacket.IpToBytes(request.GiAddr)), DhcpServerPort);
        }
        else if (request.CiAddr != 0)
        {
            // Client a une IP, répondre en unicast
            destination = new IPEndPoint(new IPAddress(DhcpPacket.IpToBytes(request.CiAddr)), DhcpClientPort);
        }
        else if ((request.Flags & 0x8000) != 0)
        {
            // Flag broadcast activé
            destination = new IPEndPoint(IPAddress.Broadcast, DhcpClientPort);
        }
        else
        {
            // Broadcast par défaut pour DISCOVER/REQUEST
            destination = new IPEndPoint(IPAddress.Broadcast, DhcpClientPort);
        }
        
        try
        {
            await _udpServer!.SendAsync(data, data.Length, destination);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DHCP: Erreur envoi réponse vers {Destination}", destination);
        }
    }

    private uint AllocateNewIp(string clientMac)
    {
        var rangeStart = DhcpPacket.StringToIp(_config.RangeStart);
        var rangeEnd = DhcpPacket.StringToIp(_config.RangeEnd);
        
        for (uint ip = rangeStart; ip <= rangeEnd; ip++)
        {
            if (IsIpAvailable(ip, clientMac))
            {
                return ip;
            }
        }
        
        return 0; // Aucune IP disponible
    }

    private bool IsIpAvailable(uint ip, string clientMac)
    {
        var ipStr = DhcpPacket.IpToString(ip);
        
        // Vérifier si dans la plage
        if (!IsIpInRange(ip))
            return false;
        
        // Vérifier les réservations statiques
        var reservation = _config.StaticReservations.FirstOrDefault(r => r.IpAddress == ipStr);
        if (reservation != null && NormalizeMac(reservation.MacAddress) != clientMac)
            return false;
        
        // Vérifier les baux actifs
        var existingLease = _leases.Values.FirstOrDefault(l => l.IpAddress == ipStr);
        if (existingLease != null && existingLease.MacAddress != clientMac && existingLease.Expiration > DateTime.UtcNow)
            return false;
        
        // Vérifier les offres en attente
        if (_pendingOffers.TryGetValue(ipStr, out var pending) && 
            pending.MacAddress != clientMac && 
            pending.Expiration > DateTime.UtcNow)
            return false;
        
        return true;
    }

    private bool IsIpInRange(uint ip)
    {
        var rangeStart = DhcpPacket.StringToIp(_config.RangeStart);
        var rangeEnd = DhcpPacket.StringToIp(_config.RangeEnd);
        return ip >= rangeStart && ip <= rangeEnd;
    }

    private async Task CleanupExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                
                var expiredCount = 0;
                foreach (var kvp in _leases)
                {
                    if (kvp.Value.Expiration < DateTime.UtcNow)
                    {
                        if (_leases.TryRemove(kvp.Key, out _))
                            expiredCount++;
                    }
                }
                
                // Nettoyer les offres expirées
                foreach (var kvp in _pendingOffers)
                {
                    if (kvp.Value.Expiration < DateTime.UtcNow)
                        _pendingOffers.TryRemove(kvp.Key, out _);
                }
                
                if (expiredCount > 0)
                {
                    SaveLeases();
                    _logger.LogInformation("DHCP: {Count} bail(s) expiré(s) nettoyé(s)", expiredCount);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DHCP: Erreur nettoyage baux expirés");
            }
        }
    }

    private void NotifyDeviceDiscovered(DhcpLease lease)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var discoveryService = scope.ServiceProvider.GetService<IDeviceDiscoveryService>();
            
            // Le service de découverte va gérer l'ajout/mise à jour de l'appareil
            // via la méthode ProcessPacketAsync ou une méthode dédiée
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DHCP: Erreur notification découverte appareil");
        }
    }

    private static string NormalizeMac(string mac)
    {
        return mac.ToUpperInvariant().Replace("-", ":").Trim();
    }

    private void LoadData()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<DhcpConfig>(json) ?? new DhcpConfig();
                _logger.LogInformation("DHCP: Configuration chargée");
            }
            
            if (File.Exists(LeasesPath))
            {
                var json = File.ReadAllText(LeasesPath);
                var leases = JsonSerializer.Deserialize<List<DhcpLease>>(json) ?? new List<DhcpLease>();
                foreach (var lease in leases.Where(l => l.Expiration > DateTime.UtcNow))
                {
                    _leases[lease.MacAddress] = lease;
                }
                _logger.LogInformation("DHCP: {Count} bail(s) chargé(s)", _leases.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DHCP: Erreur chargement données");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DHCP: Erreur sauvegarde configuration");
        }
    }

    private void SaveLeases()
    {
        try
        {
            var json = JsonSerializer.Serialize(_leases.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LeasesPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DHCP: Erreur sauvegarde baux");
        }
    }
}
