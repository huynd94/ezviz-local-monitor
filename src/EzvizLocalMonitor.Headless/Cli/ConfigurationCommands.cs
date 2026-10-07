using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Cli;

public static class ConfigurationCommands
{
    public static async Task ConfigureAsync(AppPaths paths, bool fromStdin, TextReader input, TextWriter output, bool interactive, CancellationToken ct)
    {
        if (!fromStdin && !interactive) throw new ConfigurationException("Configure requires a terminal or --stdin.");
        AppSettings settings;
        if (fromStdin)
        {
            var json = await ReadJsonAsync(input, ct);
            try { settings = SettingsValidator.Decode(json); }
            finally { CryptographicOperations.ZeroMemory(json); }
        }
        else settings = new AppSettings();
        using var stateLock = StateDirectoryLock.Acquire(paths);
        // Open old state before editing and refuse corrupt ciphertext or missing keys.
        if (!fromStdin && File.Exists(paths.SettingsFile))
        {
            using var existing = ProtectedConfiguration.OpenExisting(paths);
            settings = existing.Settings;
        }
        if (!fromStdin) await EditAsync(settings, input, output, ct);
        SettingsValidator.Validate(settings);
        ct.ThrowIfCancellationRequested();
        using var configuration = ProtectedConfiguration.OpenForNewOrExistingWrite(paths);
        configuration.Save(settings);
        await output.WriteLineAsync("Configuration saved.");
    }

    public static AppSettings Validate(AppPaths paths)
    {
        using var configuration = ProtectedConfiguration.OpenExisting(paths);
        return configuration.Settings;
    }

    private static async Task<byte[]> ReadJsonAsync(TextReader input, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) break;
            text.Append(buffer, 0, count);
            // Bound allocation before computing exact UTF-8 size (including split surrogate pairs).
            if (text.Length > SettingsValidator.MaximumJsonBytes) throw new ConfigurationException("Configuration JSON exceeds 1 MiB.");
        }
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        if (bytes.Length > SettingsValidator.MaximumJsonBytes) throw new ConfigurationException("Configuration JSON exceeds 1 MiB.");
        return bytes;
    }

    private static async Task EditAsync(AppSettings settings, TextReader input, TextWriter output, CancellationToken ct)
    {
        await output.WriteLineAsync("Press Enter to keep each field. Existing text and secrets are never displayed. Use '-' to clear text. Counts remove trailing entries or add new ones.");
        var cameras = await CountAsync("Camera count (0-4)", settings.Cameras.Count, 4, input, output, ct);
        while (settings.Cameras.Count > cameras) settings.Cameras.RemoveAt(settings.Cameras.Count - 1);
        while (settings.Cameras.Count < cameras) settings.Cameras.Add(new CameraDefinition());
        for (var index = 0; index < settings.Cameras.Count; index++) await EditFieldsAsync(settings.Cameras[index], $"Camera {index + 1}", input, output, ct);
        await EditFieldsAsync(settings.Alerts, "Alerts", input, output, ct);
        await EditFieldsAsync(settings.Ai, "AI", input, output, ct);
        var schedules = await CountAsync("Schedule count (0-100)", settings.MonitorSchedules.Count, 100, input, output, ct);
        while (settings.MonitorSchedules.Count > schedules) settings.MonitorSchedules.RemoveAt(settings.MonitorSchedules.Count - 1);
        while (settings.MonitorSchedules.Count < schedules) settings.MonitorSchedules.Add(new MonitorSchedule());
        for (var index = 0; index < settings.MonitorSchedules.Count; index++) await EditFieldsAsync(settings.MonitorSchedules[index], $"Schedule {index + 1}", input, output, ct);
        await EditFieldsAsync(settings, "Settings", input, output, ct);
    }

    private static async Task<int> CountAsync(string field, int previous, int maximum, TextReader input, TextWriter output, CancellationToken ct)
    {
        await output.WriteAsync($"{field} [{previous}]: ");
        var value = await input.ReadLineAsync(ct) ?? throw new ConfigurationException("Configuration input ended.");
        if (value.Length == 0) return previous;
        if (!int.TryParse(value, out var count) || count < 0 || count > maximum) throw new ConfigurationException("Invalid entry count.");
        return count;
    }

    private static async Task EditFieldsAsync(object section, string label, TextReader input, TextWriter output, CancellationToken ct)
    {
        foreach (var property in section.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var type = property.PropertyType;
            if (type != typeof(string) && type != typeof(bool) && type != typeof(int) && type != typeof(double)) continue;
            var hidden = type == typeof(string) && (property.Name.Contains("Token") || property.Name.Contains("Key") || property.Name.Contains("Url") || property.Name.Contains("ChatId"));
            await output.WriteAsync($"{label}.{property.Name}" + (type == typeof(string) ? " [keep]" : $" [{property.GetValue(section)}]") + (hidden ? " (hidden): " : ": "));
            var value = hidden ? await new SecretInput(input, output, true).ReadAsync(false, ct)
                : await input.ReadLineAsync(ct) ?? throw new ConfigurationException("Configuration input ended.");
            if (value.Length == 0) continue;
            try
            {
                object parsed = type == typeof(string) ? (value == "-" ? "" : value)
                    : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                property.SetValue(section, parsed);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException) { throw new ConfigurationException("Invalid value for " + label + "." + property.Name + "."); }
        }
    }
}
