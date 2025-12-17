using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetworkFirewall.Data;
using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

public interface ICameraDetectionService
{
    Task<IEnumerable<NetworkCamera>> ScanForCamerasAsync(CancellationToken cancellationToken = default);
    Task<NetworkCamera?> CheckCameraAsync(string ip, int port);
    Task<CameraCheckResult> TestCredentialsAsync(string ip, int port, string username, string password);
    Task<CameraCheckResult> TestDefaultCredentialsAsync(string ip, int port);
    Task<string?> GetSnapshotAsync(int cameraId);
}

public class CameraCheckResult
{
    public bool Success { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool IsDefaultPassword { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? StreamUrl { get; set; }
    public string? SnapshotUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CameraDetectionService : ICameraDetectionService
{
    private readonly ILogger<CameraDetectionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationService _notificationService;
    private readonly IScanLogService _scanLog;
    private readonly AppSettings _settings;

    private const string ScanSource = "camera-scan";

    private static readonly Regex HikvisionPattern = new(@"hikvision|hik|DS-\d", RegexOptions.IgnoreCase);
    private static readonly Regex DahuaPattern = new(@"dahua|DH-|IPC-", RegexOptions.IgnoreCase);
    private static readonly Regex AxisPattern = new(@"axis|AXIS", RegexOptions.IgnoreCase);
    private static readonly Regex FoscamPattern = new(@"foscam|FI\d", RegexOptions.IgnoreCase);

    public CameraDetectionService(
        ILogger<CameraDetectionService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        INotificationService notificationService,
        IScanLogService scanLog,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _notificationService = notificationService;
        _scanLog = scanLog;
        _settings = settings.Value;
    }

    public async Task<IEnumerable<NetworkCamera>> ScanForCamerasAsync(CancellationToken cancellationToken = default)
    {
        var scanId = $"camera-scan-{DateTime.UtcNow:yyyyMMddHHmmss}";
        _scanLog.StartScan(ScanSource, "Scan des cameras reseau");
        
        var detectedCameras = new List<NetworkCamera>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            var cameraRepo = scope.ServiceProvider.GetRequiredService<ICameraRepository>();

            var devices = (await deviceRepo.GetAllAsync()).Where(d => !string.IsNullOrEmpty(d.IpAddress)).ToList();
            var ports = DefaultCameraCredentials.CommonCameraPorts;
            var totalChecks = devices.Count * ports.Count;
            var currentCheck = 0;

            _scanLog.Log(ScanSource, $"{devices.Count} appareils a scanner sur {ports.Count} ports", ScanLogLevel.Info);
            _scanLog.Log(ScanSource, $"Ports: {string.Join(", ", ports)}", ScanLogLevel.Debug);

            foreach (var device in devices)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _scanLog.Log(ScanSource, "Scan annule par l'utilisateur", ScanLogLevel.Warning);
                    break;
                }

                _scanLog.Log(ScanSource, $"Scan de {device.IpAddress} ({device.Vendor ?? "Unknown"})", ScanLogLevel.Info);

                foreach (var port in ports)
                {
                    currentCheck++;
                    
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        _scanLog.LogProgress(ScanSource, $"Verification {device.IpAddress}:{port}", currentCheck, totalChecks);

                        var camera = await CheckCameraAsync(device.IpAddress!, port);
                        if (camera != null)
                        {
                            camera.DeviceId = device.Id;
                            var saved = await cameraRepo.AddOrUpdateAsync(camera);
                            detectedCameras.Add(saved);

                            var statusIcon = camera.PasswordStatus switch
                            {
                                PasswordStatus.DefaultPassword => "[!]",
                                PasswordStatus.NoPassword => "[!]",
                                PasswordStatus.CustomPassword => "[OK]",
                                _ => "[?]"
                            };

                            _scanLog.Log(ScanSource, 
                                $"{statusIcon} CAMERA DETECTEE: {device.IpAddress}:{port} - {camera.Manufacturer ?? "Inconnue"}", 
                                camera.PasswordStatus == PasswordStatus.DefaultPassword || camera.PasswordStatus == PasswordStatus.NoPassword 
                                    ? ScanLogLevel.Warning 
                                    : ScanLogLevel.Success);

                            if (camera.PasswordStatus == PasswordStatus.DefaultPassword || 
                                camera.PasswordStatus == PasswordStatus.NoPassword)
                            {
                                _scanLog.Log(ScanSource, 
                                    $"   [!] VULNERABLE: Mot de passe par defaut detecte ({camera.DetectedCredentials})", 
                                    ScanLogLevel.Warning);
                                await CreateCameraAlertAsync(camera);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _scanLog.Log(ScanSource, $"   Erreur {device.IpAddress}:{port}: {ex.Message}", ScanLogLevel.Debug);
                    }
                }
            }

            var vulnerableCount = detectedCameras.Count(c => 
                c.PasswordStatus == PasswordStatus.DefaultPassword || 
                c.PasswordStatus == PasswordStatus.NoPassword);

            var summary = $"{detectedCameras.Count} camera(s) trouvee(s), {vulnerableCount} vulnerable(s)";
            _scanLog.EndScan(ScanSource, true, summary);

            return detectedCameras;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during camera scan");
            _scanLog.EndScan(ScanSource, false, $"Erreur: {ex.Message}");
            throw;
        }
    }

    public async Task<NetworkCamera?> CheckCameraAsync(string ip, int port)
    {
        // Check if port is open
        if (!await IsPortOpenAsync(ip, port))
            return null;

        var camera = new NetworkCamera
        {
            IpAddress = ip,
            Port = port,
            Status = CameraStatus.Unknown,
            PasswordStatus = PasswordStatus.Unknown
        };

        // Try to detect camera type
        var detectionResult = await DetectCameraTypeAsync(ip, port);
        if (detectionResult == null)
            return null;

        camera.Manufacturer = detectionResult.Manufacturer;
        camera.Model = detectionResult.Model;
        camera.Status = CameraStatus.Online;

        // Test default credentials
        var credResult = await TestDefaultCredentialsAsync(ip, port);
        if (credResult.Success)
        {
            camera.IsAccessible = true;
            camera.Status = CameraStatus.Authenticated;
            camera.PasswordStatus = credResult.IsDefaultPassword ? PasswordStatus.DefaultPassword : PasswordStatus.CustomPassword;
            camera.DetectedCredentials = $"{credResult.Username}:{credResult.Password}";
            camera.StreamUrl = credResult.StreamUrl;
            camera.SnapshotUrl = credResult.SnapshotUrl;
        }
        else if (credResult.ErrorMessage?.Contains("401") == true || 
                 credResult.ErrorMessage?.Contains("403") == true)
        {
            camera.PasswordStatus = PasswordStatus.PasswordRequired;
            camera.Status = CameraStatus.RequiresAuth;
        }

        return camera;
    }

    private async Task<bool> IsPortOpenAsync(string ip, int port, int timeout = 2000)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(timeout);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
            {
                return client.Connected;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<CameraCheckResult?> DetectCameraTypeAsync(string ip, int port)
    {
        var result = new CameraCheckResult();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var urls = new[] { $"http://{ip}:{port}/", $"http://{ip}:{port}/doc/page/login.asp" };

            foreach (var url in urls)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();
                    var headers = response.Headers.ToString() + response.Content.Headers.ToString();

                    // Detect manufacturer
                    if (HikvisionPattern.IsMatch(content) || HikvisionPattern.IsMatch(headers))
                    {
                        result.Manufacturer = "Hikvision";
                        result.SnapshotUrl = $"http://{ip}:{port}/ISAPI/Streaming/channels/101/picture";
                        result.StreamUrl = $"rtsp://{ip}:554/Streaming/Channels/101";
                    }
                    else if (DahuaPattern.IsMatch(content) || DahuaPattern.IsMatch(headers))
                    {
                        result.Manufacturer = "Dahua";
                        result.SnapshotUrl = $"http://{ip}:{port}/cgi-bin/snapshot.cgi";
                        result.StreamUrl = $"rtsp://{ip}:554/cam/realmonitor?channel=1&subtype=0";
                    }
                    else if (AxisPattern.IsMatch(content) || AxisPattern.IsMatch(headers))
                    {
                        result.Manufacturer = "Axis";
                        result.SnapshotUrl = $"http://{ip}:{port}/axis-cgi/jpg/image.cgi";
                        result.StreamUrl = $"rtsp://{ip}:554/axis-media/media.amp";
                    }
                    else if (FoscamPattern.IsMatch(content) || FoscamPattern.IsMatch(headers))
                    {
                        result.Manufacturer = "Foscam";
                        result.SnapshotUrl = $"http://{ip}:{port}/cgi-bin/CGIProxy.fcgi?cmd=snapPicture2";
                        result.StreamUrl = $"rtsp://{ip}:554/videoMain";
                    }
                    else if (content.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("video", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("stream", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("RTSP", StringComparison.OrdinalIgnoreCase) ||
                             headers.Contains("camera", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Manufacturer = "Generic";
                        result.SnapshotUrl = $"http://{ip}:{port}/snapshot.jpg";
                        result.StreamUrl = $"rtsp://{ip}:554/stream1";
                    }
                    else
                    {
                        continue;
                    }

                    result.Success = true;
                    return result;
                }
                catch
                {
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error detecting camera type for {Ip}:{Port}", ip, port);
        }

        return null;
    }

    public async Task<CameraCheckResult> TestDefaultCredentialsAsync(string ip, int port)
    {
        var result = new CameraCheckResult();

        // Get detected manufacturer for specific credentials
        var detection = await DetectCameraTypeAsync(ip, port);
        var manufacturer = detection?.Manufacturer ?? "Generic";

        var credentialsToTry = new List<(string, string)>();
        
        if (DefaultCameraCredentials.ByManufacturer.TryGetValue(manufacturer, out var specificCreds))
        {
            credentialsToTry.AddRange(specificCreds);
        }
        credentialsToTry.AddRange(DefaultCameraCredentials.ByManufacturer["Generic"]);

        foreach (var (username, password) in credentialsToTry.Distinct())
        {
            var testResult = await TestCredentialsAsync(ip, port, username, password);
            if (testResult.Success)
            {
                testResult.IsDefaultPassword = true;
                testResult.Manufacturer = manufacturer;
                return testResult;
            }
        }

        result.Success = false;
        result.ErrorMessage = "No valid credentials found";
        return result;
    }

    public async Task<CameraCheckResult> TestCredentialsAsync(string ip, int port, string username, string password)
    {
        var result = new CameraCheckResult
        {
            Username = username,
            Password = password
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Add Basic authentication
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            // URLs to test for authentication
            var testUrls = new[]
            {
                $"http://{ip}:{port}/",
                $"http://{ip}:{port}/cgi-bin/snapshot.cgi",
                $"http://{ip}:{port}/ISAPI/System/status",
                $"http://{ip}:{port}/axis-cgi/jpg/image.cgi"
            };

            foreach (var url in testUrls)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        result.Success = true;
                        
                        // Try to find snapshot URL
                        if (url.Contains("snapshot") || url.Contains("image"))
                        {
                            result.SnapshotUrl = url;
                        }
                        
                        return result;
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        result.ErrorMessage = "401 Unauthorized";
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<string?> GetSnapshotAsync(int cameraId)
    {
        using var scope = _scopeFactory.CreateScope();
        var cameraRepo = scope.ServiceProvider.GetRequiredService<ICameraRepository>();
        
        var camera = await cameraRepo.GetByIdAsync(cameraId);
        if (camera == null || string.IsNullOrEmpty(camera.SnapshotUrl))
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            if (!string.IsNullOrEmpty(camera.DetectedCredentials))
            {
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes(camera.DetectedCredentials));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            }

            var response = await client.GetAsync(camera.SnapshotUrl);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return Convert.ToBase64String(bytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting snapshot for camera {Id}", cameraId);
        }

        return null;
    }

    private async Task CreateCameraAlertAsync(NetworkCamera camera)
    {
        using var scope = _scopeFactory.CreateScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

        var alert = new NetworkAlert
        {
            Type = AlertType.UnauthorizedAccess,
            Severity = AlertSeverity.Critical,
            Title = "Camera with default password detected!",
            Message = $"Camera {camera.Manufacturer ?? "unknown"} at {camera.IpAddress}:{camera.Port} uses default credentials ({camera.DetectedCredentials}). Change password immediately!",
            SourceIp = camera.IpAddress,
            DeviceId = camera.DeviceId
        };

        await alertRepo.AddAsync(alert);
        await _notificationService.SendAlertAsync(alert);
    }
}
