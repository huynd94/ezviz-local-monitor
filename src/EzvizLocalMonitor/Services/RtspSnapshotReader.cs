using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class RtspSnapshotReader : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, VideoCapture> _captures = new();

    public void Warm(CameraDefinition camera)
    {
        _ = Task.Run(() =>
        {
            try
            {
                lock (_sync) _ = GetOrOpen(camera);
            }
            catch
            {
                // TryRead sẽ thực hiện mở lại và báo trạng thái nếu camera chưa sẵn sàng.
            }
        });
    }

    public bool TryRead(CameraDefinition camera, Mat destination)
    {
        lock (_sync)
        {
            var capture = GetOrOpen(camera);
            if (capture.IsOpened() && capture.Read(destination) && !destination.Empty()) return true;
            DisposeCapture(camera.Id);
            capture = GetOrOpen(camera);
            return capture.IsOpened() && capture.Read(destination) && !destination.Empty();
        }
    }

    private VideoCapture GetOrOpen(CameraDefinition camera)
    {
        if (_captures.TryGetValue(camera.Id, out var existing) && existing.IsOpened()) return existing;
        DisposeCapture(camera.Id);
        var capture = new VideoCapture(camera.RtspUrl, VideoCaptureAPIs.FFMPEG);
        capture.Set(VideoCaptureProperties.BufferSize, 1);
        _captures[camera.Id] = capture;
        return capture;
    }

    private void DisposeCapture(Guid id)
    {
        if (_captures.Remove(id, out var capture)) capture.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var capture in _captures.Values) capture.Dispose();
            _captures.Clear();
        }
    }
}
