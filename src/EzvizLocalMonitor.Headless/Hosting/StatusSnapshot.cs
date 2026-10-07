using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Headless.Hosting;

public sealed record StatusSnapshot(int SchemaVersion, string InstanceId, ProcessIdentity Process,
    DateTimeOffset UpdatedAtUtc, string TimeZone, string Version, string State, bool ScheduleAllowed,
    IReadOnlyList<CameraStatusView> Cameras, long EventCount);

public sealed record CameraStatusView(Guid Id, string Name, CameraConnectionState State, string Message, int Reconnects);

public sealed record StatusReadResult(bool IsFresh, StatusSnapshot? Snapshot, string? Failure);
