namespace EzvizLocalMonitor.Services;

public sealed class ActiveEventFiles
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private sealed class Entry { public int Readers; public bool Deleting; }

    public IDisposable Acquire(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_sync)
        {
            if (!_files.TryGetValue(full, out var entry)) _files[full] = entry = new Entry();
            if (entry.Deleting) throw new IOException("Event image is being deleted.");
            entry.Readers++;
        }
        return new Lease(this, full, false);
    }

    public IDisposable? TryBeginDelete(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_sync)
        {
            if (_files.ContainsKey(full)) return null;
            _files[full] = new Entry { Deleting = true };
        }
        return new Lease(this, full, true);
    }

    public bool IsInUse(string path) { lock (_sync) return _files.ContainsKey(Path.GetFullPath(path)); }
    private void Release(string path, bool deleting)
    {
        lock (_sync)
            if (_files.TryGetValue(path, out var entry) && (deleting || --entry.Readers == 0)) _files.Remove(path);
    }

    private sealed class Lease(ActiveEventFiles owner, string path, bool deleting) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(path, deleting); }
    }
}
