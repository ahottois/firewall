using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface INatService
{
    NatConfig GetConfig();
    Task UpdateConfigAsync(NatConfig config);
    Task<IEnumerable<NatRule>> GetRulesAsync();
    Task<NatRule> AddRuleAsync(NatRuleDto rule);
    Task UpdateRuleAsync(int id, NatRuleDto rule);
    Task DeleteRuleAsync(int id);
    Task<IEnumerable<NatConnection>> GetConnectionsAsync();
    Task EnableMasqueradeAsync(string wanInterface);
    Task DisableMasqueradeAsync();
}

public class NatService : INatService
{
    private readonly ILogger<NatService> _logger;
    private NatConfig _config = new();
    private int _ruleIdCounter = 1;
    
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const string ConfigPath = "nat_config.json";

    public NatService(ILogger<NatService> logger)
    {
        _logger = logger;
        LoadConfig();
    }

    public NatConfig GetConfig() => _config;

    public async Task UpdateConfigAsync(NatConfig config)
    {
        _config = config;
        SaveConfig();

        if (IsLinux)
        {
            await ApplyNatConfigAsync();
        }

        _logger.LogInformation("NAT configuration updated (Enabled: {Enabled}, Masquerade: {Masq})", 
            config.Enabled, config.MasqueradeEnabled);
    }

    public Task<IEnumerable<NatRule>> GetRulesAsync()
    {
        return Task.FromResult(_config.Rules.AsEnumerable());
    }

    public async Task<NatRule> AddRuleAsync(NatRuleDto dto)
    {
        var rule = new NatRule
        {
            Id = _ruleIdCounter++,
            Name = dto.Name,
            Type = dto.Type,
            Protocol = dto.Protocol,
            SourceAddress = dto.SourceAddress,
            SourcePort = dto.SourcePort,
            DestinationAddress = dto.DestinationAddress,
            DestinationPort = dto.DestinationPort,
            TranslatedAddress = dto.TranslatedAddress,
            TranslatedPort = dto.TranslatedPort,
            InInterface = dto.InInterface,
            OutInterface = dto.OutInterface,
            Masquerade = dto.Masquerade,
            Enabled = true
        };

        _config.Rules.Add(rule);
        SaveConfig();

        if (IsLinux && _config.Enabled)
        {
            await ApplyNatRuleAsync(rule);
        }

        _logger.LogInformation("NAT rule added: {Name} ({Type})", rule.Name, rule.Type);
        return rule;
    }

    public async Task UpdateRuleAsync(int id, NatRuleDto dto)
    {
        var rule = _config.Rules.FirstOrDefault(r => r.Id == id);
        if (rule == null)
            throw new KeyNotFoundException($"NAT rule {id} not found");

        // Supprimer l'ancienne règle
        if (IsLinux && _config.Enabled)
        {
            await RemoveNatRuleAsync(rule);
        }

        // Mettre à jour
        rule.Name = dto.Name;
        rule.Type = dto.Type;
        rule.Protocol = dto.Protocol;
        rule.SourceAddress = dto.SourceAddress;
        rule.SourcePort = dto.SourcePort;
        rule.DestinationAddress = dto.DestinationAddress;
        rule.DestinationPort = dto.DestinationPort;
        rule.TranslatedAddress = dto.TranslatedAddress;
        rule.TranslatedPort = dto.TranslatedPort;
        rule.InInterface = dto.InInterface;
        rule.OutInterface = dto.OutInterface;
        rule.Masquerade = dto.Masquerade;

        SaveConfig();

        // Appliquer la nouvelle règle
        if (IsLinux && _config.Enabled && rule.Enabled)
        {
            await ApplyNatRuleAsync(rule);
        }

        _logger.LogInformation("NAT rule updated: {Name}", rule.Name);
    }

    public async Task DeleteRuleAsync(int id)
    {
        var rule = _config.Rules.FirstOrDefault(r => r.Id == id);
        if (rule == null)
            throw new KeyNotFoundException($"NAT rule {id} not found");

        if (IsLinux && _config.Enabled)
        {
            await RemoveNatRuleAsync(rule);
        }

        _config.Rules.Remove(rule);
        SaveConfig();

        _logger.LogInformation("NAT rule deleted: {Name}", rule.Name);
    }

    public async Task<IEnumerable<NatConnection>> GetConnectionsAsync()
    {
        var connections = new List<NatConnection>();

        if (!IsLinux) return connections;

        try
        {
            // Lire la table conntrack
            var output = await ExecuteCommandAsync("conntrack", "-L -n");
            connections.AddRange(ParseConntrackOutput(output));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get NAT connections");
        }

        return connections;
    }

    private IEnumerable<NatConnection> ParseConntrackOutput(string output)
    {
        var connections = new List<NatConnection>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            try
            {
                var conn = new NatConnection();
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                    conn.Protocol = parts[0];

                foreach (var part in parts)
                {
                    if (part.StartsWith("src="))
                        conn.OriginalSource = part.Substring(4);
                    else if (part.StartsWith("dst="))
                        conn.OriginalDestination = part.Substring(4);
                    else if (part.StartsWith("sport="))
                        int.TryParse(part.Substring(6), out var sport);
                    else if (part.StartsWith("dport="))
                        int.TryParse(part.Substring(6), out var dport);
                }

                connections.Add(conn);
            }
            catch { }
        }

