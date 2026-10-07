using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EzvizLocalMonitor.Headless.Hosting;

public sealed record ProcessIdentity(int Pid, DateTimeOffset StartedAtUtc)
{
    public static ProcessIdentity Capture()
        => Capture(Environment.ProcessId);

    public static ProcessIdentity Capture(int pid)
    {
        var start = LinuxStart(pid);
        if (!start.Alive) throw new InvalidOperationException("Process is no longer alive.");
        return new(pid, start.StartedAtUtc);
    }

    public static bool IsAlive(ProcessIdentity identity)
    {
        if (identity is null || identity.Pid <= 0 || identity.StartedAtUtc == default || identity.StartedAtUtc.Offset != TimeSpan.Zero)
            return false;
        try
        {
            var process = LinuxStart(identity.Pid);
            return process.Alive && process.StartedAtUtc == identity.StartedAtUtc;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (FormatException) { return false; }
    }

    private static (DateTimeOffset StartedAtUtc, bool Alive) LinuxStart(int pid)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Process identity requires Linux /proc.");
        // .NET estimates boot time from per-process clocks; its StartTime can differ
        // by milliseconds between readers. Kernel btime + field22 is deterministic.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var endOfCommand = stat.LastIndexOf(')');
        if (endOfCommand < 0) throw new FormatException("Malformed process stat.");
        var fields = stat[(endOfCommand + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20) throw new FormatException("Malformed process stat.");
        var alive = fields[0] is not ("Z" or "X" or "x");
        var clockTicks = ulong.Parse(fields[19], CultureInfo.InvariantCulture);
        var rate = Sysconf(2); // Linux _SC_CLK_TCK
        if (rate <= 0) throw new InvalidOperationException("Cannot determine kernel clock rate.");
        var boot = File.ReadLines("/proc/stat").First(line => line.StartsWith("btime ", StringComparison.Ordinal));
        var seconds = long.Parse(boot.AsSpan(6), CultureInfo.InvariantCulture);
        var ticks = checked((long)(clockTicks * (ulong)TimeSpan.TicksPerSecond / (ulong)rate));
        return (DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(ticks), alive);
    }

    [DllImport("libc", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);
}
