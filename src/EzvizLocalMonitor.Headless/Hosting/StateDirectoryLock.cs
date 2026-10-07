using System.ComponentModel;
using System.Runtime.InteropServices;
using EzvizLocalMonitor.Services;
using Microsoft.Win32.SafeHandles;

namespace EzvizLocalMonitor.Headless.Hosting;

public static class StateDirectoryLock
{
    public static IDisposable Acquire(AppPaths paths)
    {
        using var root = LinuxStateFiles.OpenRoot(paths.Root, create: true);
        var file = LinuxStateFiles.OpenPrivateFile(root, Path.GetFileName(paths.LockFile), create: true)!;
        try
        {
            // flock locks the open file description, unlike process-associated fcntl locks.
            while (Flock(file, 2 | 4) != 0) // LOCK_EX | LOCK_NB
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == 4) continue; // EINTR
                if (error == 11) throw new StateInUseException("The state directory is already in use."); // EWOULDBLOCK
                throw LinuxStateFiles.IoError("Cannot lock the state directory", error);
            }
            // Closing the descriptor releases the lock. Never unlink the shared inode.
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle fd, int operation);
}

// Descriptor-relative access keeps checks and use on the same inode. statx has a
// fixed Linux ABI, avoiding the architecture-dependent layout of struct stat.
internal static class LinuxStateFiles
{
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int NonBlocking = 0x800;
    private const int DirectoryFlag = 0x10000;

    internal static SafeFileHandle OpenRoot(string path, bool create)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("State files require Linux.");
        var root = Path.TrimEndingDirectorySeparator(path);
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("State root must not be a symbolic link.");
        if (create)
        {
            try { Directory.CreateDirectory(root, (UnixFileMode)448); }
            catch (UnauthorizedAccessException ex) { throw new IOException("Cannot create the state directory.", ex); }
        }
        var fd = Open(root, DirectoryFlag | NoFollow | CloseOnExec, 0);
        if (fd < 0) throw IoError("Cannot open the state directory", Marshal.GetLastPInvokeError());
        var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try { RequireMode(handle, 0x4000 | 448, directory: true); return handle; }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle? OpenPrivateFile(SafeFileHandle root, string name, bool create, bool allowMissing = false)
    {
        var flags = NoFollow | CloseOnExec | NonBlocking | (create ? 2 | 0x40 : 0); // O_RDWR | O_CREAT, or O_RDONLY
        var fd = OpenAt(root, name, flags, 384);
        if (fd < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (allowMissing && error == 2) return null;
            throw IoError("Cannot open the private state file", error);
        }
        var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try { RequireMode(handle, 0x8000 | 384, directory: false); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static void RequireMode(SafeFileHandle fd, int expectedMode, bool directory)
    {
        if (Statx(fd, "", 0x1000, 0x7ff, out var stat) != 0)
            throw IoError("Cannot inspect the state file", Marshal.GetLastPInvokeError());
        if (stat.Mode != expectedMode || stat.Uid != GetEuid() || (!directory && stat.LinkCount != 1))
            throw new IOException(directory
                ? "State root must be owned by the current user with permissions 0700."
                : "State file must be a singly linked regular file owned by the current user with permissions 0600.");
    }

    internal static IOException IoError(string message, int error) => new($"{message} (errno {error}).", new Win32Exception(error));

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(SafeFileHandle directory, string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle fd, string path, int flags, uint mask, out StatxBuffer stat);
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
