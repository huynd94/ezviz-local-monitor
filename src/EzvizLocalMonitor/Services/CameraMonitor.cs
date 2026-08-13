using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class CameraMonitor : IAsyncDisposable
{
    private readonly CameraDefinition _camera;
    private readonly YoloPersonDetector _detector;
    private readonly int _fps;
    private readonly int _requiredConfirmations;
    private readonly int _confirmationWindow;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<bool> _recentHits = new();
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;
    private Task? _runTask;

    public event Action<CameraDefinition, Mat, PersonDetection>? PersonConfirmed;
    public event Action<CameraDefinition, string>? StatusChanged;
    public event Action<CameraDefinition, Mat>? PreviewReady;

    public CameraMonitor(CameraDefinition camera, YoloPersonDetector detector, int fps, int requiredConfirmations, int confirmationWindow)
    {
        _camera = camera;
        _detector = detector;
        _fps = Math.Clamp(fps, 1, 3);
        _requiredConfirmations = Math.Clamp(requiredConfirmations, 1, confirmationWindow);
        _confirmationWindow = Math.Clamp(confirmationWindow, _requiredConfirmations, 5);
    }

    public void Start() => _runTask ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var reconnectDelay = TimeSpan.FromSeconds(2);
        while (!_stop.Token.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke(_camera, "Đang kết nối...");
                using var capture = new VideoCapture(_camera.RtspUrl, VideoCaptureAPIs.FFMPEG);
                if (!capture.IsOpened()) throw new InvalidOperationException("Không mở được luồng RTSP.");

                reconnectDelay = TimeSpan.FromSeconds(2);
                StatusChanged?.Invoke(_camera, "Đang giám sát");
                using var frame = new Mat();
                while (!_stop.Token.IsCancellationRequested && capture.Read(frame) && !frame.Empty())
                {
                    var preview = frame.Clone();
                    PreviewReady?.Invoke(_camera, preview);

                    var detections = _detector.Detect(frame, _camera.ConfidenceThreshold);
                    var person = detections.FirstOrDefault(x => IsInsideRoi(x, frame.Width, frame.Height));
                    var found = person is not null;
                    _recentHits.Enqueue(found);
                    while (_recentHits.Count > _confirmationWindow) _recentHits.Dequeue();

                    if (found && _recentHits.Count(x => x) >= _requiredConfirmations &&
                        DateTimeOffset.Now - _lastAlert >= TimeSpan.FromSeconds(_camera.CooldownSeconds))
                    {
                        _lastAlert = DateTimeOffset.Now;
                        var snapshot = frame.Clone();
                        DrawDetection(snapshot, person!);
                        PersonConfirmed?.Invoke(_camera, snapshot, person!);
                        _recentHits.Clear();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1d / _fps), _stop.Token);
                }

                StatusChanged?.Invoke(_camera, "Mất luồng; sẽ kết nối lại");
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(_camera, $"Lỗi kết nối: {ex.Message}");
            }

            try
            {
                await Task.Delay(reconnectDelay, _stop.Token);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(30, reconnectDelay.TotalSeconds * 2));
            }
            catch (OperationCanceledException) { break; }
        }

        StatusChanged?.Invoke(_camera, "Đã dừng");
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
        _stop.Dispose();
    }
}
