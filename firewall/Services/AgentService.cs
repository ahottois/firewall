using Microsoft.EntityFrameworkCore;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface IAgentService
{
    Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat);
    Task<IEnumerable<Agent>> GetAllAgentsAsync();
    Task<Agent?> GetAgentByIdAsync(int id);
    Task DeleteAgentAsync(int id);
    string GenerateLinuxInstallScript(string serverUrl);
    string GenerateWindowsInstallScript(string serverUrl);
}

public class AgentService : IAgentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentService> _logger;

    public AgentService(IServiceScopeFactory scopeFactory, ILogger<AgentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();

        var agent = await context.Agents.FirstOrDefaultAsync(a => a.Hostname == heartbeat.Hostname);
        
        if (agent == null)
        {
            agent = new Agent
            {
                Hostname = heartbeat.Hostname,
                RegisteredAt = DateTime.UtcNow
            };
            context.Agents.Add(agent);
        }

        agent.OS = heartbeat.OS;
        agent.IpAddress = heartbeat.IpAddress;
        agent.CpuUsage = heartbeat.CpuUsage;
        agent.MemoryUsage = heartbeat.MemoryUsage;
        agent.DiskUsage = heartbeat.DiskUsage;
        agent.Version = heartbeat.Version;
        agent.LastSeen = DateTime.UtcNow;
        agent.Status = AgentStatus.Online;

        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Agent>> GetAllAgentsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        
        var agents = await context.Agents.ToListAsync();
        
        // Update status for offline agents
        var now = DateTime.UtcNow;
        foreach (var agent in agents)
        {
            if ((now - agent.LastSeen).TotalMinutes > 5)
            {
                agent.Status = AgentStatus.Offline;
            }
        }
        
        return agents;
    }

    public async Task<Agent?> GetAgentByIdAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        return await context.Agents.FindAsync(id);
    }

    public async Task DeleteAgentAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        var agent = await context.Agents.FindAsync(id);
        if (agent != null)
        {
            context.Agents.Remove(agent);
            await context.SaveChangesAsync();
        }
    }

    public string GenerateLinuxInstallScript(string serverUrl)
    {
        return $@"#!/bin/bash
# NetGuard Agent Installer for Linux

SERVER_URL=""{serverUrl}""
INSTALL_DIR=""/opt/netguard-agent""
SERVICE_NAME=""netguard-agent""

echo ""Installing NetGuard Agent...""

# Create directory
mkdir -p ""$INSTALL_DIR""

# Create agent script
cat > ""$INSTALL_DIR/agent.sh"" << 'EOF'
#!/bin/bash
SERVER_URL=""__SERVER_URL__""
HOSTNAME=$(hostname)
OS=""Linux $(uname -r)""

while true; do
    # Gather stats
    CPU=$(top -bn1 | grep ""Cpu(s)"" | sed ""s/.*, *\([0-9.]*\)%* id.*/\1/"" | awk '{{print 100 - $1}}')
    MEM=$(free | grep Mem | awk '{{print $3/$2 * 100.0}}')
    DISK=$(df -h / | tail -1 | awk '{{print $5}}' | sed 's/%//')
    IP=$(hostname -I | awk '{{print $1}}')

    # Send heartbeat
    curl -s -X POST ""$SERVER_URL/api/agents/heartbeat"" \
        -H ""Content-Type: application/json"" \
        -d ""{{\""hostname\"":\""$HOSTNAME\"",\""os\"":\""$OS\"",\""ipAddress\"":\""$IP\"",\""cpuUsage\"":$CPU,\""memoryUsage\"":$MEM,\""diskUsage\"":$DISK,\""version\"":\""1.0.0\""}}""

    sleep 60
done
EOF

# Replace placeholder
sed -i ""s|__SERVER_URL__|$SERVER_URL|g"" ""$INSTALL_DIR/agent.sh""
chmod +x ""$INSTALL_DIR/agent.sh""

# Create systemd service
cat > ""/etc/systemd/system/$SERVICE_NAME.service"" << EOF
[Unit]
Description=NetGuard Agent
After=network.target

[Service]
Type=simple
ExecStart=$INSTALL_DIR/agent.sh
Restart=always
User=root

[Install]
WantedBy=multi-user.target
EOF

# Start service
systemctl daemon-reload
systemctl enable $SERVICE_NAME
systemctl start $SERVICE_NAME

echo ""NetGuard Agent installed and started!""
";
    }

    public string GenerateWindowsInstallScript(string serverUrl)
    {
        return $@"
# NetGuard Agent Installer for Windows
$ServerUrl = ""{serverUrl}""
$InstallDir = ""C:\Program Files\NetGuardAgent""
$TaskName = ""NetGuardAgent""

Write-Host ""Installing NetGuard Agent...""

# Create directory
if (!(Test-Path -Path $InstallDir)) {{
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}}

# Create agent script
$AgentScript = @""
`$ServerUrl = ""$ServerUrl""
`$Hostname = $env:COMPUTERNAME
`$OS = (Get-CimInstance Win32_OperatingSystem).Caption

while (`$true) {{
    try {{
        # Gather stats
        `$Cpu = (Get-WmiObject Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
        `$Mem = (Get-WmiObject Win32_OperatingSystem | ForEach-Object {{ (`$_.TotalVisibleMemorySize - `$_.FreePhysicalMemory) / `$_.TotalVisibleMemorySize * 100 }})
        `$Disk = (Get-WmiObject Win32_LogicalDisk -Filter ""DeviceID='C:'"" | ForEach-Object {{ (`$_.Size - `$_.FreeSpace) / `$_.Size * 100 }})
        `$Ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object {{ `$_.InterfaceAlias -notlike '*Loopback*' -and `$_.AddressState -eq 'Preferred' }} | Select-Object -First 1).IPAddress

        `$Body = @{{
            hostname = `$Hostname
            os = `$OS
            ipAddress = `$Ip
            cpuUsage = `$Cpu
            memoryUsage = `$Mem
            diskUsage = `$Disk
            version = ""1.0.0""
        }}

        Invoke-RestMethod -Uri ""`$ServerUrl/api/agents/heartbeat"" -Method Post -Body (`$Body | ConvertTo-Json) -ContentType ""application/json""
    }} catch {{
        Write-Host ""Error sending heartbeat: `$(`$_.Exception.Message)""
    }}
    Start-Sleep -Seconds 60
}}
""@

$AgentScript | Out-File -FilePath ""$InstallDir\agent.ps1"" -Encoding ASCII

# Create Scheduled Task to run at startup as SYSTEM
$Action = New-ScheduledTaskAction -Execute ""powershell.exe"" -Argument ""-ExecutionPolicy Bypass -WindowStyle Hidden -File `""$InstallDir\agent.ps1`""""
$Trigger = New-ScheduledTaskTrigger -AtStartup
$Principal = New-ScheduledTaskPrincipal -UserId ""SYSTEM"" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Force | Out-Null

# Start the task now
Start-ScheduledTask -TaskName $TaskName

Write-Host ""NetGuard Agent installed and started!""
";
    }
}
