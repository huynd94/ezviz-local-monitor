using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Hosting;

public sealed class StatusFile(AppPaths paths, LogRedactor? redactor = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private readonly LogRedactor _redactor = redactor ?? new LogRedactor();
    private static readonly string[] RequiredFields = ["schemaVersion", "instanceId", "process", "updatedAtUtc", "timeZone", "version", "state", "scheduleAllowed", "cameras", "eventCount"];

    public void Write(StatusSnapshot snapshot)
    {
        var json = SerializeSafe(snapshot);
        using var root = LinuxStateFiles.OpenRoot(paths.Root, create: true);
        using var existing = LinuxStateFiles.OpenPrivateFile(root, Path.GetFileName(paths.StatusFile), create: false, allowMissing: true);
        // AtomicFile creates a 0600 temporary file, flushes, and renames. Anchor
        // the destination to our checked directory descriptor, not a second path lookup.
        AtomicFile.Write($"/proc/self/fd/{root.DangerousGetHandle()}/{Path.GetFileName(paths.StatusFile)}", Encoding.UTF8.GetBytes(json), (UnixFileMode)384);
    }

    public StatusReadResult Read(DateTimeOffset now)
    {
        try
        {
            using var root = LinuxStateFiles.OpenRoot(paths.Root, create: false);
            using var handle = LinuxStateFiles.OpenPrivateFile(root, Path.GetFileName(paths.StatusFile), create: false)!;
            using var stream = new FileStream(handle, FileAccess.Read);
            if (stream.Length > 1024 * 1024) return Invalid("Status file is too large.");
            using var document = JsonDocument.Parse(stream);
            if (!HasRequiredFields(document.RootElement, RequiredFields)) return Invalid("Status fields are missing or malformed.");
            var snapshot = document.RootElement.Deserialize<StatusSnapshot>(JsonOptions);
            if (snapshot is null || !IsValid(snapshot)) return Invalid("Status contents are invalid.");
            var safe = Sanitize(snapshot);
            if (safe.UpdatedAtUtc > now || safe.Process.StartedAtUtc > now) return Invalid("Status timestamps are in the future.");
            if (now - safe.UpdatedAtUtc > TimeSpan.FromSeconds(30)) return new(false, safe, "Status heartbeat is stale.");
            if (!ProcessIdentity.IsAlive(safe.Process)) return new(false, safe, "Status process identity is no longer live.");
            if (safe.State is not ("running" or "idle" or "outside-schedule")) return new(false, safe, "Daemon is not in a healthy state.");
            return new(true, safe, null);
        }
        // Never export exception text: malformed input and paths may themselves contain secrets.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return Invalid("Status is missing, unreadable, or malformed.");
        }
    }

    public string SerializeSafe(StatusSnapshot snapshot)
    {
        if (snapshot is null || !IsValid(snapshot)) throw new InvalidDataException("Status contents are invalid.");
        // Serialize only the status DTO; settings and arbitrary JSON fields never enter the export.
        return JsonSerializer.Serialize(Sanitize(snapshot), JsonOptions);
    }

    private StatusSnapshot Sanitize(StatusSnapshot snapshot) => snapshot with
    {
        InstanceId = _redactor.Redact(snapshot.InstanceId), TimeZone = _redactor.Redact(snapshot.TimeZone), Version = _redactor.Redact(snapshot.Version),
        Cameras = snapshot.Cameras.Select(camera => camera with { Name = _redactor.Redact(camera.Name), Message = _redactor.Redact(camera.Message) }).ToArray()
    };

    private static bool IsValid(StatusSnapshot value) => value.SchemaVersion == 1
        && !string.IsNullOrWhiteSpace(value.InstanceId) && !string.IsNullOrWhiteSpace(value.TimeZone) && !string.IsNullOrWhiteSpace(value.Version)
        && value.State is "starting" or "running" or "idle" or "outside-schedule" or "paused" or "stopping" or "stopped" or "faulted"
        && value.Process is { Pid: > 0 } && value.Process.StartedAtUtc != default && value.Process.StartedAtUtc.Offset == TimeSpan.Zero
        && value.UpdatedAtUtc != default && value.UpdatedAtUtc.Offset == TimeSpan.Zero && value.EventCount >= 0
        && value.Cameras is not null && value.Cameras.All(camera => camera is not null && camera.Id != Guid.Empty
            && camera.Name is not null && camera.Message is not null && Enum.IsDefined(camera.State) && camera.Reconnects >= 0);

    private static bool HasRequiredFields(JsonElement root, string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object || fields.Any(field => !root.TryGetProperty(field, out _))) return false;
        var process = root.GetProperty("process");
        if (process.ValueKind != JsonValueKind.Object || !process.TryGetProperty("pid", out _) || !process.TryGetProperty("startedAtUtc", out _)) return false;
        var cameras = root.GetProperty("cameras");
        return cameras.ValueKind == JsonValueKind.Array && cameras.EnumerateArray().All(camera => camera.ValueKind == JsonValueKind.Object
            && new[] { "id", "name", "state", "message", "reconnects" }.All(field => camera.TryGetProperty(field, out _)));
    }

    private static StatusReadResult Invalid(string failure) => new(false, null, failure);
}
