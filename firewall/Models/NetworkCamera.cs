namespace NetworkFirewall.Models;

/// <summary>
/// Représente une caméra IP détectée sur le réseau
/// </summary>
public class NetworkCamera
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? StreamUrl { get; set; }
    public string? SnapshotUrl { get; set; }
    public CameraStatus Status { get; set; } = CameraStatus.Unknown;
    public PasswordStatus PasswordStatus { get; set; } = PasswordStatus.Unknown;
    public string? DetectedCredentials { get; set; }
    public DateTime FirstDetected { get; set; }
    public DateTime LastChecked { get; set; }
    public bool IsAccessible { get; set; }
    public string? Notes { get; set; }
    
    public NetworkDevice? Device { get; set; }
}

public enum CameraStatus
{
    Unknown,
    Online,
    Offline,
    Authenticated,
    RequiresAuth
}

public enum PasswordStatus
{
    Unknown,
    DefaultPassword,
    CustomPassword,
    NoPassword,
    PasswordRequired
}

/// <summary>
/// Informations d'identification par défaut des caméras
/// </summary>
public static class DefaultCameraCredentials
{
    public static readonly Dictionary<string, List<(string Username, string Password)>> ByManufacturer = new()
    {
        { "Hikvision", new() { ("admin", "12345"), ("admin", "admin"), ("admin", ""), ("admin", "1234") } },
        { "Dahua", new() { ("admin", "admin"), ("admin", ""), ("888888", "888888"), ("666666", "666666") } },
        { "Axis", new() { ("root", "pass"), ("root", "root"), ("admin", "admin") } },
        { "Foscam", new() { ("admin", ""), ("admin", "admin"), ("admin", "123456") } },
        { "TP-Link", new() { ("admin", "admin"), ("admin", ""), ("admin", "1234") } },
        { "D-Link", new() { ("admin", ""), ("admin", "admin"), ("Admin", "") } },
        { "Reolink", new() { ("admin", ""), ("admin", "admin") } },
        { "Amcrest", new() { ("admin", "admin"), ("admin", "") } },
        { "Ubiquiti", new() { ("ubnt", "ubnt"), ("admin", "admin") } },
        { "Vivotek", new() { ("root", ""), ("admin", "admin") } },
        { "Generic", new() { ("admin", "admin"), ("admin", ""), ("admin", "1234"), ("admin", "12345"), ("admin", "123456"), ("root", "root"), ("user", "user") } }
    };

    public static readonly List<int> CommonCameraPorts = new() { 80, 8080, 8081, 554, 8554, 443, 8443, 37777, 34567 };
    
    public static readonly List<string> CommonRtspPaths = new()
    {
        "/live/ch00_0",
        "/h264Preview_01_main",
        "/Streaming/Channels/101",
        "/cam/realmonitor?channel=1&subtype=0",
        "/videoMain",
        "/video1",
        "/stream1",
        "/1",
        "/ch1/main/av_stream",
        "/"
    };
}
