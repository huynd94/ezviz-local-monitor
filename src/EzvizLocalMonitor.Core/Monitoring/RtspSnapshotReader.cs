using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class RtspSnapshotReader(TaskRegistry work) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, VideoCapture> _captures = new();
    private bool _disposed;

    public void Warm(CameraDefinition camera)
    {
        work.TryStart(ct => Task.Run(() =>
        {
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                if (!_disposed) _ = GetOrOpen(camera);
            }
        }, ct));
    }

    public bool TryRead(CameraDefinition camera, Mat destination, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var capture = GetOrOpen(camera);
            if (capture.IsOpened() && capture.Read(destination) && !destination.Empty()) return true;
            DisposeCapture(camera.Id);
            cancellationToken.ThrowIfCancellationRequested();
            capture = GetOrOpen(camera);
            return capture.IsOpened() && capture.Read(destination) && !destination.Empty();
        }
    }

    private VideoCapture GetOrOpen(CameraDefinition camera)
    {
        if (_captures.TryGetValue(camera.Id, out var existing) && existing.IsOpened()) return existing;
        DisposeCapture(camera.Id);
        var capture = RtspCapture.Open(camera.RtspUrl);
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
            _disposed = true;
            foreach (var capture in _captures.Values) capture.Dispose();
            _captures.Clear();
        }
    }
}
