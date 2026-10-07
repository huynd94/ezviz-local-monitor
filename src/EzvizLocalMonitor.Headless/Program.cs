using EzvizLocalMonitor.Headless.Cli;

namespace EzvizLocalMonitor.Headless;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await CommandDispatcher.ExecuteAsync(args, Console.In, Console.Out, Console.Error, cancel.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }
}
