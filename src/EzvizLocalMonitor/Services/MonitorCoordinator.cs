using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class MonitorCoordinator : IAsyncDisposable
{
    private readonly EventStore _eventStore;
    private readonly AlertDispatcher _alerts;
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

    private void OnPersonConfirmed(CameraDefinition camera, Mat snapshot, PersonDetection detection)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                DataPaths.EnsureCreated();
                var directory = Path.Combine(DataPaths.EventImages, DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(directory);
                var imagePath = Path.Combine(directory, $"{DateTime.Now:HHmmss}_{camera.Id:N}.jpg");
                Cv2.ImWrite(imagePath, snapshot);

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

                var status = _settings is null ? "Không có cấu hình cảnh báo" :
                    await _alerts.SendAsync(_settings.Alerts, item);
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
