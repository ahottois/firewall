using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Data;
using NetworkFirewall.Models;
using System.Text;

namespace NetworkFirewall.Services;

public interface IAgentService
{
    Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat);
    Task<IEnumerable<Agent>> GetAllAgentsAsync();
    Task DeleteAgentAsync(int id);
    string GenerateLinuxInstallScript(string serverUrl);
    string GenerateWindowsInstallScript(string serverUrl);
}

public class AgentService : IAgentService
{
    private readonly FirewallDbContext _context;
    private readonly ILogger<AgentService> _logger;

    public AgentService(FirewallDbContext context, ILogger<AgentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat)
    {
        // Rechercher l'agent existant par plusieurs critères (ordre de priorité)
        // 1. Hostname + IP (le plus fiable)
        // 2. Hostname + MAC (si MAC est valide)
        // 3. Hostname seul (dernier recours)
        var normalizedHostname = heartbeat.Hostname?.ToLowerInvariant() ?? string.Empty;
        var normalizedMac = NormalizeMacAddress(heartbeat.MacAddress);
        var hasValidMac = !string.IsNullOrEmpty(normalizedMac) && normalizedMac != "unknown" && normalizedMac != "00:00:00:00:00:00";
        
        Agent? agent = null;
        
        // Essayer de trouver par hostname + IP d'abord
        if (!string.IsNullOrEmpty(heartbeat.IpAddress))
        {
            agent = await _context.Agents.FirstOrDefaultAsync(a => 
                a.Hostname.ToLower() == normalizedHostname && 
                a.IpAddress == heartbeat.IpAddress);
        }
        
        // Si pas trouvé, essayer par hostname + MAC (si MAC valide)
        if (agent == null && hasValidMac)
        {
            agent = await _context.Agents.FirstOrDefaultAsync(a => 
                a.Hostname.ToLower() == normalizedHostname && 
                a.MacAddress.ToLower() == normalizedMac);
        }
        
        // Dernier recours: hostname seul (pour éviter les doublons du même serveur)
        if (agent == null)
        {
            agent = await _context.Agents.FirstOrDefaultAsync(a => 
                a.Hostname.ToLower() == normalizedHostname);
        }
        
        if (agent == null)
        {
            agent = new Agent
            {
                Hostname = heartbeat.Hostname,
                MacAddress = hasValidMac ? normalizedMac : string.Empty,
                IpAddress = heartbeat.IpAddress,
                OS = heartbeat.OS,
                RegisteredAt = DateTime.UtcNow
            };
            _context.Agents.Add(agent);
            _logger.LogInformation("Nouvel agent enregistré: {Hostname} ({IP})", heartbeat.Hostname, heartbeat.IpAddress);
        }
        else
        {
            _logger.LogDebug("Agent existant mis à jour: {Hostname} ({IP})", heartbeat.Hostname, heartbeat.IpAddress);
        }

        // Mettre à jour toutes les informations
        agent.LastSeen = DateTime.UtcNow;
        agent.Status = AgentStatus.Online;
        agent.CpuUsage = heartbeat.CpuUsage;
        agent.MemoryUsage = heartbeat.MemoryUsage;
        agent.DiskUsage = heartbeat.DiskUsage;
        agent.Version = heartbeat.Version;
        agent.DetailsJson = heartbeat.DetailsJson;
        
        // Mettre à jour IP et MAC si disponibles
        if (!string.IsNullOrEmpty(heartbeat.IpAddress))
            agent.IpAddress = heartbeat.IpAddress;
        if (hasValidMac)
            agent.MacAddress = normalizedMac;
        // Mettre à jour l'OS seulement s'il est plus descriptif
        if (!string.IsNullOrEmpty(heartbeat.OS) && 
            (string.IsNullOrEmpty(agent.OS) || heartbeat.OS.Length > agent.OS.Length))
            agent.OS = heartbeat.OS;
        
        await _context.SaveChangesAsync();
    }

    private static string NormalizeMacAddress(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return string.Empty;
        
        // Retirer les espaces et convertir en minuscules
        mac = mac.Trim().ToLowerInvariant();
        
        // Convertir les différents formats en format unifié (aa:bb:cc:dd:ee:ff)
        mac = mac.Replace("-", ":").Replace(".", ":");
        
        return mac;
    }

    public async Task<IEnumerable<Agent>> GetAllAgentsAsync()
    {
        return await _context.Agents.ToListAsync();
    }

