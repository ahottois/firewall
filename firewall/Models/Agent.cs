using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NetworkFirewall.Models;

public class Agent
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Hostname { get; set; } = string.Empty;

    [MaxLength(50)]
    public string OS { get; set; } = string.Empty;

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public AgentStatus Status { get; set; } = AgentStatus.Offline;

    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double DiskUsage { get; set; }
    
    [MaxLength(20)]
    public string Version { get; set; } = "1.0.0";

    public string? DetailsJson { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

public enum AgentStatus
{
    Offline,
    Online,
    Warning,
    Critical
}

public class AgentHeartbeat
{
    public string Hostname { get; set; } = string.Empty;
    public string OS { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double DiskUsage { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
}
