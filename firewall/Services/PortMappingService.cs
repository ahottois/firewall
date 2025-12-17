using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public class PortMappingService : BackgroundService
{
    private readonly ILogger<PortMappingService> _logger;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<string, Task> _activeMappings = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _mappingCts = new();

    public PortMappingService(ILogger<PortMappingService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Port Mapping Service started");

        // Start initial mappings
        foreach (var rule in _settings.Router.PortMappings.Where(r => r.Enabled))
        {
            StartMapping(rule);
        }

        // Keep service alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public void StartMapping(PortMappingRule rule)
    {
        if (_activeMappings.ContainsKey(rule.Id)) return;

        var cts = new CancellationTokenSource();
        _mappingCts[rule.Id] = cts;

        Task task;
        if (rule.Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
        {
            task = Task.Run(() => RunTcpProxy(rule, cts.Token), cts.Token);
        }
        else
        {
            task = Task.Run(() => RunUdpProxy(rule, cts.Token), cts.Token);
        }

        _activeMappings[rule.Id] = task;
        _logger.LogInformation("Started port mapping: {Name} ({Protocol} {ListenPort} -> {TargetIp}:{TargetPort})", 
            rule.Name, rule.Protocol, rule.ListenPort, rule.TargetIp, rule.TargetPort);
    }

    public async Task StopMappingAsync(string ruleId)
    {
        if (_mappingCts.TryRemove(ruleId, out var cts))
        {
            cts.Cancel();
            if (_activeMappings.TryRemove(ruleId, out var task))
            {
                try
                {
                    await task; // Wait for it to finish
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping mapping {Id}", ruleId);
                }
            }
            cts.Dispose();
            _logger.LogInformation("Stopped port mapping {Id}", ruleId);
        }
    }

    private async Task RunTcpProxy(PortMappingRule rule, CancellationToken token)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, rule.ListenPort);
            listener.Start();

            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleTcpClient(client, rule, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TCP proxy for rule {Name}", rule.Name);
        }
        finally
        {
            listener?.Stop();
        }
    }

    private async Task HandleTcpClient(TcpClient client, PortMappingRule rule, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var target = new TcpClient();
                await target.ConnectAsync(rule.TargetIp, rule.TargetPort, token);

                using var clientStream = client.GetStream();
                using var targetStream = target.GetStream();

                var clientToTarget = clientStream.CopyToAsync(targetStream, token);
                var targetToClient = targetStream.CopyToAsync(clientStream, token);

                await Task.WhenAny(clientToTarget, targetToClient);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Connection error in TCP proxy {Name}", rule.Name);
            }
        }
    }

    private async Task RunUdpProxy(PortMappingRule rule, CancellationToken token)
    {
        UdpClient? listener = null;
        try
        {
            listener = new UdpClient(rule.ListenPort);
            var targetEndpoint = new IPEndPoint(IPAddress.Parse(rule.TargetIp), rule.TargetPort);

            // Map client endpoint -> forwarder client
            // UDP is connectionless, so we need to maintain a map of "sessions" or just forward blindly if 1-to-1
            // Simple implementation: One UDP client per incoming packet source? Complex.
            // Simplest: Just forward everything to target, and replies back to last sender? 
            // Better: Create a new UdpClient for each new sender to talk to target.
            
            var clients = new ConcurrentDictionary<IPEndPoint, UdpClient>();

            while (!token.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(token);
                var clientEp = result.RemoteEndPoint;

                if (!clients.TryGetValue(clientEp, out var forwarder))
                {
                    forwarder = new UdpClient();
                    forwarder.Connect(targetEndpoint);
                    clients[clientEp] = forwarder;

                    // Start listening for responses from target
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                var response = await forwarder.ReceiveAsync(token);
                                await listener.SendAsync(response.Buffer, response.Buffer.Length, clientEp);
                            }
                        }
                        catch { 
                            clients.TryRemove(clientEp, out _);
                            forwarder.Dispose();
                        }
                    }, token);
                }

                await forwarder.SendAsync(result.Buffer, result.Buffer.Length);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UDP proxy for rule {Name}", rule.Name);
        }
        finally
        {
            listener?.Close();
        }
    }
}
