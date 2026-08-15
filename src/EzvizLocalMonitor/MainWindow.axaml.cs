using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using OpenCvSharp;

namespace EzvizLocalMonitor;

public partial class MainWindow : Avalonia.Controls.Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly EventStore _eventStore = new();
    private readonly EventLogMaintenanceService _eventMaintenance;
    private readonly AlertDispatcher _alerts = new();
    private readonly LanCameraDiscovery _lanDiscovery = new();
    private AppSettings _settings = new();
    private MonitorCoordinator? _coordinator;
    private Image[] _previews = Array.Empty<Image>();
    private TextBlock[] _cameraStatuses = Array.Empty<TextBlock>();
    private TextBlock[] _cameraOverlayLabels = Array.Empty<TextBlock>();
    private Button[] _cameraFocusButtons = Array.Empty<Button>();
    private Border[] _cameraTiles = Array.Empty<Border>();
    private readonly AppUpdateService _updateService = new();
    private readonly WatchdogService _watchdog = new();
    private readonly object _previewSync = new();
    private readonly Dictionary<Guid, Mat> _pendingPreviewMats = new();
    private readonly Dictionary<Guid, Bitmap> _pendingPreviewBitmaps = new();
    private readonly HashSet<Guid> _previewEncodeScheduled = new();
    private readonly HashSet<Guid> _previewDispatchScheduled = new();
    private readonly Dictionary<Guid, string> _cameraRuntimeStatuses = new();
    private readonly Dictionary<Guid, CameraRuntimeSnapshot> _cameraRuntimeSnapshots = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastPreviewAt = new();
    private readonly DispatcherTimer _systemStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<DetectionEvent> _eventCache = Array.Empty<DetectionEvent>();
    private TimeSpan _lastProcessCpu;
    private DateTimeOffset _lastProcessCpuAt;
    private int _previewFramesApplied;
    private int _lastPreviewFramesApplied;
    private DateTimeOffset _lastPreviewMetricAt;
    private bool _loadingSettings;
    private bool _uiInitialized;
    private Bitmap? _eventDetailBitmap;
    private bool _updateCheckStarted;
    private bool _exitRequested;
    private bool _shutdownStarted;
    private bool _liveViewEnabled = true;
    private DetectionEvent? _lastRecordedEvent;
    private bool? _lastScheduleAllowed;
    private int? _lastScheduleProfile;
    private int _scheduleTransition;

    public event Action<string>? TrayStatusChanged;

    public MainWindow()
    {
        InitializeComponent();
        _previews = new[] { PreviewOne, PreviewTwo, PreviewThree, PreviewFour };
        _cameraStatuses = new[] { CameraOneStatus, CameraTwoStatus, CameraThreeStatus, CameraFourStatus };
        _cameraOverlayLabels = new[] { CameraOneOverlay, CameraTwoOverlay, CameraThreeOverlay, CameraFourOverlay };
        _cameraFocusButtons = new[] { CameraOneFocusButton, CameraTwoFocusButton, CameraThreeFocusButton, CameraFourFocusButton };
        _cameraTiles = new[] { CameraTileOne, CameraTileTwo, CameraTileThree, CameraTileFour };
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "không xác định";
        VersionText.Text = $"Bản {version} · Nhận diện người cục bộ · Ảnh sự kiện chỉ rời LAN khi Telegram/Zalo được bật.";
        AuthorVersionText.Text = $"Bản {version}";
        DataPaths.EnsureCreated();
        _eventStore.Initialize();
        _eventMaintenance = new EventLogMaintenanceService(_eventStore);
        _loadingSettings = true;
        LoadSettings();
        RefreshScheduleGrid();
        ApplyTheme();
        PerformanceProfileCombo.SelectedIndex = Math.Clamp(_settings.PerformanceProfile, 0, 3);
        PreviewFitCombo.SelectedIndex = Math.Clamp(_settings.PreviewFitMode, 0, 1);
        DashboardViewModeCombo.SelectedIndex = Math.Clamp(_settings.DashboardViewMode, 0, 1);
        _loadingSettings = false;
        ApplyLayoutMode(_settings.DashboardLayoutMode, false);
        ApplyPreviewFit();
        ApplyDashboardViewMode();
        RefreshEvents();
        ConfidenceSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "Value") ConfidenceText.Text = $"{ConfidenceSlider.Value:P0}";
        };
        Opened += MainWindow_Opened;
        _systemStatusTimer.Tick += (_, _) =>
        {
            RefreshSystemStatus();
            _ = ApplyScheduleAsync();
        };
        _systemStatusTimer.Start();
        Closed += (_, _) => _systemStatusTimer.Stop();
        _uiInitialized = true;
    }

    private void LoadSettings()
    {
        try { _settings = _settingsStore.Load(); }
        catch (Exception ex)
        {
            _settings = new AppSettings();
            SetStatus($"Không mở được cấu hình: {ex.Message}");
        }

        RefreshCameraList();
        RefreshScheduleGrid();
        ThemeCombo.SelectedIndex = ThemeIndex(_settings.ThemeName, _settings.DarkTheme);
        TelegramEnabledCheck.IsChecked = _settings.Alerts.TelegramEnabled;
        TelegramTokenText.Text = _settings.Alerts.TelegramBotToken;
        TelegramChatText.Text = _settings.Alerts.TelegramChatId;
        ZaloEnabledCheck.IsChecked = _settings.Alerts.ZaloEnabled;
        ZaloTokenText.Text = _settings.Alerts.ZaloBotToken;
        ZaloChatText.Text = _settings.Alerts.ZaloChatId;
        ZaloImageRelayEnabledCheck.IsChecked = _settings.Alerts.ZaloImageRelayEnabled;
        ZaloAllowImageRelayCheck.IsChecked = _settings.Alerts.AllowImageRelayOutsideLan;
        ZaloImageRelayUrlText.Text = _settings.Alerts.ZaloImageRelayUrl;
        ZaloImageRelayApiKeyText.Text = _settings.Alerts.ZaloImageRelayApiKey;
        AiEnabledCheck.IsChecked = _settings.Ai.Enabled;
        AiBaseUrlText.Text = _settings.Ai.BaseUrl;
        AiModelText.Text = _settings.Ai.Model;
        AiApiKeyText.Text = _settings.Ai.ApiKey;
        AiTimeoutText.Text = _settings.Ai.TimeoutSeconds.ToString();
                AiRequireConfirmationCheck.IsChecked = _settings.Ai.RequireConfirmationBeforeAlert;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        WatchdogEnabledCheck.IsChecked = _settings.WatchdogEnabled;
        LoggingEnabledCheck.IsChecked = _settings.LoggingEnabled;
        AlertLoggingEnabledCheck.IsChecked = _settings.AlertLoggingEnabled;
        AutoUpdateEnabledCheck.IsChecked = _settings.AutoUpdateEnabled;
        AppLogger.Configure(_settings.LoggingEnabled, _settings.AlertLoggingEnabled);
        ZaloDiagnostics.Configure(_settings.AlertLoggingEnabled);
        RuntimeInfoText.Text =
 $"Hồ sơ: {PerformanceProfileName(_settings.PerformanceProfile)} · {_settings.InferenceFpsPerCamera} lần suy luận/giây/camera · xác nhận {_settings.ConfirmationsRequired}/{_settings.ConfirmationWindow} khung";
        PreviewFitCombo.SelectedIndex = Math.Clamp(_settings.PreviewFitMode, 0, 1);
        DashboardViewModeCombo.SelectedIndex = Math.Clamp(_settings.DashboardViewMode, 0, 1);

        if (_settings.Cameras.Count > 0) CameraList.SelectedIndex = 0;
    }

    private void RefreshCameraList()
    {
        CameraList.ItemsSource = null;
        CameraList.ItemsSource = _settings.Cameras;
        if (_cameraStatuses.Length > 0) RefreshOverviewStatuses();
        RefreshSystemStatus();
    }

    private void RefreshSystemStatus()
    {
        var enabled = _settings.Cameras.Count(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl));
        var active = _settings.Cameras.Count(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl) &&
            _cameraRuntimeStatuses.TryGetValue(x.Id, out var status) &&
            (status.Contains("giám sát", StringComparison.OrdinalIgnoreCase) || status.Contains("ONVIF", StringComparison.OrdinalIgnoreCase)));
        var warning = _cameraRuntimeStatuses.Values.Count(x => x.Contains("lỗi", StringComparison.OrdinalIgnoreCase) || x.Contains("mất", StringComparison.OrdinalIgnoreCase));
        var telegram = _settings.Alerts.TelegramEnabled ? "Telegram bật" : "Telegram tắt";
        var zalo = _settings.Alerts.ZaloEnabled ? "Zalo bật" : "Zalo tắt";
        var systemDetails = $"Hệ thống: {active}/{enabled} camera đang hoạt động · {warning} cảnh báo kết nối · {telegram} · {zalo}";
        SystemHealthText.Text = $"{active}/{enabled} hoạt động";
        ToolTip.SetTip(SystemHealthText, systemDetails);

        var cameraDetails = _settings.Cameras.Count == 0
            ? "Camera: chưa cấu hình"
            : "Camera: " + string.Join(" · ", _settings.Cameras.Take(4).Select(camera =>
            {
                var configured = camera.IsEnabled && !string.IsNullOrWhiteSpace(camera.RtspUrl);
                var status = _cameraRuntimeStatuses.TryGetValue(camera.Id, out var runtime) ? runtime : configured ? "Chờ kết nối" : "Chưa cấu hình";
                var reconnect = _cameraRuntimeSnapshots.TryGetValue(camera.Id, out var snapshot) ? $", reconnect {snapshot.ReconnectCount}" : string.Empty;
                var preview = _lastPreviewAt.TryGetValue(camera.Id, out var last)
                    ? $", frame {Math.Max(0, (DateTimeOffset.Now - last).TotalSeconds):0.0}s trước"
                    : string.Empty;
                return $"{camera.Name}: {status}{reconnect}{preview}";
            }));
        var connectionSummary = enabled == 0 ? "Chưa cấu hình" : warning > 0 ? $"{warning} cảnh báo" : active == enabled ? "Ổn định" : $"{active}/{enabled} hoạt động";
        CameraHealthText.Text = connectionSummary;
        ToolTip.SetTip(CameraHealthText, cameraDetails);

        var alertParts = new List<string>();
        alertParts.Add(_settings.Alerts.TelegramEnabled ? "Telegram đã bật" : "Telegram tắt");
        alertParts.Add(_settings.Alerts.ZaloEnabled ? "Zalo đã bật" : "Zalo tắt");
        var alertDetails = "Cảnh báo: " + string.Join(" · ", alertParts);
        AlertHealthText.Text = _settings.Alerts.TelegramEnabled || _settings.Alerts.ZaloEnabled ? "Kênh đã bật" : "Chưa bật";
        ToolTip.SetTip(AlertHealthText, alertDetails);

        AiHealthText.Text = _settings.Ai.Enabled ? "Đang bật" : "Đang tắt";
        ToolTip.SetTip(AiHealthText, _settings.Ai.Enabled ? $"AI bật: {_settings.Ai.Model}" : "Phân tích AI đang tắt.");

        var lastEventDetails = _lastRecordedEvent is null
            ? "Sự kiện gần nhất: chưa có"
            : $"Sự kiện gần nhất: {_lastRecordedEvent.CameraName} · {_lastRecordedEvent.DetectedAt:HH:mm:ss} · {_lastRecordedEvent.DeliveryStatus}";
        LastEventHealthText.Text = _lastRecordedEvent is null ? "Chưa có" : _lastRecordedEvent.DetectedAt.ToString("HH:mm:ss");
        ToolTip.SetTip(LastEventHealthText, lastEventDetails);
        var process = Process.GetCurrentProcess();
        var now = DateTimeOffset.Now;
        var cpu = 0d;
        if (_lastProcessCpuAt != default)
        {
            var wallSeconds = Math.Max(0.1, (now - _lastProcessCpuAt).TotalSeconds);
            var cpuSeconds = (process.TotalProcessorTime - _lastProcessCpu).TotalSeconds;
            cpu = Math.Clamp(cpuSeconds / (wallSeconds * Environment.ProcessorCount) * 100d, 0d, 100d);
        }
        _lastProcessCpu = process.TotalProcessorTime;
        _lastProcessCpuAt = now;
        var memoryMb = process.WorkingSet64 / 1024d / 1024d;
        var fps = 0d;
        if (_lastPreviewMetricAt != default)
        {
            var metricSeconds = Math.Max(0.1, (now - _lastPreviewMetricAt).TotalSeconds);
            fps = (_previewFramesApplied - _lastPreviewFramesApplied) / metricSeconds;
        }
        _lastPreviewMetricAt = now;
        _lastPreviewFramesApplied = _previewFramesApplied;
        SystemMetricsText.Text = _liveViewEnabled
            ? $"CPU {cpu:0}% · RAM {memoryMb:0} MB · Preview {fps:0.0} FPS"
            : $"CPU {cpu:0}% · RAM {memoryMb:0} MB · Preview tạm dừng (tray)";
    }

    private void SaveSettings()
    {
        _settingsStore.Save(_settings);
        SetStatus("Đã lưu cấu hình cục bộ an toàn");
    }

    private CameraDefinition? SelectedCamera => CameraList.SelectedItem as CameraDefinition;

    private void RefreshScheduleGrid()
    {
        ScheduleGrid.ItemsSource = null;
        ScheduleGrid.ItemsSource = _settings.MonitorSchedules;
    }

    private void AddSchedule_Click(object? sender, RoutedEventArgs e)
    {
        _settings.MonitorSchedules.Add(new MonitorSchedule { Name = $"Lịch {_settings.MonitorSchedules.Count + 1}" });
        RefreshScheduleGrid();
        ScheduleGrid.SelectedIndex = _settings.MonitorSchedules.Count - 1;
    }

    private void RemoveSchedule_Click(object? sender, RoutedEventArgs e)
    {
        if (ScheduleGrid.SelectedItem is not MonitorSchedule selected) { SetStatus("Hãy chọn một lịch để xóa."); return; }
        _settings.MonitorSchedules.Remove(selected);
        RefreshScheduleGrid();
        SaveSettings();
        _lastScheduleAllowed = null;
    }

    private void SaveSchedules_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var schedule in _settings.MonitorSchedules)
        {
            if (!TimeSpan.TryParseExact(schedule.StartTime, new[] { @"hh\:mm", @"h\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out _) ||
                !TimeSpan.TryParseExact(schedule.EndTime, new[] { @"hh\:mm", @"h\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out _) ||
                schedule.PerformanceProfile is < 0 or > 3)
            {
                SetStatus($"Lịch “{schedule.Name}” chưa hợp lệ: giờ phải HH:mm và hồ sơ từ 0 đến 3.");
                return;
            }
        }
        SaveSettings();
        _lastScheduleAllowed = null;
        SetStatus(_settings.MonitorSchedules.Count == 0 ? "Đã lưu: không có lịch, giám sát liên tục." : "Đã lưu lịch giám sát.");
    }

    private async Task ApplyScheduleAsync()
    {
        if (!_uiInitialized || _shutdownStarted || Interlocked.Exchange(ref _scheduleTransition, 1) != 0) return;
        try
        {
            var allowed = MonitorScheduleService.IsMonitoringAllowed(_settings);
            var profile = MonitorScheduleService.ActivePerformanceProfile(_settings);
            if (_lastScheduleAllowed == allowed && _lastScheduleProfile == profile) return;
            _lastScheduleAllowed = allowed;
            _lastScheduleProfile = profile;

            if (!allowed)
            {
                if (_coordinator is not null)
                {
                    SetStatus("Ngoài lịch giám sát — đang tạm dừng camera.");
                    await StopMonitoringAsync(TimeSpan.FromSeconds(5));
                }
                return;
            }

            if (profile is int scheduledProfile && scheduledProfile != _settings.PerformanceProfile)
            {
                _settings.PerformanceProfile = scheduledProfile;
                _settings.InferenceFpsPerCamera = scheduledProfile switch { 0 => 1, 2 => 3, 3 => 1, _ => 2 };
                PerformanceProfileCombo.SelectedIndex = scheduledProfile;
                if (_coordinator is not null)
                {
                    await StopMonitoringAsync(TimeSpan.FromSeconds(5));
                    await StartMonitoringAsync(false);
                }
            }
            else if (_coordinator is null && _settings.Cameras.Any(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl)))
            {
                await StartMonitoringAsync(false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "schedule transition failed", ex);
        }
        finally
        {
            Volatile.Write(ref _scheduleTransition, 0);
        }
    }

    private void CameraList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = SelectedCamera;
        if (item is null) return;
        CameraNameText.Text = item.Name;
        CameraRtspText.Text = item.RtspUrl;
        ConfidenceSlider.Value = item.ConfidenceThreshold;
        ConfidenceText.Text = $"{item.ConfidenceThreshold:P0}";
        CooldownText.Text = item.CooldownSeconds.ToString();
        MinPresenceText.Text = item.MinPresenceSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        CameraEnabledCheck.IsChecked = item.IsEnabled;
    }

    private void AddCamera_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Cameras.Count >= 4)
        {
            SetStatus("Tối đa 4 camera cho bố cục 4 màn hình.");
            return;
        }
        var camera = new CameraDefinition { Name = _settings.Cameras.Count == 0 ? "C6N" : "H8C" };
        _settings.Cameras.Add(camera);
        RefreshCameraList();
        CameraList.SelectedItem = camera;
    }

    private async void FindCameras_Click(object? sender, RoutedEventArgs e)
    {
        DiscoveryList.ItemsSource = null;
        VerificationCodeText.Text = string.Empty;
        DiscoveryStatusText.Text = "Đang quét ONVIF và RTSP trong LAN...";
        try
        {
            var progress = new Progress<string>(message => DiscoveryStatusText.Text = message);
            var found = await _lanDiscovery.DiscoverAsync(progress);
            DiscoveryList.ItemsSource = found;
            if (found.Count > 0)
            {
                DiscoveryList.SelectedIndex = 0;
                DiscoveryStatusText.Text = $"Tìm thấy {found.Count} ứng viên. Chọn camera và nhập mã xác thực trên nhãn.";
            }
            else
            {
                DiscoveryStatusText.Text = "Không tìm thấy camera. Kiểm tra PC/camera cùng Wi-Fi hoặc VLAN, sau đó bật Local Service/RTSP; bạn vẫn có thể nhập RTSP thủ công.";
            }
        }
        catch (Exception ex)
        {
            DiscoveryStatusText.Text = $"Không thể quét LAN: {ex.Message}";
        }
    }

    private async void AddSelectedDiscovery_Click(object? sender, RoutedEventArgs e)
    {
        var found = DiscoveryList.SelectedItem as DiscoveredCamera;
        var verificationCode = VerificationCodeText.Text?.Trim() ?? string.Empty;
        if (found is null) { DiscoveryStatusText.Text = "Hãy chọn một camera đã tìm thấy trước."; return; }
        if (verificationCode.Length < 6) { DiscoveryStatusText.Text = "Nhập mã xác thực trên nhãn camera (thường gồm 6 ký tự)."; return; }
        if (_settings.Cameras.Count >= 4) { DiscoveryStatusText.Text = "Đã đủ 4 camera trong danh sách."; return; }
        if (_settings.Cameras.Any(x => x.RtspUrl.Contains(found.IpAddress, StringComparison.OrdinalIgnoreCase)))
        {
            DiscoveryStatusText.Text = "Camera này đã có trong danh sách.";
            return;
        }

        var rtspUrl = $"rtsp://admin:{Uri.EscapeDataString(verificationCode)}@{found.IpAddress}:{found.RtspPort}/ch1/main";
        DiscoveryStatusText.Text = "Đang xác thực mã thiết bị và kiểm tra RTSP...";
        var connected = await Task.Run(() => TryReadRtspFrame(rtspUrl));
        if (!connected)
        {
            DiscoveryStatusText.Text = "Không xác thực được luồng RTSP. Kiểm tra mã, Local Service/RTSP, firmware hoặc thử RTSP thủ công.";
            return;
        }

        var camera = new CameraDefinition
        {
            Name = $"EZVIZ {found.IpAddress}",
            RtspUrl = rtspUrl,
            OnvifServiceUrl = found.OnvifServiceUrl,
            IsEnabled = true
        };
        _settings.Cameras.Add(camera);
        RefreshCameraList();
        CameraList.SelectedItem = camera;
        VerificationCodeText.Text = string.Empty;
        SaveSettings();
        DiscoveryStatusText.Text = "Đã thêm camera và bảo vệ cấu hình bằng Windows DPAPI.";
    }

    private async void RemoveCamera_Click(object? sender, RoutedEventArgs e)
    {
        var camera = SelectedCamera;
        if (camera is null)
        {
            SetStatus("Hãy chọn camera cần xóa.");
            return;
        }

        var confirmed = await new CleanupConfirmWindow(
            "Xóa camera",
            $"Bạn có chắc muốn xóa camera \"{camera.Name}\" khỏi danh sách? Thao tác này chỉ xóa cấu hình camera khỏi ứng dụng, không xóa thiết bị trong tài khoản EZVIZ.").ShowDialog<bool>(this);
        if (confirmed != true)
        {
            SetStatus("Đã hủy xóa camera.");
            return;
        }

        var wasRunning = _coordinator is not null;
        if (wasRunning) await StopMonitoringAsync();
        _settings.Cameras.Remove(camera);
        ClearRuntimeStateForConfigurationChange();
        RefreshCameraList();
        SaveSettings();
        if (wasRunning) await StartMonitoringAsync(false);
        SetStatus($"Đã xóa camera {camera.Name} khỏi danh sách.");
    }

    private void ClearRuntimeStateForConfigurationChange()
    {
        _cameraRuntimeStatuses.Clear();
        _cameraRuntimeSnapshots.Clear();
        _lastPreviewAt.Clear();
        lock (_previewSync)
        {
            foreach (var pendingMat in _pendingPreviewMats.Values) pendingMat.Dispose();
            foreach (var pendingBitmap in _pendingPreviewBitmaps.Values) pendingBitmap.Dispose();
            _pendingPreviewMats.Clear();
            _pendingPreviewBitmaps.Clear();
            _previewEncodeScheduled.Clear();
            _previewDispatchScheduled.Clear();
        }
        foreach (var preview in _previews)
        {
            (preview.Source as IDisposable)?.Dispose();
            preview.Source = null;
        }
    }

    private void SaveCamera_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryApplySelectedCameraFromUi(out var error))
        {
            SetStatus(error);
            return;
        }
        RefreshCameraList();
        SaveSettings();
    }

    private bool TryApplySelectedCameraFromUi(out string error)
    {
        var camera = SelectedCamera;
        if (camera is null)
        {
            error = "Hãy thêm hoặc chọn camera trước khi lưu.";
            return false;
        }
        if (!int.TryParse(CooldownText.Text, out var cooldown) || cooldown is < 5 or > 3600)
        {
            error = "Khoảng im lặng phải là số từ 5 đến 3600 giây.";
            return false;
        }
        if (!double.TryParse(MinPresenceText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minPresence) || minPresence is < 0 or > 30)
        {
            error = "Thời gian xuất hiện tối thiểu phải từ 0 đến 30 giây, ví dụ 1.5.";
            return false;
        }
        camera.Name = string.IsNullOrWhiteSpace(CameraNameText.Text) ? "Camera" : CameraNameText.Text.Trim();
        camera.RtspUrl = CameraRtspText.Text?.Trim() ?? string.Empty;
        camera.ConfidenceThreshold = ConfidenceSlider.Value;
        camera.CooldownSeconds = cooldown;
        camera.MinPresenceSeconds = minPresence;
        camera.IsEnabled = CameraEnabledCheck.IsChecked == true;
        error = string.Empty;
        return true;
    }

    private async void TestRtsp_Click(object? sender, RoutedEventArgs e)
    {
        var url = CameraRtspText.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) { SetStatus("Nhập RTSP URL trước khi kiểm tra."); return; }
        SetStatus("Đang kiểm tra RTSP...");
        var connected = await Task.Run(() => TryReadRtspFrame(url));
        SetStatus(connected ? "RTSP kết nối thành công." : "Không đọc được RTSP. Kiểm tra IP, mã xác thực, cùng mạng LAN và Local Service/RTSP.");
    }

    private static bool TryReadRtspFrame(string url)
    {
        using var capture = new VideoCapture(url, VideoCaptureAPIs.FFMPEG);
        using var frame = new Mat();
        return capture.IsOpened() && capture.Read(frame) && !frame.Empty();
    }

    private static string PerformanceProfileName(int profile) => profile switch
    {
        0 => "Tiết kiệm CPU",
        2 => "Phản hồi nhanh",
        3 => "Ưu tiên ONVIF",
        _ => "Cân bằng"
    };

    private async void PerformanceProfile_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || _loadingSettings || sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        _settings.PerformanceProfile = combo.SelectedIndex;
        _settings.InferenceFpsPerCamera = _settings.PerformanceProfile switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            _ => 2
        };
        SaveSettings();
        RuntimeInfoText.Text = $"Hồ sơ: {PerformanceProfileName(_settings.PerformanceProfile)} · {_settings.InferenceFpsPerCamera} lần suy luận/giây/camera";
        if (_coordinator is not null) await StartMonitoringAsync(false);
    }

    private void SaveAlerts_Click(object? sender, RoutedEventArgs e)
    {
        _settings.Alerts.TelegramEnabled = TelegramEnabledCheck.IsChecked == true;
        _settings.Alerts.TelegramBotToken = TelegramTokenText.Text?.Trim() ?? string.Empty;
        _settings.Alerts.TelegramChatId = TelegramChatText.Text?.Trim() ?? string.Empty;
        _settings.Alerts.ZaloEnabled = ZaloEnabledCheck.IsChecked == true;
        _settings.Alerts.ZaloBotToken = ZaloTokenText.Text?.Trim() ?? string.Empty;
        _settings.Alerts.ZaloChatId = ZaloChatText.Text?.Trim() ?? string.Empty;
        _settings.Alerts.ZaloImageRelayEnabled = ZaloImageRelayEnabledCheck.IsChecked == true;
        _settings.Alerts.AllowImageRelayOutsideLan = ZaloAllowImageRelayCheck.IsChecked == true;
        _settings.Alerts.ZaloImageRelayUrl = ZaloImageRelayUrlText.Text?.Trim() ?? string.Empty;
        _settings.Alerts.ZaloImageRelayApiKey = ZaloImageRelayApiKeyText.Text?.Trim() ?? string.Empty;
        _settings.Ai.Enabled = AiEnabledCheck.IsChecked == true;
        _settings.Ai.BaseUrl = AiBaseUrlText.Text?.Trim() ?? string.Empty;
        _settings.Ai.Model = AiModelText.Text?.Trim() ?? string.Empty;
        _settings.Ai.ApiKey = AiApiKeyText.Text?.Trim() ?? string.Empty;
        _settings.Ai.TimeoutSeconds = int.TryParse(AiTimeoutText.Text, out var timeout) ? Math.Clamp(timeout, 5, 90) : 25;
        _settings.Ai.RequireConfirmationBeforeAlert = AiRequireConfirmationCheck.IsChecked == true;
        SaveSettings();
        RefreshSystemStatus();
    }

    private void SaveSystemSettings_Click(object? sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.WatchdogEnabled = WatchdogEnabledCheck.IsChecked == true;
        _settings.LoggingEnabled = LoggingEnabledCheck.IsChecked == true;
        _settings.AlertLoggingEnabled = AlertLoggingEnabledCheck.IsChecked == true;
        _settings.AutoUpdateEnabled = AutoUpdateEnabledCheck.IsChecked == true;
        AppLogger.Configure(_settings.LoggingEnabled, _settings.AlertLoggingEnabled);
        ZaloDiagnostics.Configure(_settings.AlertLoggingEnabled);
        WindowsStartupService.Apply(_settings.StartWithWindows);
        if (_settings.WatchdogEnabled) _watchdog.Start(Program.LaunchInTray); else _watchdog.Stop();
        SaveSettings();
        RefreshSystemStatus();
        SetStatus("Đã lưu cài đặt hệ thống.");
    }

    private void ValidateAlerts_Click(object? sender, RoutedEventArgs e)
    {
        SaveAlerts_Click(sender, e);
        var checks = new List<string>();
        if (!_settings.Alerts.TelegramEnabled && !_settings.Alerts.ZaloEnabled)
            checks.Add("Chưa bật Telegram hoặc Zalo");
        if (_settings.Alerts.TelegramEnabled)
        {
            if (string.IsNullOrWhiteSpace(_settings.Alerts.TelegramBotToken)) checks.Add("Telegram thiếu Bot Token");
            if (string.IsNullOrWhiteSpace(_settings.Alerts.TelegramChatId)) checks.Add("Telegram thiếu Chat ID");
        }
        if (_settings.Alerts.ZaloEnabled)
        {
            if (string.IsNullOrWhiteSpace(_settings.Alerts.ZaloBotToken)) checks.Add("Zalo thiếu Bot Token");
            if (string.IsNullOrWhiteSpace(_settings.Alerts.ZaloChatId)) checks.Add("Zalo thiếu Chat ID");
            checks.Add("Zalo ảnh LAN-only cần URL HTTPS công khai; tin chữ vẫn có thể gửi");
        }
        if (_settings.Ai.Enabled)
        {
            if (!Uri.TryCreate(_settings.Ai.BaseUrl, UriKind.Absolute, out var aiUri) || aiUri.Scheme is not ("http" or "https")) checks.Add("AI Base URL không hợp lệ");
            if (string.IsNullOrWhiteSpace(_settings.Ai.Model)) checks.Add("AI thiếu Model");
            if (string.IsNullOrWhiteSpace(_settings.Ai.ApiKey)) checks.Add("AI thiếu API key");
        }
        ConfigurationCheckText.Text = checks.Count == 0
            ? "Cấu hình local hợp lệ; các kênh đã bật sẵn sàng để Gửi thử thủ công."
            : string.Join(" · ", checks);
        SetStatus("Đã kiểm tra cấu hình; không tự gửi tin nhắn.");
    }

    private async void TestTelegram_Click(object? sender, RoutedEventArgs e)
    {
        SaveAlerts_Click(sender, e);
        try { SetStatus(await _alerts.TestAsync(_settings.Alerts, "telegram")); }
        catch (Exception ex) { SetStatus($"Telegram: {ex.Message}"); }
    }

    private async void TestZalo_Click(object? sender, RoutedEventArgs e)
    {
        SaveAlerts_Click(sender, e);
        try { SetStatus(await _alerts.TestAsync(_settings.Alerts, "zalo")); }
        catch (Exception ex) { SetStatus($"Zalo: {ex.Message}"); }
    }

    private async void StartMonitoring_Click(object? sender, RoutedEventArgs e) => await StartMonitoringAsync(true);

    private async Task StartMonitoringAsync(bool saveSettings)
    {
        if (saveSettings) SaveSettings();
        if (!_settings.Cameras.Any(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl)))
        {
            SetStatus("Chưa có camera bật hợp lệ; thêm camera trong tab Camera để tự động bắt đầu giám sát.");
            return;
        }
        if (_coordinator is not null) await StopMonitoringAsync();

        try
        {
            _coordinator = new MonitorCoordinator(_eventStore, _alerts);
            _coordinator.CameraStatusChanged += UpdateCameraStatus;
            _coordinator.CameraRuntimeChanged += UpdateCameraRuntime;
            _coordinator.PreviewReady += UpdatePreview;
            _coordinator.EventRecorded += HandleEventRecorded;
            await _coordinator.StartAsync(_settings);
            _coordinator.SetPreviewEnabled(_liveViewEnabled);
            SetStatus(_liveViewEnabled
                ? "Đang giám sát cục bộ — ONVIF event hoặc YOLO fallback"
                : "Đang chạy nền — giám sát vẫn hoạt động, live view tạm dừng");
        }
        catch (Exception ex)
        {
            _coordinator = null;
            SetStatus($"Không thể khởi động: {ex.Message}");
        }
    }

    private async void StopMonitoring_Click(object? sender, RoutedEventArgs e) => await StopMonitoringAsync();

    private async Task StopMonitoringAsync(TimeSpan? timeout = null)
    {
        var coordinator = Interlocked.Exchange(ref _coordinator, null);
        if (coordinator is null) return;

        try
        {
            var disposeTask = coordinator.DisposeAsync().AsTask();
            await disposeTask.WaitAsync(timeout ?? TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException ex)
        {
            StartupDiagnostics.Write("StopMonitoringAsync timeout; process will continue cleanup in background", ex);
        }
        catch (OperationCanceledException)
        {
            // Normal when camera/ONVIF loops observe cancellation.
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Write("StopMonitoringAsync", ex);
        }
        finally
        {
            SetStatus("Đã dừng giám sát");
            foreach (var status in _cameraStatuses) status.Text = "Đã dừng";
        }
    }

    private void FocusSelectedCamera_Click(object? sender, RoutedEventArgs e)
    {
        ApplyLayoutMode(1, true);
        var selected = SelectedCamera;
        SetStatus(selected is null ? "Đã phóng to camera đầu tiên." : $"Đã phóng to {selected.Name}. Bấm 2/4 màn hình để quay lại.");
    }

    private void Theme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || _loadingSettings || ThemeCombo.SelectedIndex < 0) return;
        _settings.ThemeName = ThemeNames[Math.Clamp(ThemeCombo.SelectedIndex, 0, ThemeNames.Length - 1)];
        if (string.Equals(_settings.ThemeName, "Dark/Light", StringComparison.OrdinalIgnoreCase))
            _settings.DarkTheme = false;
        ApplyTheme();
        SaveSettings();
        SetStatus($"Đã chọn theme {_settings.ThemeName}.");
    }

    private void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        _settings.ThemeName = "Dark/Light";
        _settings.DarkTheme = !_settings.DarkTheme;
        _loadingSettings = true;
        ThemeCombo.SelectedIndex = ThemeIndex(_settings.ThemeName, _settings.DarkTheme);
        _loadingSettings = false;
        ApplyTheme();
        SaveSettings();
    }

    private static readonly string[] ThemeNames = { "Orchid", "Ocean", "Midnight", "Lavender", "Crimson", "Dark/Light" };

    private static int ThemeIndex(string? name, bool darkTheme)
    {
        if (string.Equals(name, "Dark/Light", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name)) return 5;
        var index = Array.FindIndex(ThemeNames, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : (darkTheme ? 5 : 5);
    }

    private void ApplyTheme()
    {
        var theme = string.IsNullOrWhiteSpace(_settings.ThemeName) ? "Dark/Light" : _settings.ThemeName;
        var dark = string.Equals(theme, "Midnight", StringComparison.OrdinalIgnoreCase) ||
                   (string.Equals(theme, "Dark/Light", StringComparison.OrdinalIgnoreCase) && _settings.DarkTheme);
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var palette = theme.ToLowerInvariant() switch
        {
            "orchid" => new ThemePalette("#FFF7FD", "#6D2A68", "#FFFFFF", "#FFF0FA", "#FFFFFF", "#D8B5D2", "#4A1942", "#74556F", "#FBE7F5", "#E6B7D8", "#E8B566", "#805B12", "#F8EEF6", "#1F1630", "#1B122A", "#3B1F4A", "#6D2A68", "#FFFFFF", "#4A1942", "#8E3B86", "#4A1942", "#FFFFFF", "#1F1630", "#74556F", "#6D2A68"),
            "ocean" => new ThemePalette("#F1FAFF", "#075985", "#FFFFFF", "#E0F2FE", "#FFFFFF", "#B8D6E5", "#0C4A6E", "#4B6475", "#E6F6FF", "#A8D8EF", "#F0C36D", "#805B12", "#F0F7FA", "#102A43", "#102A43", "#0B3B5C", "#075985", "#FFFFFF", "#0C4A6E", "#0E7490", "#063B57", "#FFFFFF", "#102A43", "#24516D", "#075985"),
            "midnight" => new ThemePalette("#111827", "#172554", "#FFFFFF", "#BFDBFE", "#1F2937", "#475569", "#BFDBFE", "#CBD5E1", "#1E3A5F", "#3B82B6", "#B88A3B", "#FDE68A", "#374151", "#F8FAFC", "#F8FAFC", "#FFFFFF", "#2563EB", "#FFFFFF", "#1D4ED8", "#3B82F6", "#1D4ED8", "#111827", "#F8FAFC", "#94A3B8", "#60A5FA"),
            "lavender" => new ThemePalette("#FAF8FF", "#5B4B8A", "#FFFFFF", "#EDE9FE", "#FFFFFF", "#D6CCF4", "#44337A", "#665F78", "#F2EEFF", "#C9BDF2", "#E8B566", "#805B12", "#F4F1FB", "#241A3A", "#241A3A", "#372568", "#5B4B8A", "#FFFFFF", "#44337A", "#7357B2", "#44337A", "#FFFFFF", "#241A3A", "#665F78", "#5B4B8A"),
            "crimson" => new ThemePalette("#FFF8F8", "#8F1D3D", "#FFFFFF", "#FFE4E6", "#FFFFFF", "#F0B7BF", "#7F1D1D", "#735B63", "#FFF0F1", "#F0B7BF", "#E8B566", "#805B12", "#FBF0F1", "#32131D", "#32131D", "#6B142C", "#8F1D3D", "#FFFFFF", "#6B142C", "#B3264E", "#6B142C", "#FFFFFF", "#32131D", "#735B63", "#8F1D3D"),
            _ => new ThemePalette(dark ? "#111827" : "#F5F7FA", dark ? "#093B5A" : "#093B5A", dark ? "#FFFFFF" : "#FFFFFF", dark ? "#BFDBFE" : "#D8EDF8", dark ? "#1F2937" : "#FFFFFF", dark ? "#475569" : "#D9E1E8", dark ? "#BFDBFE" : "#093B5A", dark ? "#CBD5E1" : "#52606D", dark ? "#17324D" : "#EAF4F9", dark ? "#315A7D" : "#B8D6E5", dark ? "#B88A3B" : "#F0C36D", dark ? "#FDE68A" : "#805B12", dark ? "#374151" : "#F4F7F9", dark ? "#F8FAFC" : "#14202B", dark ? "#F8FAFC" : "#14202B", dark ? "#FFFFFF" : "#17324D", dark ? "#2563EB" : "#0B4F71", "#FFFFFF", dark ? "#1D4ED8" : "#083B55", dark ? "#3B82F6" : "#126D96", dark ? "#1D4ED8" : "#06364D", dark ? "#111827" : "#FFFFFF", dark ? "#F8FAFC" : "#14202B", dark ? "#94A3B8" : "#5A6B78", dark ? "#60A5FA" : "#0078B8")
        };

        SetBrush("AppBackgroundBrush", palette.AppBackground);
        SetBrush("HeaderBrush", palette.Header);
        SetBrush("HeaderForegroundBrush", palette.HeaderForeground);
        SetBrush("HeaderMutedBrush", palette.HeaderMuted);
        SetBrush("PanelBrush", palette.Panel);
        SetBrush("PanelBorderBrush", palette.PanelBorder);
        SetBrush("HeadingBrush", palette.Heading);
        SetBrush("MutedTextBrush", palette.MutedText);
        SetBrush("InfoBrush", palette.Info);
        SetBrush("InfoBorderBrush", palette.InfoBorder);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("WarningBorderBrush", palette.WarningBorder);
        SetBrush("WarningTextBrush", palette.WarningText);
        SetBrush("SoftPanelBrush", palette.SoftPanel);
        SetBrush("BodyTextBrush", palette.BodyText);
        SetBrush("ControlForegroundBrush", palette.ControlForeground);
        SetBrush("TabForegroundBrush", palette.TabForeground);
        SetBrush("ButtonBackgroundBrush", palette.ButtonBackground);
        SetBrush("ButtonForegroundBrush", palette.ButtonForeground);
        SetBrush("ButtonBorderBrush", palette.ButtonBorder);
        SetBrush("ButtonHoverBrush", palette.ButtonHover);
        SetBrush("ButtonPressedBrush", palette.ButtonPressed);
        SetBrush("InputBackgroundBrush", palette.InputBackground);
        SetBrush("InputForegroundBrush", palette.InputForeground);
        SetBrush("InputBorderBrush", palette.InputBorder);
        SetBrush("InputFocusBrush", palette.InputFocus);
        SetBrush("CameraStatusBackgroundBrush", dark ? "#1E3A5F" : "#E6F6FF");
        SetBrush("CameraStatusForegroundBrush", dark ? "#F8FAFC" : "#102A43");
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is SolidColorBrush brush && Color.TryParse(color, out var parsed))
            brush.Color = parsed;
    }

    private sealed record ThemePalette(string AppBackground, string Header, string HeaderForeground, string HeaderMuted, string Panel, string PanelBorder, string Heading, string MutedText, string Info, string InfoBorder, string WarningBorder, string WarningText, string SoftPanel, string BodyText, string ControlForeground, string TabForeground, string ButtonBackground, string ButtonForeground, string ButtonBorder, string ButtonHover, string ButtonPressed, string InputBackground, string InputForeground, string InputBorder, string InputFocus)
    {
        public string Warning => _warning;
        private const string _warning = "#FFF8E8";
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
            return;
        }
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        var mode = e.Key switch { Key.D1 => 1, Key.NumPad1 => 1, Key.D2 => 2, Key.NumPad2 => 2, Key.D4 => 4, Key.NumPad4 => 4, _ => 0 };
        if (mode > 0)
        {
            ApplyLayoutMode(mode, true);
            e.Handled = true;
        }
    }

    private void LayoutMode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var mode))
            ApplyLayoutMode(mode, true);
    }

    private void PreviewFit_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || _loadingSettings || sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        _settings.PreviewFitMode = Math.Clamp(combo.SelectedIndex, 0, 1);
        ApplyPreviewFit();
        SaveSettings();
    }

    private void DashboardViewMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || _loadingSettings || sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        _settings.DashboardViewMode = Math.Clamp(combo.SelectedIndex, 0, 1);
        ApplyDashboardViewMode();
        SaveSettings();
    }

    private void ApplyDashboardViewMode()
    {
        var operations = _settings.DashboardViewMode == (int)DashboardViewMode.Operations;
        OpenEventsFolderButton.IsVisible = operations;
        RuntimeInfoText.IsVisible = operations;
        ToolTip.SetTip(RuntimeInfoText, operations
            ? "Chế độ Vận hành: hiển thị hồ sơ, tốc độ suy luận và thông tin kỹ thuật."
            : "Chế độ Giám sát: thông tin kỹ thuật được ẩn để ưu tiên vùng preview.");
    }

    private void ApplyPreviewFit()
    {
        var stretch = _settings.PreviewFitMode == (int)PreviewFitMode.FillFrame
            ? Stretch.UniformToFill
            : Stretch.Uniform;
        foreach (var preview in _previews) preview.Stretch = stretch;
    }

    private void ApplyLayoutMode(int mode, bool persist)
    {
        mode = mode is 1 or 2 or 4 ? mode : 2;
        _settings.DashboardLayoutMode = mode;
        var four = mode == 4;
        CameraGrid.RowDefinitions = new RowDefinitions(four ? "*,*" : "*");
        CameraGrid.ColumnDefinitions = new ColumnDefinitions(mode == 1 ? "*" : "*,*");
        for (var i = 0; i < _cameraTiles.Length; i++)
        {
            var visible = i < mode;
            _cameraTiles[i].IsVisible = visible;
            Grid.SetRow(_cameraTiles[i], four && i >= 2 ? 1 : 0);
            Grid.SetColumn(_cameraTiles[i], mode == 1 ? 0 : four ? i % 2 : i);
        }
        LayoutOneButton.Content = mode == 1 ? "✓ 1 màn hình" : "1 màn hình";
        LayoutTwoButton.Content = mode == 2 ? "✓ 2 màn hình" : "2 màn hình";
        LayoutFourButton.Content = mode == 4 ? "✓ 4 màn hình" : "4 màn hình";
        RefreshOverviewStatuses();
        if (persist) SaveSettings();
    }

    private void RefreshOverviewStatuses()
    {
        for (var i = 0; i < _cameraStatuses.Length; i++)
        {
            var configured = i < _settings.Cameras.Count;
            var text = configured
                ? $"{_settings.Cameras[i].Name} · Đang chờ giám sát"
                : $"Camera {i + 1} chưa cấu hình";
            _cameraStatuses[i].Text = text;
            _cameraOverlayLabels[i].Text = configured ? _settings.Cameras[i].Name : $"Camera {i + 1}";
            _cameraFocusButtons[i].IsVisible = configured;
            ToolTip.SetTip(_cameraOverlayLabels[i], text);
        }
    }

    private void FocusCameraTile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index) && index >= 0 && index < _settings.Cameras.Count)
        {
            CameraList.SelectedIndex = index;
            ApplyLayoutMode(1, true);
            SetStatus($"Đã phóng to {_settings.Cameras[index].Name}.");
        }
    }

    private void UpdateCameraStatus(Guid id, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _cameraRuntimeStatuses[id] = status;
            var index = _settings.Cameras.FindIndex(x => x.Id == id);
            if (index >= 0 && index < _cameraStatuses.Length)
            {
                var text = $"{_settings.Cameras[index].Name} · {status}";
                _cameraStatuses[index].Text = text;
                _cameraOverlayLabels[index].Text = _settings.Cameras[index].Name;
                ToolTip.SetTip(_cameraOverlayLabels[index], text);
            }
            RefreshSystemStatus();
        });
    }

    private void UpdateCameraRuntime(Guid id, CameraRuntimeSnapshot runtime)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _cameraRuntimeSnapshots[id] = runtime;
            _cameraRuntimeStatuses[id] = runtime.Message;
            RefreshSystemStatus();
        });
    }

    private void UpdatePreview(Guid id, Mat image)
    {
        if (!_liveViewEnabled)
        {
            image.Dispose();
            return;
        }

        lock (_previewSync)
        {
            if (_pendingPreviewMats.TryGetValue(id, out var oldPending)) oldPending.Dispose();
            _pendingPreviewMats[id] = image;
            if (!_previewEncodeScheduled.Add(id)) return;
        }

        // Không encode JPEG trên thread đọc RTSP. Mỗi camera chỉ có một encode
        // task; frame mới sẽ thay thế frame cũ trong pending queue.
        _ = Task.Run(() => EncodeLatestPreview(id));
    }

    private void EncodeLatestPreview(Guid id)
    {
        while (!_shutdownStarted)
        {
            Mat? image;
            lock (_previewSync)
            {
                if (!_pendingPreviewMats.Remove(id, out image))
                {
                    _previewEncodeScheduled.Remove(id);
                    return;
                }
            }

            Bitmap? bitmap = null;
            try { bitmap = ToBitmap(image); }
            catch (Exception ex) { AppLogger.Error(LogChannel.App, $"preview encode failed; cameraId={id}", ex); }
            finally { image.Dispose(); }
            if (bitmap is null) continue;

            var shouldPost = false;
            lock (_previewSync)
            {
                if (_pendingPreviewBitmaps.TryGetValue(id, out var oldPending)) oldPending.Dispose();
                _pendingPreviewBitmaps[id] = bitmap;
                shouldPost = _previewDispatchScheduled.Add(id);
            }
            if (shouldPost) Dispatcher.UIThread.Post(() => ApplyPendingPreview(id));
        }
    }

    private void ApplyPendingPreview(Guid id)
    {
        Bitmap? bitmap;
        lock (_previewSync)
        {
            if (!_pendingPreviewBitmaps.Remove(id, out bitmap))
            {
                _previewDispatchScheduled.Remove(id);
                return;
            }
            _previewDispatchScheduled.Remove(id);
        }

        var index = _settings.Cameras.FindIndex(x => x.Id == id);
        if (bitmap is null) return;
        if (index >= 0 && index < _previews.Length)
        {
            var old = _previews[index].Source as IDisposable;
            _previews[index].Source = bitmap;
            old?.Dispose();
            _lastPreviewAt[id] = DateTimeOffset.Now;
            _previewFramesApplied++;
        }
        else
        {
            bitmap.Dispose();
        }

        lock (_previewSync)
        {
            if (_pendingPreviewBitmaps.ContainsKey(id) && _previewDispatchScheduled.Add(id))
                Dispatcher.UIThread.Post(() => ApplyPendingPreview(id));
        }
    }

    private static Bitmap ToBitmap(Mat image)
    {
        const int maxPreviewDimension = 960;
        using var preview = new Mat();
        var longest = Math.Max(image.Width, image.Height);
        if (longest > maxPreviewDimension)
        {
            var scale = maxPreviewDimension / (double)longest;
            var size = new OpenCvSharp.Size(
                Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale)));
            Cv2.Resize(image, preview, size, 0, 0, InterpolationFlags.Area);
        }
        else
        {
            image.CopyTo(preview);
        }

        Cv2.ImEncode(".jpg", preview, out var bytes);
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    private void HandleEventRecorded(DetectionEvent item)
    {
        _lastRecordedEvent = item;
        Dispatcher.UIThread.Post(() =>
        {
            RefreshEvents();
            RefreshSystemStatus();
        });
    }

    private void RefreshEvents_Click(object? sender, RoutedEventArgs e) => RefreshEvents();

    private void EventSearch_Changed(object? sender, TextChangedEventArgs e) => ApplyEventFilters();

    private void EventCameraFilter_Changed(object? sender, SelectionChangedEventArgs e) => ApplyEventFilters();

    private void EventTimeFilter_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_uiInitialized) ApplyEventFilters();
    }

    private void ApplyEventDate_Click(object? sender, RoutedEventArgs e)
    {
        if (EventTimeFilterCombo.SelectedIndex != (int)EventTimeFilterMode.SpecificDate)
            EventTimeFilterCombo.SelectedIndex = (int)EventTimeFilterMode.SpecificDate;
        ApplyEventFilters();
    }

    private void RefreshEvents()
    {
        try
        {
            _eventCache = _eventStore.Recent(10_000);
            var cameraNames = new[] { "Tất cả camera" }.Concat(_eventCache.Select(x => x.CameraName).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
            var previousCamera = EventCameraFilter.SelectedItem as string;
            EventCameraFilter.ItemsSource = cameraNames;
            var preferredIndex = !string.IsNullOrWhiteSpace(previousCamera) ? cameraNames.IndexOf(previousCamera) : 0;
            EventCameraFilter.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
            ApplyEventFilters();
            EventLogSummaryText.Text = $"Đã tải {_eventCache.Count} sự kiện · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _eventCache = Array.Empty<DetectionEvent>();
            EventsGrid.ItemsSource = Array.Empty<DetectionEvent>();
            EventLogSummaryText.Text = $"Lỗi đọc nhật ký: {ex.Message}";
            StartupDiagnostics.Write("RefreshEvents", ex);
        }
    }

    private void ApplyEventFilters()
    {
        if (EventsGrid is null) return;
        var search = EventSearchText?.Text?.Trim() ?? string.Empty;
        var selectedCamera = EventCameraFilter?.SelectedItem as string;
        var mode = (EventTimeFilterMode)Math.Clamp(EventTimeFilterCombo?.SelectedIndex ?? 0, 0, 4);
        var range = GetEventTimeRange(mode);
        if (!range.IsValid)
        {
            EventsGrid.ItemsSource = Array.Empty<DetectionEvent>();
            if (EventLogSummaryText is not null) EventLogSummaryText.Text = range.ErrorMessage!;
            return;
        }

        var filtered = _eventCache.Where(x =>
            (!range.From.HasValue || x.DetectedAt >= range.From.Value) &&
            (!range.To.HasValue || x.DetectedAt < range.To.Value) &&
            (string.IsNullOrWhiteSpace(selectedCamera) || selectedCamera == "Tất cả camera" || x.CameraName.Equals(selectedCamera, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(search) || $"{x.CameraName} {x.DetectionSource} {x.DeliveryStatus} {x.AiStatus} {x.AiSummary}".Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        EventsGrid.ItemsSource = filtered;
        if (EventLogSummaryText is not null)
        {
            var rangeText = mode switch
            {
                EventTimeFilterMode.LastDay => " · 1 ngày",
                EventTimeFilterMode.LastTwoDays => " · 2 ngày",
                EventTimeFilterMode.LastSevenDays => " · 7 ngày",
                EventTimeFilterMode.SpecificDate => $" · {EventSpecificDateText.Text?.Trim()}",
                _ => string.Empty
            };
            EventLogSummaryText.Text = $"Hiển thị {filtered.Count}/{_eventCache.Count} sự kiện{rangeText}";
        }
    }

    private (DateTimeOffset? From, DateTimeOffset? To, bool IsValid, string? ErrorMessage) GetEventTimeRange(EventTimeFilterMode mode)
    {
        var now = DateTimeOffset.Now;
        return mode switch
        {
            EventTimeFilterMode.LastDay => (now.AddDays(-1), null, true, null),
            EventTimeFilterMode.LastTwoDays => (now.AddDays(-2), null, true, null),
            EventTimeFilterMode.LastSevenDays => (now.AddDays(-7), null, true, null),
            EventTimeFilterMode.SpecificDate => ParseSpecificEventDate(),
            _ => (null, null, true, null)
        };
    }

    private (DateTimeOffset? From, DateTimeOffset? To, bool IsValid, string? ErrorMessage) ParseSpecificEventDate()
    {
        if (!DateTime.TryParseExact(EventSpecificDateText?.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return (null, null, false, "Ngày không hợp lệ · dùng yyyy-MM-dd");
        var localDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Local);
        var from = new DateTimeOffset(localDate);
        return (from, from.AddDays(1), true, null);
    }

    private async void CleanupEventData_Click(object? sender, RoutedEventArgs e)
    {
        var policy = (CleanupRetentionPolicy)Math.Clamp(CleanupRetentionCombo?.SelectedIndex ?? 0, 0, 4);
        var policyText = CleanupRetentionCombo?.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : "chính sách đã chọn";
        var warning = policy == CleanupRetentionPolicy.DeleteAll
            ? "Thao tác này sẽ xóa toàn bộ sự kiện trong cơ sở dữ liệu, ảnh sự kiện và các file log của ứng dụng. Không thể hoàn tác."
            : $"Thao tác này sẽ xóa sự kiện, ảnh và nội dung log cũ hơn mốc {policyText?.ToLowerInvariant()}. Dữ liệu mới hơn sẽ được giữ lại.";
        var confirmed = await new CleanupConfirmWindow(policyText ?? "Dọn dữ liệu", warning).ShowDialog<bool>(this);
        if (!confirmed) return;

        try
        {
            SetStatus("Đang dọn log, sự kiện và ảnh; vui lòng chờ...");
            var result = await Task.Run(() => _eventMaintenance.Cleanup(policy));
            _eventDetailBitmap?.Dispose();
            _eventDetailBitmap = null;
            EventDetailImage.Source = null;
            EventDetailText.Text = "Chọn một dòng để xem chi tiết.";
            RefreshEvents();
            CleanupSummaryText.Text = $"Đã xóa {result.DeletedEvents} sự kiện · giải phóng {FormatBytes(result.FreedBytes)} · {result.ProcessedLogFiles} log" + (result.FailedFiles > 0 ? $" · lỗi {result.FailedFiles} file" : string.Empty);
            SetStatus("Đã dọn dữ liệu theo chính sách lưu giữ.");
        }
        catch (Exception ex)
        {
            CleanupSummaryText.Text = $"Dọn dữ liệu lỗi: {ex.Message}";
            SetStatus("Không thể hoàn tất dọn dữ liệu; xem log ứng dụng.");
            AppLogger.Error(LogChannel.App, "event/log cleanup failed", ex);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024d):0.0} MB";
        return $"{bytes / (1024d * 1024d * 1024d):0.0} GB";
    }

    private void EventsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not DetectionEvent item) return;
        _eventDetailBitmap?.Dispose();
        _eventDetailBitmap = null;
        EventDetailText.Text = $"{item.DetectedAt:yyyy-MM-dd HH:mm:ss}\nCamera: {item.CameraName}\nPhát hiện: {(item.IsHumanDetection ? "Người" : "Chuyển động")} · Tin cậy: {item.Confidence:P0}\nNguồn: {item.DetectionSource}\nAI: {item.AiStatus}{(string.IsNullOrWhiteSpace(item.AiSummary) ? string.Empty : $"\n{item.AiSummary}")}\nGửi: {item.DeliveryStatus}";
        try
        {
            if (File.Exists(item.ImagePath))
            {
                _eventDetailBitmap = new Bitmap(item.ImagePath);
                EventDetailImage.Source = _eventDetailBitmap;
            }
            else
            {
                EventDetailImage.Source = null;
            }
        }
        catch (Exception ex)
        {
            EventDetailImage.Source = null;
            EventDetailText.Text += $"\nKhông đọc được ảnh: {ex.Message}";
        }
    }

    private void OpenSelectedEventImage_Click(object? sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is DetectionEvent item && File.Exists(item.ImagePath))
            Process.Start(new ProcessStartInfo { FileName = item.ImagePath, UseShellExecute = true });
        else
            SetStatus("Sự kiện chưa có ảnh hoặc ảnh đã bị xóa theo chính sách lưu trữ.");
    }

    private void OpenAuthorWebsite_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "https://huynd.io.vn", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Không mở được website tác giả: {ex.Message}");
        }
    }

    private void SendAuthorEmail_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "mailto:huynd130994@gmail.com", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Không mở được ứng dụng email: {ex.Message}");
        }
    }

    private void OpenEventsFolder_Click(object? sender, RoutedEventArgs e)
    {
        DataPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = DataPaths.EventImages, UseShellExecute = true });
    }

    private void OpenZaloLog_Click(object? sender, RoutedEventArgs e)
    {
        DataPaths.EnsureCreated();
        if (!File.Exists(DataPaths.ZaloLogFile))
            File.WriteAllText(DataPaths.ZaloLogFile, "Chưa có log gửi Zalo. Hãy tạo một sự kiện hoặc bấm Gửi thử trước.\n");
        Process.Start(new ProcessStartInfo { FileName = DataPaths.ZaloLogFile, UseShellExecute = true });
    }

    private static string? ResolveSystemLogPath(string? key) => key?.ToLowerInvariant() switch
    {
        "app" => DataPaths.AppLogFile,
        "camera" => DataPaths.CameraLogFile,
        "alerts" => DataPaths.AlertsLogFile,
        "ai" => DataPaths.AiLogFile,
        "zalo" => DataPaths.ZaloLogFile,
        "startup" => StartupDiagnostics.LogFilePath,
        _ => null
    };

    private void OpenSystemLog_Click(object? sender, RoutedEventArgs e)
    {
        var key = (sender as Control)?.Tag?.ToString();
        var path = ResolveSystemLogPath(key);
        if (path is null) return;
        try
        {
            DataPaths.EnsureCreated();
            if (!File.Exists(path)) File.WriteAllText(path, $"Chưa có log {key}.\\n");
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Không mở được log: {ex.Message}");
        }
    }

    private async void ClearSystemLogs_Click(object? sender, RoutedEventArgs e)
    {
        var confirm = new CleanupConfirmWindow("log vận hành", "Thao tác này sẽ xóa app.log, camera.log, alerts.log, ai.log, zalo-send.log và startup-crash.log cùng các file xoay .1. Không xóa nhật ký sự kiện hoặc ảnh camera.");
        if (!await confirm.ShowDialog<bool>(this)) return;
        var paths = new[]
        {
            DataPaths.AppLogFile, DataPaths.CameraLogFile, DataPaths.AlertsLogFile, DataPaths.AiLogFile,
            DataPaths.ZaloLogFile, StartupDiagnostics.LogFilePath
        };
        var deleted = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidate in new[] { path, path + ".1" })
            {
                try
                {
                    if (File.Exists(candidate)) { File.Delete(candidate); deleted++; }
                }
                catch (Exception ex) { AppLogger.Error(LogChannel.App, $"system log delete failed; file={Path.GetFileName(candidate)}", ex); }
            }
        }
        SetStatus($"Đã xóa {deleted} file log vận hành.");
    }

    private void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DataPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo { FileName = DataPaths.Root, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Không mở được thư mục dữ liệu: {ex.Message}");
        }
    }

    private async void BackupSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedCamera is not null && !TryApplySelectedCameraFromUi(out var cameraError))
            {
                SetStatus($"Không sao lưu được cấu hình camera: {cameraError}");
                return;
            }
            if (SelectedCamera is not null) RefreshCameraList();
            SaveAlerts_Click(sender, e);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Sao lưu cấu hình EZVIZ Local Monitor",
                SuggestedFileName = $"ezviz-settings-backup-{DateTime.Now:yyyyMMdd-HHmmss}.ezvizbackup",
                FileTypeChoices = new[] { new FilePickerFileType("EZVIZ backup") { Patterns = new[] { "*.ezvizbackup" } } }
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) { SetStatus("Đã hủy sao lưu cấu hình."); return; }
            _settingsStore.ExportBackup(_settings, path);
            AppLogger.Info(LogChannel.App, $"settings backup exported; file={Path.GetFileName(path)}");
            SetStatus("Đã sao lưu cấu hình bằng DPAPI; token không nằm dạng plaintext trong file.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "settings backup failed", ex);
            SetStatus($"Không sao lưu được cấu hình: {ex.Message}");
        }
    }

    private async void RestoreSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Khôi phục cấu hình EZVIZ Local Monitor",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("EZVIZ backup") { Patterns = new[] { "*.ezvizbackup" } } }
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) { SetStatus("Đã hủy khôi phục cấu hình."); return; }
            var imported = _settingsStore.ImportBackup(path);
            var wasRunning = _coordinator is not null;
            if (wasRunning) await StopMonitoringAsync();
            _settings = imported;
            ClearRuntimeStateForConfigurationChange();
            _settingsStore.Save(_settings);
            _loadingSettings = true;
            LoadSettings();
            PerformanceProfileCombo.SelectedIndex = Math.Clamp(_settings.PerformanceProfile, 0, 3);
            _loadingSettings = false;
            ApplyTheme();
            ApplyLayoutMode(_settings.DashboardLayoutMode, false);
            ApplyPreviewFit();
            ApplyDashboardViewMode();
            if (wasRunning) await StartMonitoringAsync(false);
            AppLogger.Info(LogChannel.App, $"settings backup restored; file={Path.GetFileName(path)}");
            SetStatus("Đã khôi phục cấu hình và mã hóa lại bằng Windows DPAPI.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "settings restore failed", ex);
            SetStatus($"Không khôi phục được cấu hình: {ex.Message}");
        }
    }

    private async void ExportDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SaveAlerts_Click(sender, e);
            SetStatus("Đang tạo gói chẩn đoán đã che thông tin nhạy cảm...");
            var package = await AppLogger.ExportDiagnosticsAsync(_settings);
            AppLogger.Info(LogChannel.App, $"diagnostics package exported; file={Path.GetFileName(package)}");
            SetStatus($"Đã xuất gói chẩn đoán: {package}");
            Process.Start(new ProcessStartInfo { FileName = DataPaths.Root, UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            SetStatus("Đã hủy xuất gói chẩn đoán.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "diagnostics package export failed", ex);
            SetStatus($"Không xuất được gói chẩn đoán: {ex.Message}");
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
        if (!_settings.HasCompletedOnboarding)
        {
            var onboarding = new OnboardingWindow();
            await onboarding.ShowDialog(this);
            if (onboarding.Completed)
            {
                _settings.HasCompletedOnboarding = true;
                SaveSettings();
            }
        }

        WindowsStartupService.Apply(_settings.StartWithWindows);
        if (_settings.WatchdogEnabled) _watchdog.Start(Program.LaunchInTray);
        var monitoringTask = MonitorScheduleService.IsMonitoringAllowed(_settings)
            ? StartMonitoringAsync(false)
            : Task.CompletedTask;

        if (Program.LaunchInTray)
        {
            await monitoringTask;
            HideToTray();
            return;
        }

        if (!_settings.AutoUpdateEnabled)
        {
            await monitoringTask;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var update = await _updateService.CheckAsync(timeout.Token);
            if (update?.IsNewer != true || string.IsNullOrWhiteSpace(update.PackageUrl)) return;

            var prompt = new UpdatePromptWindow(update);
            var choice = await prompt.ShowDialog<UpdatePromptChoice>(this);
            if (choice == UpdatePromptChoice.OpenRelease && Uri.TryCreate(update.ReleaseUrl, UriKind.Absolute, out var releaseUri))
            {
                Process.Start(new ProcessStartInfo { FileName = releaseUri.ToString(), UseShellExecute = true });
            }
            else if (choice == UpdatePromptChoice.Update)
            {
                await LaunchUpdaterAsync(update);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Bỏ qua kiểm tra cập nhật do kết nối quá chậm.");
        }
        catch
        {
            SetStatus("Không kiểm tra được bản cập nhật tự động; ứng dụng vẫn hoạt động bình thường.");
        }

        await monitoringTask;
    }

    private async Task LaunchUpdaterAsync(AppUpdateInfo update)
    {
        if (!OperatingSystem.IsWindows()) return;

        var script = AppUpdateService.UpdaterScriptPath;
        if (!File.Exists(script))
        {
            SetStatus("Đang tải thành phần cập nhật và kiểm tra SHA-256...");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            script = await _updateService.PrepareUpdaterAsync(update, timeout.Token);
            if (string.IsNullOrWhiteSpace(script) || !File.Exists(script))
            {
                SetStatus("Không tìm thấy thành phần updater. Hãy dùng scripts\\Update-EzvizLocalMonitor.cmd thủ công.");
                return;
            }
        }

        SetStatus("Đang mở trình cập nhật...");
        _watchdog.Stop();
        await StopMonitoringAsync();
        var process = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppUpdateService.InstalledDirectory
        };
        process.ArgumentList.Add("-NoLogo");
        process.ArgumentList.Add("-NoProfile");
        process.ArgumentList.Add("-ExecutionPolicy");
        process.ArgumentList.Add("Bypass");
        process.ArgumentList.Add("-File");
        process.ArgumentList.Add(script);
        process.ArgumentList.Add("-InstallDir");
        process.ArgumentList.Add(AppUpdateService.InstalledDirectory);
        process.ArgumentList.Add("-Repository");
        process.ArgumentList.Add(AppUpdateService.Repository);
        process.ArgumentList.Add("-Force");
        Process.Start(process);
        _exitRequested = true;
        Close();
    }

    public void HideToTray()
    {
        _liveViewEnabled = false;
        _coordinator?.SetPreviewEnabled(false);
        ClearPendingPreviewFrames();
        Hide();
        SetStatus("Đang chạy nền — live view tạm dừng, giám sát và cảnh báo vẫn hoạt động.");
    }

    public void ShowFromTray()
    {
        _liveViewEnabled = true;
        _coordinator?.SetPreviewEnabled(true);
        Show();
        WindowState = WindowState.Normal;
        Activate();
        SetStatus("Đã mở ứng dụng — live view đang khôi phục, giám sát vẫn hoạt động.");
    }

    private void ClearPendingPreviewFrames()
    {
        lock (_previewSync)
        {
            foreach (var pending in _pendingPreviewMats.Values) pending.Dispose();
            foreach (var pending in _pendingPreviewBitmaps.Values) pending.Dispose();
            _pendingPreviewMats.Clear();
            _pendingPreviewBitmaps.Clear();
        }
    }

    public async void StopFromTray()
    {
        await StopMonitoringAsync();
    }

    public async void CheckForUpdateFromTray()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var update = await _updateService.CheckAsync(timeout.Token);
            if (update?.IsNewer == true && !string.IsNullOrWhiteSpace(update.PackageUrl))
            {
                var prompt = new UpdatePromptWindow(update);
                var choice = await prompt.ShowDialog<UpdatePromptChoice>(this);
                if (choice == UpdatePromptChoice.Update) await LaunchUpdaterAsync(update);
                else if (choice == UpdatePromptChoice.OpenRelease && Uri.TryCreate(update.ReleaseUrl, UriKind.Absolute, out var uri))
                    Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
            }
            else SetStatus("Đã là phiên bản mới nhất.");
        }
        catch (Exception ex)
        {
            SetStatus($"Không kiểm tra được cập nhật: {ex.Message}");
        }
    }

    public async void ExitFromTray()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _exitRequested = true;
        _watchdog.Stop();
        Hide();
        SetStatus("Đang dừng camera và thoát an toàn...");

        await StopMonitoringAsync(TimeSpan.FromSeconds(8));

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        ClearPendingPreviewFrames();
        lock (_previewSync)
        {
            _previewEncodeScheduled.Clear();
            _previewDispatchScheduled.Clear();
        }
        _watchdog.Dispose();
        // Không dispose đồng bộ trên UI thread. ExitFromTray/LaunchUpdaterAsync đã dừng
        // coordinator theo đường async có timeout; Window_Closing chỉ giải phóng bitmap UI.
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            GlobalStatusText.Text = text;
            TrayStatusChanged?.Invoke(text);
        });
    }
}
