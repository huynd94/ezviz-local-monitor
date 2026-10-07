using System.Collections.Concurrent;
using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class MonitorCoordinator : IMonitoringSession
{
    private readonly EventStore _eventStore;
    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly AlertDispatcher _alerts;
    private readonly AlertQueueService _alertQueue;
    private readonly OpenAiCompatibleMovementAnalyzer _movementAnalyzer = new();
    private readonly List<CameraMonitor> _monitors = new();
    private readonly List<OnvifEventListener> _onvifListeners = new();
    private readonly RtspSnapshotReader _snapshotReader;
    private readonly TaskRegistry _work;
    private readonly CancellationTokenSource _readersStop = new();
    private readonly object _lifecycle = new();
    private readonly object _fallbackSync = new();
    private Task? _disposeTask;
    private Task? _startTask;
    private bool _stopping;
    private CancellationTokenRegistration _externalStop;
    public ActiveEventFiles ActiveFiles { get; }
    private bool _cleanupCompleted;
    public bool CleanupCompleted => Volatile.Read(ref _cleanupCompleted);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastOnvifEvents = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAcceptedDetections = new();
    private YoloPersonDetector? _detector;
    private AppSettings? _settings;
    private volatile bool _previewEnabled = true;

    public event Action<Guid, string>? CameraStatusChanged;
    public event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged;
    public event Action<Guid, Mat>? PreviewReady;
    public event Action<DetectionEvent>? EventRecorded;

    public MonitorCoordinator(AppPaths paths, EventStore eventStore, AlertDispatcher alerts, IAppLogger logger, ActiveEventFiles? activeFiles = null)
    {
        _eventStore = eventStore;
        _paths = paths;
        _logger = logger;
        _work = new TaskRegistry(logger);
        _snapshotReader = new RtspSnapshotReader(_work);
        ActiveFiles = activeFiles ?? new ActiveEventFiles();
        _alerts = alerts;
        _alertQueue = new AlertQueueService(alerts);
    }

    public void SetPreviewEnabled(bool enabled)
    {
        lock (_lifecycle)
        {
            _previewEnabled = enabled;
            foreach (var monitor in _monitors) monitor.SetPreviewEnabled(enabled);
        }
    }

    public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_startTask is not null) return _startTask;
            _settings = settings;
            _externalStop = cancellationToken.Register(() => _readersStop.Cancel());
            return _startTask = Task.Run(() => StartCoreAsync(settings));
        }
    }

    private async Task StartCoreAsync(AppSettings settings)
    {
        foreach (var camera in settings.Cameras.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.RtspUrl)))
        {
            _readersStop.Token.ThrowIfCancellationRequested();
            var password = ExtractVerificationCode(camera.RtspUrl);
            if (!string.IsNullOrWhiteSpace(camera.OnvifServiceUrl) && !string.IsNullOrWhiteSpace(password))
            {
                var listener = new OnvifEventListener(camera, password);
                listener.StatusChanged += (definition, status) => CameraStatusChanged?.Invoke(definition.Id, status);
                listener.EventReceived += OnOnvifEvent;
                listener.FallbackRequested += definition => StartFallback(definition);
                lock (_lifecycle) _onvifListeners.Add(listener);
                if (await listener.StartAsync(_readersStop.Token))
                {
                    _snapshotReader.Warm(camera);
                    continue;
                }
                await listener.DisposeAsync();
            }
            await Task.Run(() => StartFallbackCore(camera, _readersStop.Token));
        }
    }

    private void StartFallback(CameraDefinition camera)
    {
        _work.TryStart(ct =>
        {
            StartFallbackCore(camera, ct);
            return Task.CompletedTask;
        });
    }

    private void StartFallbackCore(CameraDefinition camera, CancellationToken ct)
    {
        lock (_fallbackSync)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lifecycle)
                if (_stopping || _readersStop.IsCancellationRequested || _monitors.Any(x => x.CameraId == camera.Id))
                    return;

            // Native model loading is owned work, but must not hold the lifecycle lock.
            _detector ??= new YoloPersonDetector(_paths.ModelPath);
            lock (_lifecycle)
            {
                if (_stopping || _readersStop.IsCancellationRequested) return;
                var monitor = new CameraMonitor(camera, _detector, _settings?.InferenceFpsPerCamera ?? 1,
                    _settings?.ConfirmationsRequired ?? 2, _settings?.ConfirmationWindow ?? 3, _logger);
                monitor.StatusChanged += (definition, status) => CameraStatusChanged?.Invoke(definition.Id, status);
                monitor.RuntimeChanged += (definition, runtime) => CameraRuntimeChanged?.Invoke(definition.Id, runtime);
                monitor.SetPreviewEnabled(_previewEnabled);
                monitor.PreviewReady += (definition, image) => PublishPreview(definition.Id, image);
                monitor.PersonConfirmed += OnPersonConfirmed;
                _monitors.Add(monitor);
                monitor.Start();
            }
        }
    }

    private void PublishPreview(Guid cameraId, Mat image)
    {
        var handler = PreviewReady;
        if (handler is null) { image.Dispose(); return; }
        try { handler(cameraId, image); }
        catch { image.Dispose(); throw; }
    }

    private void OnOnvifEvent(CameraDefinition camera, OnvifEvent onvifEvent)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastOnvifEvents.TryGetValue(camera.Id, out var previous) &&
            (now - previous).TotalSeconds < Math.Max(1, camera.CooldownSeconds)) return;
        _lastOnvifEvents[camera.Id] = now;
        _work.TryStart(ct => Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var frame = new Mat();
                if (!_snapshotReader.TryRead(camera, frame, ct))
                {
                    CameraStatusChanged?.Invoke(camera.Id, "ONVIF có event nhưng không đọc được RTSP xác minh");
                    return;
                }

                if (_previewEnabled) PublishPreview(camera.Id, frame.Clone());
                var snapshot = frame.Clone();
                var confidence = onvifEvent.IsHuman ? 0.95 : 0.75;
                var detection = new PersonDetection(confidence, 0, 0, frame.Width, frame.Height);
                var label = onvifEvent.IsHuman ? "ONVIF Human shape detection" : "ONVIF Motion alarm";
                QueueDetection(camera, snapshot, null, detection, label, onvifEvent.IsHuman);
                CameraStatusChanged?.Invoke(camera.Id, label);
            }
            catch (Exception ex)
            {
                _logger.Error(LogChannel.Camera, $"camera={camera.Name}; ONVIF/RTSP verification failed", ex);
                CameraStatusChanged?.Invoke(camera.Id, "Lỗi xác minh ONVIF/RTSP: " + SafeMessage(ex));
            }
        }, ct));
    }

    private void OnPersonConfirmed(CameraDefinition camera, Mat snapshot, Mat? previousSnapshot, PersonDetection detection)
        => QueueDetection(camera, snapshot, previousSnapshot, detection, "YOLO cục bộ", true);

    private void QueueDetection(CameraDefinition camera, Mat snapshot, Mat? previousSnapshot, PersonDetection detection,
        string detectionSource, bool isHumanDetection)
    {
        if (!TryAcceptDetection(camera))
        {
            previousSnapshot?.Dispose();
            snapshot.Dispose();
            CameraStatusChanged?.Invoke(camera.Id, "Bỏ qua cảnh báo trùng trong khoảng im lặng");
            return;
        }

        if (!_work.TryStart(async ct =>
        {
            string? previousImagePath = null;
            long? eventId = null;
            IDisposable? imageLease = null;
            IDisposable? previousLease = null;
            try
            {
                _paths.EnsureDirectories();
                var directory = Path.Combine(_paths.EventImages, DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(directory);
                var imagePath = Path.Combine(directory, $"{DateTime.Now:HHmmss}_{camera.Id:N}.jpg");
                previousImagePath = previousSnapshot is null ? null : Path.Combine(directory, $"{DateTime.Now:HHmmss}_{camera.Id:N}_before.jpg");
                imageLease = ActiveFiles.Acquire(imagePath);
                previousLease = previousImagePath is null ? null : ActiveFiles.Acquire(previousImagePath);
                SaveAlertJpeg(imagePath, snapshot);
                if (previousSnapshot is not null && previousImagePath is not null) SaveAlertJpeg(previousImagePath, previousSnapshot);

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
                eventId = item.Id;
                _logger.Info(LogChannel.Alerts, $"event recorded; eventId={item.Id}; camera={camera.Name}; source={detectionSource}; human={isHumanDetection}");
                EventRecorded?.Invoke(item);

                var aiEnabled = _settings?.Ai.Enabled == true;
                var requiresAiConfirmation = aiEnabled && _settings!.Ai.RequireConfirmationBeforeAlert;
                // Phân tích phải hoàn tất trước khi enqueue để caption Telegram/Zalo
                // nhận được AiSummary; tùy chọn confirmation chỉ quyết định có lọc cảnh báo hay không.
                AiMovementAnalysis? analysis = aiEnabled ? await AnalyzeAndUpdateAsync(item, previousImagePath, ct) : null;
                ct.ThrowIfCancellationRequested();
                var shouldSend = !requiresAiConfirmation || analysis?.ShouldSendAlert == true;
                var status = !shouldSend
                    ? "Không gửi: AI không thấy chuyển động/người"
                    : _settings is null ? "Không có cấu hình cảnh báo" : await _alertQueue.EnqueueAsync(_settings.Alerts, item, ct);
                _logger.Info(LogChannel.Alerts, $"alert queued result; eventId={item.Id}; status={status}");
                item.DeliveryStatus = status;
                _eventStore.UpdateDeliveryStatus(item.Id, status);
                EventRecorded?.Invoke(item);

            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (eventId is long id) _eventStore.UpdateDeliveryStatus(id, "Chưa gửi (đã dừng)");
            }
            catch (Exception ex)
            {
                if (eventId is long id) _eventStore.UpdateDeliveryStatus(id, "Chưa gửi (xử lý cảnh báo lỗi)");
                _logger.Error(LogChannel.Alerts, "alert processing failed", ex);
                CameraStatusChanged?.Invoke(camera.Id, $"Lỗi xử lý cảnh báo: {SafeMessage(ex)}");
            }
            finally
            {
                try { if (previousImagePath is not null && File.Exists(previousImagePath)) File.Delete(previousImagePath); }
                finally
                {
                    previousSnapshot?.Dispose();
                    snapshot.Dispose();
                    previousLease?.Dispose();
                    imageLease?.Dispose();
                }
            }
        }))
        {
            previousSnapshot?.Dispose();
            snapshot.Dispose();
        }
    }

    private bool TryAcceptDetection(CameraDefinition camera)
    {
        var now = DateTimeOffset.Now;
        var cooldown = TimeSpan.FromSeconds(Math.Max(1, camera.CooldownSeconds));
        while (true)
        {
            if (_lastAcceptedDetections.TryGetValue(camera.Id, out var previous) && now - previous < cooldown)
                return false;
            if (_lastAcceptedDetections.TryUpdate(camera.Id, now, previous)) return true;
            if (_lastAcceptedDetections.TryAdd(camera.Id, now)) return true;
        }
    }

    private static void SaveAlertJpeg(string path, Mat source)
    {
        using var resized = new Mat();
        var scale = Math.Min(1.0, 1280.0 / Math.Max(source.Width, source.Height));
        if (scale < 1.0)
            Cv2.Resize(source, resized, new OpenCvSharp.Size((int)(source.Width * scale), (int)(source.Height * scale)), 0, 0, InterpolationFlags.Area);
        else
            source.CopyTo(resized);
        if (!Cv2.ImWrite(path, resized) || !File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidDataException("Không tạo được ảnh JPEG sự kiện hoặc tệp ảnh bị rỗng.");
    }

    private async Task<AiMovementAnalysis?> AnalyzeAndUpdateAsync(DetectionEvent item, string? previousImagePath, CancellationToken ct)
    {
        if (_settings?.Ai.Enabled != true) return null;
        try
        {
            _logger.Info(LogChannel.Ai, $"analysis start; eventId={item.Id}; camera={item.CameraName}; model={_logger.Redact(_settings.Ai.Model)}; previousImage={previousImagePath is not null}");
            var analysis = await _movementAnalyzer.AnalyzeAsync(_settings.Ai, item.ImagePath, previousImagePath, ct);
            _logger.Info(LogChannel.Ai, $"analysis result; eventId={item.Id}; status={analysis.Status}; motion={analysis.MotionDetected}; person={analysis.PersonPresent}; confidence={analysis.Confidence:0.00}");
            item.AiStatus = analysis.Status;
            item.AiMotionDetected = analysis.MotionDetected;
            item.AiPersonPresent = analysis.PersonPresent;
            item.AiConfidence = analysis.Confidence;
            item.AiSummary = analysis.Summary;
            _eventStore.UpdateAiAnalysis(item.Id, analysis);
            EventRecorded?.Invoke(item);
            return analysis;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.Error(LogChannel.Ai, $"analysis failed; eventId={item.Id}; camera={item.CameraName}", ex);
            item.AiStatus = "Lỗi AI: " + SafeMessage(ex);
            item.AiSummary = "Không nhận được kết quả phân tích AI.";
            _eventStore.UpdateAiStatus(item.Id, item.AiStatus);
            EventRecorded?.Invoke(item);
            return null;
        }
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

    private string SafeMessage(Exception ex) => _logger.Redact(ex.Message);

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _stopping = true;
            _work.CloseAdmission();
            _readersStop.Cancel();
            return new ValueTask(_disposeTask = Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Start the owned-work deadline with reader shutdown, not after it.
        var drain = _work.CompleteAsync(TimeSpan.FromSeconds(20));
        if (_startTask is not null)
        {
            try { await _startTask; }
            catch (OperationCanceledException) when (_readersStop.IsCancellationRequested) { }
            catch (Exception ex) { _logger.Error(LogChannel.App, "monitoring startup failed", ex); }
        }
        OnvifEventListener[] listeners;
        CameraMonitor[] monitors;
        lock (_lifecycle)
        {
            listeners = _onvifListeners.ToArray();
            monitors = _monitors.ToArray();
            _onvifListeners.Clear();
            _monitors.Clear();
        }
        foreach (var listener in listeners) await listener.DisposeAsync();
        foreach (var monitor in monitors) await monitor.DisposeAsync();
        var drainedInBudget = await drain;
        if (!drainedInBudget)
        {
            _logger.Error(LogChannel.App, "Monitoring work exceeded its shutdown deadline; native resources remain owned until work exits.");
            // The caller reports incomplete shutdown at its 20-second deadline. Keep
            // this disposal task alive so desktop cleanup can finish safely later.
            await _work.CompleteAsync(Timeout.InfiniteTimeSpan);
        }
        await _work.DisposeAsync();
        await _alertQueue.DisposeAsync();
        _detector?.Dispose();
        _detector = null;
        _snapshotReader.Dispose();
        _lastOnvifEvents.Clear();
        _lastAcceptedDetections.Clear();
        _externalStop.Dispose();
        _readersStop.Dispose();
        Volatile.Write(ref _cleanupCompleted, true);
        if (!drainedInBudget)
            throw new TimeoutException("Monitoring work exceeded its shutdown deadline; late cleanup has completed.");
    }
}
