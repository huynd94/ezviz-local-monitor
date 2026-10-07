using System.Text;

namespace EzvizLocalMonitor.Headless.Cli;

public sealed class SecretInput(TextReader input, TextWriter output, bool interactive)
{
    public async Task<string> ReadAsync(bool confirm, CancellationToken ct)
    {
        var password = await ReadOneAsync(ct);
        if (confirm)
        {
            if (interactive) await output.WriteAsync("Confirm secret: ");
            var confirmation = await ReadOneAsync(ct);
            if (!string.Equals(password, confirmation, StringComparison.Ordinal)) throw new ConfigurationException("Secret confirmation does not match.");
        }
        return password;
    }

    private async Task<string> ReadOneAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!interactive)
            return await input.ReadLineAsync(ct) ?? throw new ConfigurationException("Secret input ended before a complete value was provided.");
        if (Console.IsInputRedirected) throw new ConfigurationException("A terminal is required; use the explicit stdin option for automation.");
        var value = new StringBuilder();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!Console.KeyAvailable) { await Task.Delay(25, ct); continue; }
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { await output.WriteLineAsync(); return value.ToString(); }
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control)) throw new ConfigurationException("Secret input ended.");
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
    }
}
