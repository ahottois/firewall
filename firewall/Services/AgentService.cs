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

public class AgentService(FirewallDbContext context, ILogger<AgentService> logger) : IAgentService
{
    public async Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat)
    {
        var agent = await context.Agents.FirstOrDefaultAsync(a => a.Hostname == heartbeat.Hostname && a.MacAddress == heartbeat.MacAddress);
        if (agent == null)
        {
            agent = new Agent
            {
                Hostname = heartbeat.Hostname,
                MacAddress = heartbeat.MacAddress,
                IpAddress = heartbeat.IpAddress,
                OS = heartbeat.OS,
                RegisteredAt = DateTime.UtcNow
            };
            context.Agents.Add(agent);
        }

        agent.LastSeen = DateTime.UtcNow;
        agent.Status = AgentStatus.Online;
        agent.CpuUsage = heartbeat.CpuUsage;
        agent.MemoryUsage = heartbeat.MemoryUsage;
        agent.DiskUsage = heartbeat.DiskUsage;
        agent.Version = heartbeat.Version;
        agent.DetailsJson = heartbeat.DetailsJson;
        
        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Agent>> GetAllAgentsAsync()
    {
        return await context.Agents.ToListAsync();
    }

    public async Task DeleteAgentAsync(int id)
    {
        var agent = await context.Agents.FindAsync(id);
        if (agent != null)
        {
            context.Agents.Remove(agent);
            await context.SaveChangesAsync();
        }
    }

    public string GenerateLinuxInstallScript(string serverUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"SERVER_URL=\"{serverUrl}\"");
        sb.AppendLine("AGENT_DIR=\"/opt/netguard-agent\"");
        sb.AppendLine("SERVICE_FILE=\"/etc/systemd/system/netguard-agent.service\"");
        sb.AppendLine();
        sb.AppendLine("if [ \"$(id -u)\" -ne 0 ]; then");
        sb.AppendLine("    echo \"Please run as root\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("echo \"Installing NetGuard Agent...\"");
        sb.AppendLine("mkdir -p \"$AGENT_DIR\"");
        sb.AppendLine();
        sb.AppendLine("# Install dependencies");
        sb.AppendLine("if command -v apt-get &> /dev/null; then");
        sb.AppendLine("    apt-get update && apt-get install -y curl jq");
        sb.AppendLine("elif command -v yum &> /dev/null; then");
        sb.AppendLine("    yum install -y curl jq");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("cat > \"$AGENT_DIR/agent.sh\" << 'AGENTSCRIPT'");
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"SERVER_URL=\"{serverUrl}\"");
        sb.AppendLine("while true; do");
        sb.AppendLine("    # Gather metrics");
        sb.AppendLine("    CPU=$(top -bn1 | grep \"Cpu(s)\" | sed \"s/.*, *\\([0-9.]*\\)%* id.*/\\1/\" | awk '{print 100 - $1}')");
        sb.AppendLine("    MEM=$(free -m | awk 'NR==2{printf \"%.2f\", $3*100/$2}')");
        sb.AppendLine("    DISK=$(df -h / | awk 'NR==2{print $5}' | sed 's/%//')");
        sb.AppendLine("    IP=$(hostname -I | awk '{print $1}')");
        sb.AppendLine("    MAC=$(cat /sys/class/net/$(ip route show default | awk '{print $5}')/address 2>/dev/null || echo \"unknown\")");
        sb.AppendLine("    HOSTNAME=$(hostname)");
        sb.AppendLine("    OS=$(grep -oP '(?<=^PRETTY_NAME=).+' /etc/os-release | tr -d '\"')");
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
        sb.AppendLine("            \\\"memoryUsage\\\": $MEM,");
        sb.AppendLine("            \\\"diskUsage\\\": $DISK");
        sb.AppendLine("        }\"");
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
        sb.AppendLine("echo \"NetGuard Agent installed and started!\"");
        
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
