using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class MonitorCoordinator : IAsyncDisposable
{
    private readonly EventStore _eventStore;
    private readonly AlertDispatcher _alerts;
    private readonly OpenAiCompatibleMovementAnalyzer _movementAnalyzer = new();
    private readonly List<CameraMonitor> _monitors = new();
    private YoloPersonDetector? _detector;
    private AppSettings? _settings;

    public event Action<Guid, string>? CameraStatusChanged;
    public event Action<Guid, Mat>? PreviewReady;
    public event Action<DetectionEvent>? EventRecorded;

    public MonitorCoordinator(EventStore eventStore, AlertDispatcher alerts)
    {
        _eventStore = eventStore;
        _alerts = alerts;
    }

    public void Start(AppSettings settings)
    {
        if (_detector is not null) return;
        _settings = settings;
        _detector = new YoloPersonDetector();

        foreach (var camera in settings.Cameras.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl)))
        {
            var monitor = new CameraMonitor(camera, _detector, settings.InferenceFpsPerCamera,
                settings.ConfirmationsRequired, settings.ConfirmationWindow);
            monitor.StatusChanged += (definition, status) => CameraStatusChanged?.Invoke(definition.Id, status);
            monitor.PreviewReady += (definition, image) => PreviewReady?.Invoke(definition.Id, image);
            monitor.PersonConfirmed += OnPersonConfirmed;
            _monitors.Add(monitor);
            monitor.Start();
        }
    }

    private void OnPersonConfirmed(CameraDefinition camera, Mat snapshot, Mat? previousSnapshot, PersonDetection detection)
    {
        _ = Task.Run(async () =>
        {
            string? previousImagePath = null;
            try
            {
                DataPaths.EnsureCreated();
                var directory = Path.Combine(DataPaths.EventImages, DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(directory);
                var imagePath = Path.Combine(directory, $"{DateTime.Now:HHmmss}_{camera.Id:N}.jpg");
                previousImagePath = previousSnapshot is null ? null : Path.Combine(directory, $"{DateTime.Now:HHmmss}_{camera.Id:N}_before.jpg");
                Cv2.ImWrite(imagePath, snapshot);
                if (previousSnapshot is not null && previousImagePath is not null) Cv2.ImWrite(previousImagePath, previousSnapshot);

                var item = new DetectionEvent
                {
                    CameraId = camera.Id,
                    CameraName = camera.Name,
                    DetectedAt = DateTimeOffset.Now,
                    Confidence = detection.Confidence,
                    ImagePath = imagePath
                };
                item.Id = _eventStore.Add(item);
                EventRecorded?.Invoke(item);

                AiMovementAnalysis? analysis = null;
                if (_settings?.Ai.Enabled == true)
                {
                    try
                    {
                        analysis = await _movementAnalyzer.AnalyzeAsync(_settings.Ai, item.ImagePath, previousImagePath);
                        item.AiStatus = analysis.Status;
                        item.AiMotionDetected = analysis.MotionDetected;
                        item.AiPersonPresent = analysis.PersonPresent;
                        item.AiConfidence = analysis.Confidence;
                        item.AiSummary = analysis.Summary;
                        _eventStore.UpdateAiAnalysis(item.Id, analysis);
                        EventRecorded?.Invoke(item);
                    }
                    catch (Exception ex)
                    {
                        item.AiStatus = "Lỗi AI: " + ex.Message;
                        _eventStore.UpdateAiStatus(item.Id, item.AiStatus);
                        EventRecorded?.Invoke(item);
                    }
                }

                var shouldSend = _settings is null || !_settings.Ai.Enabled || !_settings.Ai.RequireConfirmationBeforeAlert ||
                    analysis is null || analysis.ShouldSendAlert;
                var status = !shouldSend
                    ? "Không gửi: AI không thấy chuyển động/người"
                    : _settings is null ? "Không có cấu hình cảnh báo" : await _alerts.SendAsync(_settings.Alerts, item);
                item.DeliveryStatus = status;
                _eventStore.UpdateDeliveryStatus(item.Id, status);
                EventRecorded?.Invoke(item);

            }
            catch (Exception ex)
            {
                CameraStatusChanged?.Invoke(camera.Id, $"Lỗi xử lý cảnh báo: {ex.Message}");
            }
            finally
            {
                if (previousImagePath is not null && File.Exists(previousImagePath)) File.Delete(previousImagePath);
                previousSnapshot?.Dispose();
                snapshot.Dispose();
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var monitor in _monitors) await monitor.DisposeAsync();
        _monitors.Clear();
        _detector?.Dispose();
        _detector = null;
    }
}