    public async Task DeleteAgentAsync(int id)
    {
        var agent = await _context.Agents.FindAsync(id);
        if (agent != null)
        {
            _context.Agents.Remove(agent);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Agent supprimé: {Id}", id);
        }
    }

    public string GenerateLinuxInstallScript(string serverUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"SERVER_URL=\"{serverUrl}\"");
        sb.AppendLine("AGENT_DIR=\"/opt/netguard-agent\"");
        sb.AppendLine("SERVICE_FILE=\"/etc/systemd/system/netguard-agent.service\"");
        sb.AppendLine("AGENT_VERSION=\"1.1.0\"");
        sb.AppendLine();
        sb.AppendLine("if [ \"$(id -u)\" -ne 0 ]; then");
        sb.AppendLine("    echo \"Please run as root\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("echo \"Installing NetGuard Agent v$AGENT_VERSION...\"");
        sb.AppendLine("mkdir -p \"$AGENT_DIR\"");
        sb.AppendLine();
        sb.AppendLine("# Install dependencies");
        sb.AppendLine("if command -v apt-get &> /dev/null; then");
        sb.AppendLine("    apt-get update -qq && apt-get install -y -qq curl jq lm-sensors 2>/dev/null || apt-get install -y -qq curl jq");
        sb.AppendLine("elif command -v yum &> /dev/null; then");
        sb.AppendLine("    yum install -y curl jq lm_sensors 2>/dev/null || yum install -y curl jq");
        sb.AppendLine("elif command -v apk &> /dev/null; then");
        sb.AppendLine("    apk add --no-cache curl jq");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("cat > \"$AGENT_DIR/agent.sh\" << 'AGENTSCRIPT'");
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"SERVER_URL=\"{serverUrl}\"");
        sb.AppendLine("AGENT_VERSION=\"1.1.0\"");
        sb.AppendLine();
        sb.AppendLine("# Function to get JSON-safe string");
        sb.AppendLine("json_escape() {");
        sb.AppendLine("    echo -n \"$1\" | sed 's/\\\\/\\\\\\\\/g; s/\"/\\\\\"/g; s/\\t/\\\\t/g; s/\\n/\\\\n/g; s/\\r/\\\\r/g'");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("while true; do");
        sb.AppendLine("    # ========== BASIC METRICS ==========");
        sb.AppendLine("    CPU=$(top -bn1 | grep \"Cpu(s)\" | sed \"s/.*, *\\([0-9.]*\\)%* id.*/\\1/\" | awk '{printf \"%.1f\", 100 - $1}')");
        sb.AppendLine("    MEM_INFO=$(free -b)");
        sb.AppendLine("    MEM_TOTAL=$(echo \"$MEM_INFO\" | awk 'NR==2{print $2}')");
        sb.AppendLine("    MEM_USED=$(echo \"$MEM_INFO\" | awk 'NR==2{print $3}')");
        sb.AppendLine("    MEM_PERCENT=$(echo \"scale=1; $MEM_USED * 100 / $MEM_TOTAL\" | bc 2>/dev/null || awk \"BEGIN {printf \\\"%.1f\\\", $MEM_USED * 100 / $MEM_TOTAL}\")");
        sb.AppendLine("    DISK_PERCENT=$(df / | awk 'NR==2{print $5}' | sed 's/%//')");
        sb.AppendLine("    IP=$(hostname -I 2>/dev/null | awk '{print $1}' || ip -4 addr show scope global | grep -oP '(?<=inet\\s)\\d+(\\.\\d+){3}' | head -1)");
        sb.AppendLine("    DEFAULT_IFACE=$(ip route show default 2>/dev/null | awk '{print $5}' | head -1)");
        sb.AppendLine("    MAC=$(cat /sys/class/net/$DEFAULT_IFACE/address 2>/dev/null || echo \"unknown\")");
        sb.AppendLine("    HOSTNAME=$(hostname)");
        sb.AppendLine("    OS=$(grep -oP '(?<=^PRETTY_NAME=).+' /etc/os-release 2>/dev/null | tr -d '\"' || uname -o)");
        sb.AppendLine();
        sb.AppendLine("    # ========== HARDWARE INFO ==========");
        sb.AppendLine("    CPU_MODEL=$(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | sed 's/^[ \\t]*//' || echo \"Unknown\")");
        sb.AppendLine("    CPU_CORES=$(nproc 2>/dev/null || grep -c processor /proc/cpuinfo 2>/dev/null || echo \"1\")");
        sb.AppendLine("    CPU_FREQ=$(grep -m1 'cpu MHz' /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | sed 's/^[ \\t]*//' | cut -d'.' -f1 || echo \"0\")");
        sb.AppendLine("    MEM_TOTAL_GB=$(echo \"scale=1; $MEM_TOTAL / 1073741824\" | bc 2>/dev/null || awk \"BEGIN {printf \\\"%.1f\\\", $MEM_TOTAL / 1073741824}\")");
        sb.AppendLine("    MEM_USED_GB=$(echo \"scale=1; $MEM_USED / 1073741824\" | bc 2>/dev/null || awk \"BEGIN {printf \\\"%.1f\\\", $MEM_USED / 1073741824}\")");
        sb.AppendLine("    SWAP_TOTAL=$(free -b | awk 'NR==3{print $2}')");
        sb.AppendLine("    SWAP_USED=$(free -b | awk 'NR==3{print $3}')");
        sb.AppendLine("    SWAP_TOTAL_GB=$(echo \"scale=1; $SWAP_TOTAL / 1073741824\" | bc 2>/dev/null || echo \"0\")");
        sb.AppendLine("    SWAP_USED_GB=$(echo \"scale=1; $SWAP_USED / 1073741824\" | bc 2>/dev/null || echo \"0\")");
        sb.AppendLine();
        sb.AppendLine("    # CPU Temperature (if available)");
        sb.AppendLine("    CPU_TEMP=\"N/A\"");
        sb.AppendLine("    if [ -f /sys/class/thermal/thermal_zone0/temp ]; then");
        sb.AppendLine("        TEMP_RAW=$(cat /sys/class/thermal/thermal_zone0/temp 2>/dev/null)");
        sb.AppendLine("        CPU_TEMP=$(echo \"scale=1; $TEMP_RAW / 1000\" | bc 2>/dev/null || echo \"N/A\")");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    # ========== DISK INFO ==========");
        sb.AppendLine("    DISK_TOTAL=$(df -B1 / | awk 'NR==2{print $2}')");
        sb.AppendLine("    DISK_USED=$(df -B1 / | awk 'NR==2{print $3}')");
        sb.AppendLine("    DISK_TOTAL_GB=$(echo \"scale=1; $DISK_TOTAL / 1073741824\" | bc 2>/dev/null || awk \"BEGIN {printf \\\"%.1f\\\", $DISK_TOTAL / 1073741824}\")");
        sb.AppendLine("    DISK_USED_GB=$(echo \"scale=1; $DISK_USED / 1073741824\" | bc 2>/dev/null || awk \"BEGIN {printf \\\"%.1f\\\", $DISK_USED / 1073741824}\")");
        sb.AppendLine();
        sb.AppendLine("    # All disks info");
        sb.AppendLine("    DISKS_JSON=$(df -BG -x tmpfs -x devtmpfs -x squashfs 2>/dev/null | awk 'NR>1 {gsub(/G/,\"\"); printf \"{\\\"mount\\\":\\\"%s\\\",\\\"total\\\":%s,\\\"used\\\":%s,\\\"percent\\\":%d},\", $6, $2, $3, $5}' | sed 's/,$//')");
        sb.AppendLine("    DISKS_JSON=\"[$DISKS_JSON]\"");
        sb.AppendLine();
        sb.AppendLine("    # ========== SYSTEM INFO ==========");
        sb.AppendLine("    KERNEL=$(uname -r)");
        sb.AppendLine("    ARCH=$(uname -m)");
        sb.AppendLine("    UPTIME_SEC=$(cat /proc/uptime 2>/dev/null | cut -d' ' -f1 | cut -d'.' -f1)");
        sb.AppendLine("    UPTIME_DAYS=$((UPTIME_SEC / 86400))");
        sb.AppendLine("    UPTIME_HOURS=$(( (UPTIME_SEC % 86400) / 3600 ))");
        sb.AppendLine("    UPTIME_MINS=$(( (UPTIME_SEC % 3600) / 60 ))");
        sb.AppendLine("    UPTIME=\"${UPTIME_DAYS}j ${UPTIME_HOURS}h ${UPTIME_MINS}m\"");
        sb.AppendLine("    LOAD_AVG=$(cat /proc/loadavg 2>/dev/null | awk '{print $1, $2, $3}' || echo \"0 0 0\")");
        sb.AppendLine("    LOAD_1=$(echo $LOAD_AVG | awk '{print $1}')");
        sb.AppendLine("    LOAD_5=$(echo $LOAD_AVG | awk '{print $2}')");
        sb.AppendLine("    LOAD_15=$(echo $LOAD_AVG | awk '{print $3}')");
        sb.AppendLine("    PROCESS_COUNT=$(ps aux 2>/dev/null | wc -l)");
        sb.AppendLine("    LAST_BOOT=$(who -b 2>/dev/null | awk '{print $3, $4}' || uptime -s 2>/dev/null || echo \"Unknown\")");
        sb.AppendLine();
        sb.AppendLine("    # ========== NETWORK INFO ==========");
        sb.AppendLine("    # Get all network interfaces with their IPs");
        sb.AppendLine("    INTERFACES_JSON=$(ip -j addr show 2>/dev/null | jq -c '[.[] | select(.operstate==\"UP\" and .link_type!=\"loopback\") | {name: .ifname, mac: .address, ips: [.addr_info[] | select(.family==\"inet\") | .local]}]' 2>/dev/null || echo \"[]\")");
        sb.AppendLine("    if [ \"$INTERFACES_JSON\" = \"[]\" ] || [ -z \"$INTERFACES_JSON\" ]; then");
        sb.AppendLine("        # Fallback without jq");
        sb.AppendLine("        INTERFACES_JSON=\"[{\\\"name\\\":\\\"$DEFAULT_IFACE\\\",\\\"mac\\\":\\\"$MAC\\\",\\\"ips\\\":[\\\"$IP\\\"]}]\"");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    # Network traffic (bytes)");
        sb.AppendLine("    if [ -n \"$DEFAULT_IFACE\" ] && [ -f \"/sys/class/net/$DEFAULT_IFACE/statistics/rx_bytes\" ]; then");
        sb.AppendLine("        RX_BYTES=$(cat /sys/class/net/$DEFAULT_IFACE/statistics/rx_bytes 2>/dev/null || echo \"0\")");
        sb.AppendLine("        TX_BYTES=$(cat /sys/class/net/$DEFAULT_IFACE/statistics/tx_bytes 2>/dev/null || echo \"0\")");
        sb.AppendLine("        RX_GB=$(echo \"scale=2; $RX_BYTES / 1073741824\" | bc 2>/dev/null || echo \"0\")");
        sb.AppendLine("        TX_GB=$(echo \"scale=2; $TX_BYTES / 1073741824\" | bc 2>/dev/null || echo \"0\")");
        sb.AppendLine("    else");
        sb.AppendLine("        RX_GB=\"0\"");
        sb.AppendLine("        TX_GB=\"0\"");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    # ========== SERVICES STATUS ==========");
        sb.AppendLine("    # Check common services");
        sb.AppendLine("    SSH_STATUS=$(systemctl is-active sshd 2>/dev/null || systemctl is-active ssh 2>/dev/null || echo \"unknown\")");
        sb.AppendLine("    DOCKER_STATUS=$(systemctl is-active docker 2>/dev/null || echo \"not installed\")");
        sb.AppendLine("    DOCKER_CONTAINERS=$(docker ps -q 2>/dev/null | wc -l || echo \"0\")");
        sb.AppendLine();
        sb.AppendLine("    # ========== BUILD JSON PAYLOAD ==========");
        sb.AppendLine("    DETAILS_JSON=$(cat << DETAILSEOF");
        sb.AppendLine("{");
        sb.AppendLine("  \"hardware\": {");
        sb.AppendLine("    \"cpuModel\": \"$(json_escape \"$CPU_MODEL\")\",");
        sb.AppendLine("    \"cpuCores\": $CPU_CORES,");
        sb.AppendLine("    \"cpuFreqMhz\": $CPU_FREQ,");
        sb.AppendLine("    \"cpuTemp\": \"$CPU_TEMP\",");
        sb.AppendLine("    \"totalRam\": \"${MEM_TOTAL_GB} GB\",");
        sb.AppendLine("    \"usedRam\": \"${MEM_USED_GB} GB\",");
        sb.AppendLine("    \"totalSwap\": \"${SWAP_TOTAL_GB} GB\",");
        sb.AppendLine("    \"usedSwap\": \"${SWAP_USED_GB} GB\",");
        sb.AppendLine("    \"diskTotal\": \"${DISK_TOTAL_GB} GB\",");
        sb.AppendLine("    \"diskUsed\": \"${DISK_USED_GB} GB\"");
        sb.AppendLine("  },");
        sb.AppendLine("  \"system\": {");
        sb.AppendLine("    \"hostname\": \"$HOSTNAME\",");
        sb.AppendLine("    \"kernel\": \"$KERNEL\",");
        sb.AppendLine("    \"arch\": \"$ARCH\",");
        sb.AppendLine("    \"uptime\": \"$UPTIME\",");
        sb.AppendLine("    \"uptimeSeconds\": $UPTIME_SEC,");
        sb.AppendLine("    \"lastBoot\": \"$(json_escape \"$LAST_BOOT\")\",");
        sb.AppendLine("    \"loadAvg1\": $LOAD_1,");
        sb.AppendLine("    \"loadAvg5\": $LOAD_5,");
        sb.AppendLine("    \"loadAvg15\": $LOAD_15,");
        sb.AppendLine("    \"processCount\": $PROCESS_COUNT");
        sb.AppendLine("  },");
        sb.AppendLine("  \"network\": {");
        sb.AppendLine("    \"primaryInterface\": \"$DEFAULT_IFACE\",");
        sb.AppendLine("    \"primaryIp\": \"$IP\",");
        sb.AppendLine("    \"primaryMac\": \"$MAC\",");
        sb.AppendLine("    \"rxTotalGb\": $RX_GB,");
        sb.AppendLine("    \"txTotalGb\": $TX_GB,");
        sb.AppendLine("    \"interfaces\": $INTERFACES_JSON");
        sb.AppendLine("  },");
        sb.AppendLine("  \"disks\": $DISKS_JSON,");
        sb.AppendLine("  \"services\": {");
        sb.AppendLine("    \"ssh\": \"$SSH_STATUS\",");
        sb.AppendLine("    \"docker\": \"$DOCKER_STATUS\",");
        sb.AppendLine("    \"dockerContainers\": $DOCKER_CONTAINERS");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("DETAILSEOF");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("    # Escape JSON for embedding");
        sb.AppendLine("    DETAILS_ESCAPED=$(echo \"$DETAILS_JSON\" | jq -c . 2>/dev/null | sed 's/\\\\/\\\\\\\\/g; s/\"/\\\\\"/g')");
        sb.AppendLine("    if [ -z \"$DETAILS_ESCAPED\" ]; then");
        sb.AppendLine("        DETAILS_ESCAPED=\"{}\"");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    # Send heartbeat");
        sb.AppendLine("    curl -s -X POST \"${SERVER_URL}/api/agents/heartbeat\" \\");
        sb.AppendLine("        -H \"Content-Type: application/json\" \\");
        sb.AppendLine("        -d \"{");
        sb.AppendLine("            \\\"hostname\\\": \\\"$HOSTNAME\\\",");
        sb.AppendLine("            \\\"macAddress\\\": \\\"$MAC\\\",");
        sb.AppendLine("            \\\"ipAddress\\\": \\\"$IP\\\",");
        sb.AppendLine("            \\\"os\\\": \\\"$OS\\\",");
        sb.AppendLine("            \\\"cpuUsage\\\": $CPU,");
        sb.AppendLine("            \\\"memoryUsage\\\": $MEM_PERCENT,");
        sb.AppendLine("            \\\"diskUsage\\\": $DISK_PERCENT,");
        sb.AppendLine("            \\\"version\\\": \\\"$AGENT_VERSION\\\",");
        sb.AppendLine("            \\\"detailsJson\\\": \\\"$DETAILS_ESCAPED\\\"");
        sb.AppendLine("        }\" > /dev/null 2>&1");
        sb.AppendLine();
        sb.AppendLine("    sleep 60");
        sb.AppendLine("done");
        sb.AppendLine("AGENTSCRIPT");
        sb.AppendLine();
        sb.AppendLine("chmod +x \"$AGENT_DIR/agent.sh\"");
        sb.AppendLine();
        sb.AppendLine("echo \"Creating systemd service...\"");
        sb.AppendLine("cat > \"$SERVICE_FILE\" << EOF");
        sb.AppendLine("[Unit]");
        sb.AppendLine("Description=NetGuard Agent");
        sb.AppendLine("After=network.target");
        sb.AppendLine();
        sb.AppendLine("[Service]");
        sb.AppendLine("ExecStart=$AGENT_DIR/agent.sh");
        sb.AppendLine("Restart=always");
        sb.AppendLine("RestartSec=10");
        sb.AppendLine("User=root");
        sb.AppendLine();
        sb.AppendLine("[Install]");
        sb.AppendLine("WantedBy=multi-user.target");
        sb.AppendLine("EOF");
        sb.AppendLine();
        sb.AppendLine("systemctl daemon-reload");
        sb.AppendLine("systemctl enable netguard-agent");
        sb.AppendLine("systemctl restart netguard-agent");
        sb.AppendLine();
        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"========================================\"");
        sb.AppendLine("echo \"NetGuard Agent v$AGENT_VERSION installed!\"");
        sb.AppendLine("echo \"Status: $(systemctl is-active netguard-agent)\"");
        sb.AppendLine("echo \"========================================\"");
        
        return sb.ToString();
    }

    public string GenerateWindowsInstallScript(string serverUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"$ServerUrl = \"{serverUrl}\"");
        sb.AppendLine("$AgentDir = \"C:\\Program Files\\NetGuardAgent\"");
        sb.AppendLine("$ScriptPath = \"$AgentDir\\agent.ps1\"");
        sb.AppendLine();
        sb.AppendLine("if (-not (Test-Path $AgentDir)) {");
        sb.AppendLine("    New-Item -ItemType Directory -Path $AgentDir -Force");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("$ScriptContent = @'");
        sb.AppendLine($"$ServerUrl = \"{serverUrl}\"");
        sb.AppendLine();
        sb.AppendLine("while ($true) {");
        sb.AppendLine("    try {");
        sb.AppendLine("        $os = (Get-CimInstance Win32_OperatingSystem).Caption");
        sb.AppendLine("        $cpu = (Get-WmiObject Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average");
        sb.AppendLine("        $mem = (Get-WmiObject Win32_OperatingSystem)");
        sb.AppendLine("        $memUsage = (($mem.TotalVisibleMemorySize - $mem.FreePhysicalMemory) / $mem.TotalVisibleMemorySize) * 100");
        sb.AppendLine("        $disk = (Get-WmiObject Win32_LogicalDisk -Filter \"DeviceID='C:'\")");
        sb.AppendLine("        $diskUsage = (($disk.Size - $disk.FreeSpace) / $disk.Size) * 100");
        sb.AppendLine("        $ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Loopback*' -and $_.InterfaceAlias -notlike '*vEthernet*' } | Select-Object -First 1).IPAddress");
        sb.AppendLine("        $mac = (Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1).MacAddress");
        sb.AppendLine("        $hostname = $env:COMPUTERNAME");
        sb.AppendLine();
        sb.AppendLine("        $body = @{");
        sb.AppendLine("            hostname = $hostname");
        sb.AppendLine("            macAddress = $mac");
        sb.AppendLine("            ipAddress = $ip");
        sb.AppendLine("            os = $os");
        sb.AppendLine("            cpuUsage = [math]::Round($cpu, 2)");
        sb.AppendLine("            memoryUsage = [math]::Round($memUsage, 2)");
        sb.AppendLine("            diskUsage = [math]::Round($diskUsage, 2)");
        sb.AppendLine("        } | ConvertTo-Json");
        sb.AppendLine();
        sb.AppendLine("        Invoke-RestMethod -Uri \"$ServerUrl/api/agents/heartbeat\" -Method Post -Body $body -ContentType \"application/json\"");
        sb.AppendLine("    } catch {");
        sb.AppendLine("        Write-Error \"Failed to send heartbeat: $_\"");
        sb.AppendLine("    }");
        sb.AppendLine("    Start-Sleep -Seconds 60");
        sb.AppendLine("}");
        sb.AppendLine("'@");
        sb.AppendLine();
        sb.AppendLine("Set-Content -Path $ScriptPath -Value $ScriptContent");
        sb.AppendLine();
        sb.AppendLine("# Create Scheduled Task");
        sb.AppendLine("$Action = New-ScheduledTaskAction -Execute 'Powershell.exe' -Argument \"-ExecutionPolicy Bypass -WindowStyle Hidden -File `\"$ScriptPath`\"\"");
        sb.AppendLine("$Trigger = New-ScheduledTaskTrigger -AtStartup");
        sb.AppendLine("$Principal = New-ScheduledTaskPrincipal -UserId \"SYSTEM\" -LogonType ServiceAccount");
        sb.AppendLine("Register-ScheduledTask -Action $Action -Trigger $Trigger -Principal $Principal -TaskName \"NetGuardAgent\" -Description \"NetGuard Monitoring Agent\" -Force");
        sb.AppendLine();
        sb.AppendLine("Start-ScheduledTask -TaskName \"NetGuardAgent\"");
        sb.AppendLine("Write-Host \"NetGuard Agent installed and started!\"");
        
        return sb.ToString();
    }
}
