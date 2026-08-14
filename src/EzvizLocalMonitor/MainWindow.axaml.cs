using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using OpenCvSharp;

namespace EzvizLocalMonitor;

public partial class MainWindow : Avalonia.Controls.Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly EventStore _eventStore = new();
    private readonly AlertDispatcher _alerts = new();
    private readonly LanCameraDiscovery _lanDiscovery = new();
    private AppSettings _settings = new();
    private MonitorCoordinator? _coordinator;
    private Image[] _previews = Array.Empty<Image>();
    private TextBlock[] _cameraStatuses = Array.Empty<TextBlock>();
    private Border[] _cameraTiles = Array.Empty<Border>();
    private readonly AppUpdateService _updateService = new();
    private readonly object _previewSync = new();
    private readonly Dictionary<Guid, Bitmap> _pendingPreviewBitmaps = new();
    private readonly HashSet<Guid> _previewDispatchScheduled = new();
    private readonly Dictionary<Guid, string> _cameraRuntimeStatuses = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastPreviewAt = new();
    private readonly DispatcherTimer _systemStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<DetectionEvent> _eventCache = Array.Empty<DetectionEvent>();
    private TimeSpan _lastProcessCpu;
    private DateTimeOffset _lastProcessCpuAt;
    private int _previewFramesApplied;
    private int _lastPreviewFramesApplied;
    private DateTimeOffset _lastPreviewMetricAt;
    private bool _loadingSettings;
    private Bitmap? _eventDetailBitmap;
    private bool _updateCheckStarted;
    private bool _exitRequested;

    public event Action<string>? TrayStatusChanged;

    public MainWindow()
    {
        InitializeComponent();
        _previews = new[] { PreviewOne, PreviewTwo, PreviewThree, PreviewFour };
        _cameraStatuses = new[] { CameraOneStatus, CameraTwoStatus, CameraThreeStatus, CameraFourStatus };
        _cameraTiles = new[] { CameraTileOne, CameraTileTwo, CameraTileThree, CameraTileFour };
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "không xác định";
        VersionText.Text = $"Bản {version} · Nhận diện người cục bộ · Ảnh sự kiện chỉ rời LAN khi Telegram/Zalo được bật.";
        DataPaths.EnsureCreated();
        _eventStore.Initialize();
        _loadingSettings = true;
        LoadSettings();
        ApplyTheme();
        PerformanceProfileCombo.SelectedIndex = Math.Clamp(_settings.PerformanceProfile, 0, 3);
        _loadingSettings = false;
        ApplyLayoutMode(_settings.DashboardLayoutMode, false);
        RefreshEvents();
        ConfidenceSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "Value") ConfidenceText.Text = $"{ConfidenceSlider.Value:P0}";
        };
        Opened += MainWindow_Opened;
        _systemStatusTimer.Tick += (_, _) => RefreshSystemStatus();
        _systemStatusTimer.Start();
        Closed += (_, _) => _systemStatusTimer.Stop();
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
        TelegramEnabledCheck.IsChecked = _settings.Alerts.TelegramEnabled;
        TelegramTokenText.Text = _settings.Alerts.TelegramBotToken;
        TelegramChatText.Text = _settings.Alerts.TelegramChatId;
        ZaloEnabledCheck.IsChecked = _settings.Alerts.ZaloEnabled;
        ZaloTokenText.Text = _settings.Alerts.ZaloBotToken;
        ZaloChatText.Text = _settings.Alerts.ZaloChatId;
        AiEnabledCheck.IsChecked = _settings.Ai.Enabled;
        AiBaseUrlText.Text = _settings.Ai.BaseUrl;
        AiModelText.Text = _settings.Ai.Model;
        AiApiKeyText.Text = _settings.Ai.ApiKey;
        AiTimeoutText.Text = _settings.Ai.TimeoutSeconds.ToString();
        AiRequireConfirmationCheck.IsChecked = _settings.Ai.RequireConfirmationBeforeAlert;
        RuntimeInfoText.Text = $"Hồ sơ: {PerformanceProfileName(_settings.PerformanceProfile)} · {_settings.InferenceFpsPerCamera} lần suy luận/giây/camera · xác nhận {_settings.ConfirmationsRequired}/{_settings.ConfirmationWindow} khung";

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
        SystemHealthText.Text = $"Hệ thống: {active}/{enabled} camera đang hoạt động · {warning} cảnh báo kết nối · {telegram} · {zalo}";
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
        SystemMetricsText.Text = $"CPU {cpu:0}% · RAM {memoryMb:0} MB · Preview {fps:0.0} FPS";
    }

    private void SaveSettings()
    {
        _settingsStore.Save(_settings);
        SetStatus("Đã lưu cấu hình cục bộ an toàn");
    }

    private CameraDefinition? SelectedCamera => CameraList.SelectedItem as CameraDefinition;

    private void CameraList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = SelectedCamera;
        if (item is null) return;
        CameraNameText.Text = item.Name;
        CameraRtspText.Text = item.RtspUrl;
        ConfidenceSlider.Value = item.ConfidenceThreshold;
        ConfidenceText.Text = $"{item.ConfidenceThreshold:P0}";
        CooldownText.Text = item.CooldownSeconds.ToString();
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

    private void RemoveCamera_Click(object? sender, RoutedEventArgs e)
    {
        var camera = SelectedCamera;
        if (camera is null) return;
        _settings.Cameras.Remove(camera);
        RefreshCameraList();
        SaveSettings();
    }

    private void SaveCamera_Click(object? sender, RoutedEventArgs e)
    {
        var camera = SelectedCamera;
        if (camera is null) { SetStatus("Hãy thêm hoặc chọn camera trước khi lưu."); return; }
        if (!int.TryParse(CooldownText.Text, out var cooldown) || cooldown is < 5 or > 3600)
        {
            SetStatus("Khoảng im lặng phải là số từ 5 đến 3600 giây.");
            return;
        }
        camera.Name = string.IsNullOrWhiteSpace(CameraNameText.Text) ? "Camera" : CameraNameText.Text.Trim();
        camera.RtspUrl = CameraRtspText.Text?.Trim() ?? string.Empty;
        camera.ConfidenceThreshold = ConfidenceSlider.Value;
        camera.CooldownSeconds = cooldown;
        camera.IsEnabled = CameraEnabledCheck.IsChecked == true;
        RefreshCameraList();
        SaveSettings();
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
        if (_loadingSettings || PerformanceProfileCombo.SelectedIndex < 0) return;
        _settings.PerformanceProfile = PerformanceProfileCombo.SelectedIndex;
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
        _settings.Ai.Enabled = AiEnabledCheck.IsChecked == true;
        _settings.Ai.BaseUrl = AiBaseUrlText.Text?.Trim() ?? string.Empty;
        _settings.Ai.Model = AiModelText.Text?.Trim() ?? string.Empty;
        _settings.Ai.ApiKey = AiApiKeyText.Text?.Trim() ?? string.Empty;
        _settings.Ai.TimeoutSeconds = int.TryParse(AiTimeoutText.Text, out var timeout) ? Math.Clamp(timeout, 5, 90) : 25;
        _settings.Ai.RequireConfirmationBeforeAlert = AiRequireConfirmationCheck.IsChecked == true;
        SaveSettings();
        RefreshSystemStatus();
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
            _coordinator.PreviewReady += UpdatePreview;
            _coordinator.EventRecorded += _ => Dispatcher.UIThread.Post(RefreshEvents);
            await _coordinator.StartAsync(_settings);
            SetStatus("Đang giám sát cục bộ — ONVIF event hoặc YOLO fallback");
        }
        catch (Exception ex)
        {
            _coordinator = null;
            SetStatus($"Không thể khởi động: {ex.Message}");
        }
    }

    private async void StopMonitoring_Click(object? sender, RoutedEventArgs e) => await StopMonitoringAsync();

    private async Task StopMonitoringAsync()
    {
        if (_coordinator is null) return;
        await _coordinator.DisposeAsync();
        _coordinator = null;
        SetStatus("Đã dừng giám sát");
        foreach (var status in _cameraStatuses) status.Text = "Đã dừng";
    }

    private void FocusSelectedCamera_Click(object? sender, RoutedEventArgs e)
    {
        ApplyLayoutMode(1, true);
        var selected = SelectedCamera;
        SetStatus(selected is null ? "Đã phóng to camera đầu tiên." : $"Đã phóng to {selected.Name}. Bấm 2/4 màn hình để quay lại.");
    }

    private void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        ApplyTheme();
        SaveSettings();
    }

    private void ApplyTheme()
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = _settings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
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
            _cameraStatuses[i].Text = i < _settings.Cameras.Count
                ? $"{_settings.Cameras[i].Name} · Đang chờ giám sát"
                : $"Camera {i + 1} chưa cấu hình";
    }

    private void UpdateCameraStatus(Guid id, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _cameraRuntimeStatuses[id] = status;
            var index = _settings.Cameras.FindIndex(x => x.Id == id);
            if (index >= 0 && index < _cameraStatuses.Length)
                _cameraStatuses[index].Text = $"{_settings.Cameras[index].Name} · {status}";
            RefreshSystemStatus();
        });
    }

    private void UpdatePreview(Guid id, Mat image)
    {
        Bitmap? bitmap = null;
        try { bitmap = ToBitmap(image); }
        finally { image.Dispose(); }
        if (bitmap is null) return;

        lock (_previewSync)
        {
            if (_pendingPreviewBitmaps.TryGetValue(id, out var oldPending)) oldPending.Dispose();
            _pendingPreviewBitmaps[id] = bitmap;
            if (!_previewDispatchScheduled.Add(id)) return;
        }
        Dispatcher.UIThread.Post(() => ApplyPendingPreview(id));
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
            RefreshSystemStatus();
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
        Cv2.ImEncode(".jpg", image, out var bytes);
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    private void RefreshEvents_Click(object? sender, RoutedEventArgs e) => RefreshEvents();

    private void EventSearch_Changed(object? sender, TextChangedEventArgs e) => ApplyEventFilters();

    private void EventCameraFilter_Changed(object? sender, SelectionChangedEventArgs e) => ApplyEventFilters();

    private void RefreshEvents()
    {
        _eventCache = _eventStore.Recent();
        var cameraNames = new[] { "Tất cả camera" }.Concat(_eventCache.Select(x => x.CameraName).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
        EventCameraFilter.ItemsSource = cameraNames;
        if (EventCameraFilter.SelectedIndex < 0) EventCameraFilter.SelectedIndex = 0;
        ApplyEventFilters();
    }

    private void ApplyEventFilters()
    {
        if (EventsGrid is null) return;
        var search = EventSearchText?.Text?.Trim() ?? string.Empty;
        var selectedCamera = EventCameraFilter?.SelectedItem as string;
        var filtered = _eventCache.Where(x =>
            (string.IsNullOrWhiteSpace(selectedCamera) || selectedCamera == "Tất cả camera" || x.CameraName.Equals(selectedCamera, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(search) || $"{x.CameraName} {x.DetectionSource} {x.DeliveryStatus} {x.AiStatus} {x.AiSummary}".Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        EventsGrid.ItemsSource = filtered;
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

        var monitoringTask = StartMonitoringAsync(false);

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
        Hide();
        SetStatus("Đang chạy nền và tiếp tục giám sát — mở lại từ biểu tượng khay thông báo.");
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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

    public void ExitFromTray()
    {
        _exitRequested = true;
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

        lock (_previewSync)
        {
            foreach (var pending in _pendingPreviewBitmaps.Values) pending.Dispose();
            _pendingPreviewBitmaps.Clear();
            _previewDispatchScheduled.Clear();
        }
        if (_coordinator is not null) _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
