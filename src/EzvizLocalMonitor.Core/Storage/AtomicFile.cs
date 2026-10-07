namespace EzvizLocalMonitor.Services;

public static class AtomicFile
{
    public static void Write(string destination, byte[] bytes, UnixFileMode? mode = null, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (OperatingSystem.IsLinux()) options.UnixCreateMode = mode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }
}
