using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public interface IMonitoringSession : IAsyncDisposable
{
    // A shutdown can exceed its budget yet finish releasing resources later.
    // False is conservative for implementations that cannot attest completion.
    bool CleanupCompleted => false;
    Task StartAsync(AppSettings settings, CancellationToken cancellationToken);
    void SetPreviewEnabled(bool enabled);
    event Action<Guid, string>? CameraStatusChanged;
    event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged;
    // Ownership transfers to the runtime, then its single desktop preview consumer.
    // A consumer accepting the Mat owns disposal; rejected/unobserved Mats are released.
    event Action<Guid, Mat>? PreviewReady;
    event Action<DetectionEvent>? EventRecorded;
}

public sealed record ShutdownResult(bool Completed, string? Failure = null);
public sealed record RuntimeSnapshot(string State, bool IsMonitoring, bool ScheduleAllowed, int EffectiveProfile, long EventCount);
