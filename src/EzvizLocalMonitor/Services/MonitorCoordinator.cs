using System.Collections.Concurrent;
using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class MonitorCoordinator : IAsyncDisposable
{
    private readonly EventStore _eventStore;
    private readonly AlertDispatcher _alerts;
    private readonly OpenAiCompatibleMovementAnalyzer _movementAnalyzer = new();
    private readonly List<CameraMonitor> _monitors = new();
    private readonly List<OnvifEventListener> _onvifListeners = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastOnvifEvents = new();
    private YoloPersonDetector? _detector;
    private AppSettings? _settings;
    private bool _started;

    public event Action<Guid, string>? CameraStatusChanged;
    public event Action<Guid, Mat>? PreviewReady;
    public event Action<DetectionEvent>? EventRecorded;

    public MonitorCoordinator(EventStore eventStore, AlertDispatcher alerts)
    {
        _eventStore = eventStore;
        _alerts = alerts;
    }

    public async Task StartAsync(AppSettings settings)
    {
        if (_started) return;
        _started = true;
        _settings = settings;

        foreach (var camera in settings.Cameras.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl)))
        {
            var password = ExtractVerificationCode(camera.RtspUrl);
            if (!string.IsNullOrWhiteSpace(camera.OnvifServiceUrl) && !string.IsNullOrWhiteSpace(password))
            {
                var listener = new OnvifEventListener(camera, password);
                listener.StatusChanged += (definition, status) => CameraStatusChanged?.Invoke(definition.Id, status);
                listener.EventReceived += OnOnvifEvent;
                listener.FallbackRequested += definition => StartFallback(definition);
                if (await listener.StartAsync())
                {
                    _onvifListeners.Add(listener);
                    continue;
                }
                await listener.DisposeAsync();
            }
            StartFallback(camera);
        }
    }

    private void StartFallback(CameraDefinition camera)
    {
        if (_monitors.Any(x => x.CameraId == camera.Id)) return;
        _detector ??= new YoloPersonDetector();
        var monitor = new CameraMonitor(camera, _detector, _settings?.InferenceFpsPerCamera ?? 1,
            _settings?.ConfirmationsRequired ?? 2, _settings?.ConfirmationWindow ?? 3);
        monitor.StatusChanged += (definition, status) => CameraStatusChanged?.Invoke(definition.Id, status);
        monitor.PreviewReady += (definition, image) => PreviewReady?.Invoke(definition.Id, image);
        monitor.PersonConfirmed += OnPersonConfirmed;
        _monitors.Add(monitor);
        monitor.Start();
    }

    private void OnOnvifEvent(CameraDefinition camera, OnvifEvent onvifEvent)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastOnvifEvents.TryGetValue(camera.Id, out var previous) &&
            (now - previous).TotalSeconds < Math.Max(1, camera.CooldownSeconds)) return;
        _lastOnvifEvents[camera.Id] = now;
        _ = Task.Run(() =>
        {
            try
            {
                using var capture = new VideoCapture(camera.RtspUrl, VideoCaptureAPIs.FFMPEG);
                using var frame = new Mat();
                if (!capture.IsOpened() || !capture.Read(frame) || frame.Empty())
                {
                    CameraStatusChanged?.Invoke(camera.Id, "ONVIF có event nhưng không đọc được RTSP xác minh");
                    return;
                }

                PreviewReady?.Invoke(camera.Id, frame.Clone());
                var snapshot = frame.Clone();
                var confidence = onvifEvent.IsHuman ? 0.95 : 0.75;
                var detection = new PersonDetection(confidence, 0, 0, frame.Width, frame.Height);
                var label = onvifEvent.IsHuman ? "ONVIF Human shape detection" : "ONVIF Motion alarm";
                QueueDetection(camera, snapshot, null, detection, label, onvifEvent.IsHuman);
                CameraStatusChanged?.Invoke(camera.Id, label);
            }
            catch (Exception ex)
            {
                CameraStatusChanged?.Invoke(camera.Id, "Lỗi xác minh ONVIF/RTSP: " + SafeMessage(ex));
            }
        });
    }

    private void OnPersonConfirmed(CameraDefinition camera, Mat snapshot, Mat? previousSnapshot, PersonDetection detection)
        => QueueDetection(camera, snapshot, previousSnapshot, detection, "YOLO cục bộ", true);

    private void QueueDetection(CameraDefinition camera, Mat snapshot, Mat? previousSnapshot, PersonDetection detection,
        string detectionSource, bool isHumanDetection)
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
                    ImagePath = imagePath,
                    DetectionSource = detectionSource,
                    IsHumanDetection = isHumanDetection
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
                CameraStatusChanged?.Invoke(camera.Id, $"Lỗi xử lý cảnh báo: {SafeMessage(ex)}");
            }
            finally
            {
                if (previousImagePath is not null && File.Exists(previousImagePath)) File.Delete(previousImagePath);
                previousSnapshot?.Dispose();
                snapshot.Dispose();
            }
        });
    }

    private static string? ExtractVerificationCode(string rtspUrl)
    {
        if (!Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri)) return null;
        var userInfo = uri.UserInfo;
        var separator = userInfo.IndexOf(':');
        return separator >= 0 && separator + 1 < userInfo.Length
            ? Uri.UnescapeDataString(userInfo[(separator + 1)..])
            : null;
    }

    private static string SafeMessage(Exception ex) => ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in _onvifListeners) await listener.DisposeAsync();
        _onvifListeners.Clear();
        foreach (var monitor in _monitors) await monitor.DisposeAsync();
        _monitors.Clear();
        _detector?.Dispose();
        _detector = null;
        _lastOnvifEvents.Clear();
        _started = false;
    }
}