        return connections;
    }

    public async Task EnableMasqueradeAsync(string wanInterface)
    {
        if (!IsLinux) return;

        try
        {
            // Activer le forwarding IP
            await ExecuteCommandAsync("sysctl", "-w net.ipv4.ip_forward=1");
            
            // Ajouter la règle MASQUERADE
            await ExecuteCommandAsync("iptables", $"-t nat -A POSTROUTING -o {wanInterface} -j MASQUERADE");
            
            // Permettre le trafic forwarded
            await ExecuteCommandAsync("iptables", "-A FORWARD -i eth0 -o " + wanInterface + " -j ACCEPT");
            await ExecuteCommandAsync("iptables", "-A FORWARD -i " + wanInterface + " -o eth0 -m state --state RELATED,ESTABLISHED -j ACCEPT");

            _config.MasqueradeEnabled = true;
            _config.WanInterface = wanInterface;
            SaveConfig();

            _logger.LogInformation("Masquerade enabled on {Interface}", wanInterface);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable masquerade");
            throw;
        }
    }

    public async Task DisableMasqueradeAsync()
    {
        if (!IsLinux) return;

        try
        {
            if (!string.IsNullOrEmpty(_config.WanInterface))
            {
                await ExecuteCommandAsync("iptables", $"-t nat -D POSTROUTING -o {_config.WanInterface} -j MASQUERADE");
            }

            _config.MasqueradeEnabled = false;
            SaveConfig();

            _logger.LogInformation("Masquerade disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable masquerade");
        }
    }

    private async Task ApplyNatConfigAsync()
    {
        if (!_config.Enabled)
        {
            await FlushNatRulesAsync();
            return;
        }

        // Activer le forwarding IP
        await ExecuteCommandAsync("sysctl", "-w net.ipv4.ip_forward=1");

        // Appliquer le masquerade si activé
        if (_config.MasqueradeEnabled && !string.IsNullOrEmpty(_config.WanInterface))
        {
            await EnableMasqueradeAsync(_config.WanInterface);
        }

        // Appliquer toutes les règles
        foreach (var rule in _config.Rules.Where(r => r.Enabled))
        {
            await ApplyNatRuleAsync(rule);
        }
    }

    private async Task ApplyNatRuleAsync(NatRule rule)
    {
        if (!IsLinux) return;

        var chain = rule.Type switch
        {
            NatType.SNAT => "POSTROUTING",
            NatType.DNAT => "PREROUTING",
            NatType.PAT => "PREROUTING",
            _ => "PREROUTING"
        };

        var proto = rule.Protocol.ToLower();
        var cmd = $"-t nat -A {chain}";

        // Protocole
        if (proto != "all")
            cmd += $" -p {proto}";

        // Interface entrée/sortie
        if (!string.IsNullOrEmpty(rule.InInterface))
            cmd += $" -i {rule.InInterface}";
        if (!string.IsNullOrEmpty(rule.OutInterface))
            cmd += $" -o {rule.OutInterface}";

        // Source
        if (!string.IsNullOrEmpty(rule.SourceAddress))
            cmd += $" -s {rule.SourceAddress}";
        if (!string.IsNullOrEmpty(rule.SourcePort) && proto != "all")
            cmd += $" --sport {rule.SourcePort}";

        // Destination
        if (!string.IsNullOrEmpty(rule.DestinationAddress))
            cmd += $" -d {rule.DestinationAddress}";
        if (!string.IsNullOrEmpty(rule.DestinationPort) && proto != "all")
            cmd += $" --dport {rule.DestinationPort}";

        // Action
        if (rule.Masquerade)
        {
            cmd += " -j MASQUERADE";
        }
        else if (rule.Type == NatType.SNAT && !string.IsNullOrEmpty(rule.TranslatedAddress))
        {
            var to = rule.TranslatedAddress;
            if (!string.IsNullOrEmpty(rule.TranslatedPort))
                to += $":{rule.TranslatedPort}";
            cmd += $" -j SNAT --to-source {to}";
        }
        else if ((rule.Type == NatType.DNAT || rule.Type == NatType.PAT) && !string.IsNullOrEmpty(rule.TranslatedAddress))
        {
            var to = rule.TranslatedAddress;
            if (!string.IsNullOrEmpty(rule.TranslatedPort))
                to += $":{rule.TranslatedPort}";
            cmd += $" -j DNAT --to-destination {to}";
        }

        // Commentaire pour identification
        cmd += $" -m comment --comment \"netguard-nat-{rule.Id}\"";

        await ExecuteCommandAsync("iptables", cmd);
    }

    private async Task RemoveNatRuleAsync(NatRule rule)
    {
        // Trouver et supprimer la règle par son commentaire
        var tables = new[] { "PREROUTING", "POSTROUTING" };
        
        foreach (var table in tables)
        {
            var output = await ExecuteCommandAsync("iptables", $"-t nat -L {table} --line-numbers -n");
            var lines = output.Split('\n');
            
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Contains($"netguard-nat-{rule.Id}"))
                {
                    var lineNum = lines[i].Split(' ')[0];
                    await ExecuteCommandAsync("iptables", $"-t nat -D {table} {lineNum}");
                }
            }
        }
    }

    private async Task FlushNatRulesAsync()
    {
        // Supprimer toutes les règles NAT NetGuard
        var output = await ExecuteCommandAsync("iptables", "-t nat -L --line-numbers -n");
        var lines = output.Split('\n');

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Contains("netguard-nat-"))
            {
                // Extraire le numéro de ligne et la chaîne
                // Format: "num  MASQUERADE  all  --  anywhere  anywhere  /* netguard-nat-1 */"
            }
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<NatConfig>(json) ?? new NatConfig();
                _ruleIdCounter = _config.Rules.Any() ? _config.Rules.Max(r => r.Id) + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load NAT configuration");
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
            _logger.LogError(ex, "Failed to save NAT configuration");
        }
    }

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
            _logger.LogDebug(ex, "Command failed: {Cmd} {Args}", command, arguments);
            return string.Empty;
        }
    }
}
