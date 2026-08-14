using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private bool _updateCheckStarted;
    private bool _exitRequested;

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
        LoadSettings();
        ApplyLayoutMode(_settings.DashboardLayoutMode, false);
        RefreshEvents();
        ConfidenceSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "Value") ConfidenceText.Text = $"{ConfidenceSlider.Value:P0}";
        };
        Opened += MainWindow_Opened;
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
        RuntimeInfoText.Text = $"Chế độ: {_settings.InferenceFpsPerCamera} lần suy luận/giây/camera · xác nhận {_settings.ConfirmationsRequired}/{_settings.ConfirmationWindow} khung";

        if (_settings.Cameras.Count > 0) CameraList.SelectedIndex = 0;
    }

    private void RefreshCameraList()
    {
        CameraList.ItemsSource = null;
        CameraList.ItemsSource = _settings.Cameras;
        if (_cameraStatuses.Length > 0) RefreshOverviewStatuses();
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
            var index = _settings.Cameras.FindIndex(x => x.Id == id);
            if (index >= 0 && index < _cameraStatuses.Length) _cameraStatuses[index].Text = status;
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

    private void RefreshEvents()
    {
        EventsList.ItemsSource = _eventStore.Recent().Select(x =>
            $"{x.DetectedAt:yyyy-MM-dd HH:mm:ss}  |  {x.CameraName}  |  {x.Confidence:P0}  |  {x.DeliveryStatus}  |  {x.AiStatus}{(string.IsNullOrWhiteSpace(x.AiSummary) ? string.Empty : $": {x.AiSummary}")}").ToList();
    }

    private void OpenEventsFolder_Click(object? sender, RoutedEventArgs e)
    {
        DataPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = DataPaths.EventImages, UseShellExecute = true });
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
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
        Dispatcher.UIThread.Post(() => GlobalStatusText.Text = text);
    }
}
