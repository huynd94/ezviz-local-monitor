using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor;

public partial class MainWindow
{
    private async Task EnsureRuntimeStartedAsync()
    {
        if (_shutdownStarted) return;
        await _monitoring.InitializeAsync(_settings, _liveViewEnabled, CancellationToken.None);
    }

    private async Task ApplyScheduleAsync()
    {
        if (!_uiInitialized || _shutdownStarted) return;
        try
        {
            await _monitoring.ApplySettingsAsync(_settings, _liveViewEnabled, CancellationToken.None);
        }
        catch (Exception ex) { _logger.Error(LogChannel.App, "schedule transition failed", ex); SetStatus(GetMonitoringStartupError(ex)); }
    }

    private async Task StartMonitoringAsync(bool saveSettings)
    {
        if (saveSettings) SaveSettings();
        try
        {
            await _monitoring.StartAsync(_settings, _liveViewEnabled, CancellationToken.None);
            SetStatus(!_runtime.Snapshot.IsMonitoring ? "Chưa có camera bật hợp lệ; thêm camera trong tab Camera."
                : _liveViewEnabled ? "Đang giám sát cục bộ — ONVIF event hoặc YOLO fallback" : "Đang chạy nền — giám sát vẫn hoạt động, live view tạm dừng");
        }
        catch (Exception ex) { _logger.Error(LogChannel.App, "monitoring start failed", ex); SetStatus(GetMonitoringStartupError(ex)); }
    }

    private async Task StopMonitoringAsync(TimeSpan? timeout = null)
    {
        try
        {
            if (_shutdownStarted)
            {
                var result = await _monitoring.ShutdownAsync(timeout ?? TimeSpan.FromSeconds(20), CancellationToken.None);
                if (!result.Completed) { SetStatus("Chưa dừng hoàn toàn: " + result.Failure); return; }
            }
            else await _monitoring.PauseAsync(CancellationToken.None);
            SetStatus("Đã dừng giám sát");
            foreach (var status in _cameraStatuses) status.Text = "Đã dừng";
        }
        catch (Exception ex) { _logger.Error(LogChannel.App, "monitoring stop failed", ex); SetStatus("Không dừng được giám sát: " + _logger.Redact(ex.Message)); }
    }
}
