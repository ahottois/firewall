using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetworkFirewall.Models;
using PacketDotNet;
using PacketDotNet.Utils;

namespace NetworkFirewall.Services;

public class DnsServerService : BackgroundService
{
    private readonly ILogger<DnsServerService> _logger;
    private readonly AppSettings _settings;
    private readonly IDnsBlocklistService _blocklistService;
    private readonly ConcurrentQueue<DnsLog> _logQueue = new();
    private UdpClient? _udpListener;

    public DnsServerService(
        ILogger<DnsServerService> logger,
        IOptions<AppSettings> settings,
        IDnsBlocklistService blocklistService)
    {
        _logger = logger;
        _settings = settings.Value;
        _blocklistService = blocklistService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Dns.Enabled)
        {
            _logger.LogInformation("DNS Server is disabled.");
            return;
        }

        // Initial blocklist download
        await _blocklistService.RefreshListsAsync();

        try
        {
            _udpListener = new UdpClient(_settings.Dns.Port);
            _logger.LogInformation("DNS Server started on port {Port}", _settings.Dns.Port);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync(stoppingToken);
                    _ = HandleQueryAsync(result.Buffer, result.RemoteEndPoint);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving DNS packet");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DNS Server. Ensure you have permission to bind to port 53.");
        }
        finally
        {
            _udpListener?.Close();
        }
    }

    private async Task HandleQueryAsync(byte[] queryBuffer, IPEndPoint clientEndpoint)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string domain = "Unknown";
        string queryType = "Unknown";
        DnsAction action = DnsAction.Allowed;
        string? blockCategory = null;

        try
        {
            // Simple manual parsing to extract domain name from query
            // Header is 12 bytes. Question starts at 12.
            if (queryBuffer.Length > 12)
            {
                domain = ParseDomainName(queryBuffer, 12);
                // Type is 2 bytes after domain
                // Class is 2 bytes after Type
            }

            if (_blocklistService.IsBlocked(domain, out var category))
            {
                action = DnsAction.Blocked;
                blockCategory = category;
                
                // Send NXDOMAIN or Refused
                var response = CreateNxDomainResponse(queryBuffer);
                await _udpListener!.SendAsync(response, response.Length, clientEndpoint);
            }
            else
            {
                // Forward to upstream
                var upstream = IPAddress.Parse(_settings.Dns.UpstreamDns);
                using var forwarder = new UdpClient();
                await forwarder.SendAsync(queryBuffer, queryBuffer.Length, new IPEndPoint(upstream, 53));
                
                // Wait for response (with timeout)
                var responseTask = forwarder.ReceiveAsync();
                if (await Task.WhenAny(responseTask, Task.Delay(2000)) == responseTask)
                {
                    var result = await responseTask;
                    await _udpListener!.SendAsync(result.Buffer, result.Buffer.Length, clientEndpoint);
                }
                else
                {
                    action = DnsAction.Error; // Timeout
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling DNS query for {Domain}", domain);
            action = DnsAction.Error;
        }

        sw.Stop();

        // Log
        var log = new DnsLog
        {
            Timestamp = DateTime.UtcNow,
            ClientIp = clientEndpoint.Address.ToString(),
            Domain = domain,
            QueryType = queryType, // Need better parsing for this
            Action = action,
            BlocklistCategory = blockCategory,
            ResponseTimeMs = sw.ElapsedMilliseconds
        };
        
        _logQueue.Enqueue(log);
        if (_logQueue.Count > 1000) _logQueue.TryDequeue(out _);
    }

    private string ParseDomainName(byte[] buffer, int offset)
    {
        var labels = new List<string>();
        int current = offset;
        
        while (current < buffer.Length)
        {
            int length = buffer[current++];
            if (length == 0) break;
            if ((length & 0xC0) == 0xC0) // Pointer (compression) - not handled in simple parser
            {
                break; 
            }
            
            if (current + length > buffer.Length) break;
            
            var label = System.Text.Encoding.ASCII.GetString(buffer, current, length);
            labels.Add(label);
            current += length;
        }
        
        return string.Join(".", labels);
    }

    private byte[] CreateNxDomainResponse(byte[] query)
    {
        // Copy query ID and flags
        var response = new byte[query.Length];
        Array.Copy(query, response, query.Length);
        
        // Set QR (Response) = 1, AA = 0, TC = 0, RD = 1, RA = 1, RCODE = 3 (NXDOMAIN)
        // Flags are at index 2 and 3
        // Original flags: query[2], query[3]
        // Response flags: 
        // Byte 2: QR(1) Opcode(4) AA(1) TC(1) RD(1) -> 1000 0001 (0x81) usually
        // Byte 3: RA(1) Z(3) RCODE(4) -> 1000 0011 (0x83) for NXDOMAIN
        
        response[2] = 0x81; // Standard query response, Recursion Desired
        response[3] = 0x83; // Recursion Available, NXDOMAIN
        
        return response;
    }

    public IEnumerable<DnsLog> GetRecentLogs(int count)
    {
        return _logQueue.Reverse().Take(count);
    }
}
