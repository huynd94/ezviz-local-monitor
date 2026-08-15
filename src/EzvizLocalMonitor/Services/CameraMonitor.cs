using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class CameraMonitor : IAsyncDisposable
{
    private readonly CameraDefinition _camera;
    private readonly YoloPersonDetector _detector;
    private readonly int _fps;
    private readonly TimeSpan _inferenceInterval;
    private readonly TimeSpan _previewInterval;
    private readonly int _requiredConfirmations;
    private readonly int _confirmationWindow;
    private readonly double _minPresenceSeconds;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _inferenceSync = new();
    private readonly Queue<bool> _recentHits = new();
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastFrameAt;
    private int _reconnectCount;
    private CameraConnectionState _state = CameraConnectionState.Stopped;
    private Task? _runTask;
    private Mat? _previousInferenceFrame;
    private DateTimeOffset? _presenceSince;
    private DateTimeOffset _lastInferenceAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPreviewAt = DateTimeOffset.MinValue;
    private Mat? _pendingInferenceFrame;
    private Task? _inferenceWorker;
    private bool _inferenceWorkerRunning;
    private bool _previewEnabled = true;

    public Guid CameraId => _camera.Id;

    public void SetPreviewEnabled(bool enabled) => Volatile.Write(ref _previewEnabled, enabled);

    public event Action<CameraDefinition, Mat, Mat?, PersonDetection>? PersonConfirmed;
    public event Action<CameraDefinition, string>? StatusChanged;
    public event Action<CameraDefinition, Mat>? PreviewReady;
    public event Action<CameraDefinition, CameraRuntimeSnapshot>? RuntimeChanged;

    public CameraMonitor(CameraDefinition camera, YoloPersonDetector detector, int fps, int requiredConfirmations, int confirmationWindow, int previewFps = 5)
    {
        _camera = camera;
        _detector = detector;
        _fps = Math.Clamp(fps, 1, 3);
        _inferenceInterval = TimeSpan.FromSeconds(1d / _fps);
        _previewInterval = TimeSpan.FromSeconds(1d / Math.Clamp(previewFps, 3, 5));
        _requiredConfirmations = Math.Clamp(requiredConfirmations, 1, confirmationWindow);
        _confirmationWindow = Math.Clamp(confirmationWindow, _requiredConfirmations, 5);
        _minPresenceSeconds = Math.Clamp(camera.MinPresenceSeconds, 0, 30);
    }

    public void Start() => _runTask ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var reconnectDelay = TimeSpan.FromSeconds(2);
        SetState(CameraConnectionState.Connecting, "Đang khởi tạo luồng RTSP");
        while (!_stop.Token.IsCancellationRequested)
        {
            try
            {
                SetState(CameraConnectionState.Connecting, "Đang kết nối RTSP...");
                StatusChanged?.Invoke(_camera, "Đang kết nối...");
                using var capture = new VideoCapture(_camera.RtspUrl, VideoCaptureAPIs.FFMPEG);
                capture.Set(VideoCaptureProperties.BufferSize, 1);
                if (!capture.IsOpened()) throw new InvalidOperationException("Không mở được luồng RTSP.");

                reconnectDelay = TimeSpan.FromSeconds(2);
                SetState(CameraConnectionState.Streaming, "Đang giám sát");
                StatusChanged?.Invoke(_camera, "Đang giám sát");
                _lastInferenceAt = DateTimeOffset.MinValue;
                _lastPreviewAt = DateTimeOffset.MinValue;
                using var frame = new Mat();
                while (!_stop.Token.IsCancellationRequested && capture.Read(frame) && !frame.Empty())
                {
                    _lastFrameAt = DateTimeOffset.Now;
                    if (_state != CameraConnectionState.Streaming)
                        SetState(CameraConnectionState.Streaming, "Đang nhận frame");

                    // Preview có nhịp riêng, không bị giới hạn bởi tần suất YOLO.
                    // Chỉ clone theo nhịp preview; resize và JPEG encode thực hiện ở
                    // worker UI để thread đọc RTSP không bị chặn.
                    var now = DateTimeOffset.UtcNow;
                    if (Volatile.Read(ref _previewEnabled) && now - _lastPreviewAt >= _previewInterval)
                    {
                        _lastPreviewAt = now;
                        PreviewReady?.Invoke(_camera, frame.Clone());
                    }

                    // Chỉ xếp frame mới nhất cho worker YOLO. Nếu detector đang bận,
                    // frame cũ trong hàng đợi sẽ bị thay thế thay vì làm tăng độ trễ.
                    if (now - _lastInferenceAt >= _inferenceInterval)
                    {
                        _lastInferenceAt = now;
                        QueueInferenceFrame(frame);
                    }
                }

                _reconnectCount++;
                AppLogger.Info(LogChannel.Camera, $"camera={_camera.Name}; frame loop ended; reconnect={_reconnectCount}");
                SetState(CameraConnectionState.Reconnecting, "Mất luồng; sẽ kết nối lại");
                StatusChanged?.Invoke(_camera, "Mất luồng; sẽ kết nối lại");
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _reconnectCount++;
                AppLogger.Error(LogChannel.Camera, $"camera={_camera.Name}; RTSP exception", ex);
                SetState(CameraConnectionState.Degraded, $"Lỗi kết nối: {ex.Message}");
                StatusChanged?.Invoke(_camera, $"Lỗi kết nối: {ex.Message}");
            }

            SetState(CameraConnectionState.Reconnecting, $"Đang thử lại sau {reconnectDelay.TotalSeconds:0}s");
            try
            {
                await Task.Delay(reconnectDelay, _stop.Token);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(30, reconnectDelay.TotalSeconds * 2));
            }
            catch (OperationCanceledException) { break; }
        }

        SetState(CameraConnectionState.Stopped, "Đã dừng");
        StatusChanged?.Invoke(_camera, "Đã dừng");
    }

    private void SetState(CameraConnectionState state, string message)
    {
        _state = state;
        AppLogger.Info(LogChannel.Camera, $"camera={_camera.Name}; state={state}; reconnects={_reconnectCount}; message={message}");
        try
        {
            RuntimeChanged?.Invoke(_camera, new CameraRuntimeSnapshot(state, _lastFrameAt, _reconnectCount, message));
        }
        catch
        {
            // Runtime telemetry must never stop the camera loop.
        }
    }

    private bool IsInsideRoi(PersonDetection detection, int frameWidth, int frameHeight)
    {
        // The detected person's center must fall in the configured monitoring region.
        var left = frameWidth * _camera.RoiLeftPercent / 100;
        var top = frameHeight * _camera.RoiTopPercent / 100;
        var right = frameWidth * _camera.RoiRightPercent / 100;
        var bottom = frameHeight * _camera.RoiBottomPercent / 100;
        return detection.CenterX >= left && detection.CenterX <= right &&
               detection.CenterY >= top && detection.CenterY <= bottom;
    }

    private void QueueInferenceFrame(Mat frame)
    {
        lock (_inferenceSync)
        {
            _pendingInferenceFrame?.Dispose();
            _pendingInferenceFrame = frame.Clone();
            if (_inferenceWorkerRunning) return;
            _inferenceWorkerRunning = true;
            _inferenceWorker = Task.Run(ProcessInferenceQueueAsync);
        }
    }

    private Task ProcessInferenceQueueAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Mat? frame;
            lock (_inferenceSync)
            {
                frame = _pendingInferenceFrame;
                _pendingInferenceFrame = null;
                if (frame is null)
                {
                    _inferenceWorkerRunning = false;
                    return Task.CompletedTask;
                }
            }

            try
            {
                using (frame)
                using (var priorFrame = _previousInferenceFrame?.Clone())
                {
                    var detections = _detector.Detect(frame, _camera.ConfidenceThreshold);
                    var person = detections.FirstOrDefault(x => IsInsideRoi(x, frame.Width, frame.Height));
                    var found = person is not null;
                    _recentHits.Enqueue(found);
                    while (_recentHits.Count > _confirmationWindow) _recentHits.Dequeue();
                    if (found) _presenceSince ??= DateTimeOffset.Now;
                    else _presenceSince = null;

                    var presenceDuration = _presenceSince is null ? TimeSpan.Zero : DateTimeOffset.Now - _presenceSince.Value;
                    if (found && _recentHits.Count(x => x) >= _requiredConfirmations &&
                        presenceDuration.TotalSeconds >= _minPresenceSeconds &&
                        DateTimeOffset.Now - _lastAlert >= TimeSpan.FromSeconds(_camera.CooldownSeconds))
                    {
                        _lastAlert = DateTimeOffset.Now;
                        var snapshot = frame.Clone();
                        DrawDetection(snapshot, person!);
                        PersonConfirmed?.Invoke(_camera, snapshot, priorFrame?.Clone(), person!);
                        _recentHits.Clear();
                        _presenceSince = null;
                    }

                    _previousInferenceFrame?.Dispose();
                    _previousInferenceFrame = frame.Clone();
                }
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLogger.Error(LogChannel.Camera, $"camera={_camera.Name}; YOLO worker exception", ex);
            }
        }

        lock (_inferenceSync)
        {
            _inferenceWorkerRunning = false;
        }
        return Task.CompletedTask;
    }

    private static void DrawDetection(Mat image, PersonDetection person)
    {
        Cv2.Rectangle(image, new Rect(person.Left, person.Top, person.Width, person.Height), Scalar.LimeGreen, 3);
        Cv2.PutText(image, $"Person {person.Confidence:P0}", new Point(person.Left, Math.Max(24, person.Top - 8)),
            HersheyFonts.HersheySimplex, 0.8, Scalar.LimeGreen, 2);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_runTask is not null)
        {
            try { await _runTask; } catch (OperationCanceledException) { }
        }
        if (_inferenceWorker is not null)
        {
            try { await _inferenceWorker; } catch (OperationCanceledException) { }
        }
        lock (_inferenceSync)
        {
            _pendingInferenceFrame?.Dispose();
            _pendingInferenceFrame = null;
            _inferenceWorkerRunning = false;
        }
        _previousInferenceFrame?.Dispose();
        _presenceSince = null;
        _stop.Dispose();
    }
}
