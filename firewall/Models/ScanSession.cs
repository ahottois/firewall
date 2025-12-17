namespace NetworkFirewall.Models;

public class ScanSession
{
    public int Id { get; set; }
    public ScanType Type { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public ScanStatus Status { get; set; }
    public int ItemsScanned { get; set; }
    public int ItemsTotal { get; set; }
    public string? ResultSummary { get; set; }
}

public enum ScanType
{
    Network,
    Camera
}

public enum ScanStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Interrupted = 3
}
