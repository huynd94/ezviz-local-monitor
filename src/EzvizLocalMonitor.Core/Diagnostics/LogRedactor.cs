using System.Text.RegularExpressions;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class LogRedactor
{
    private string[] _secrets = [];
    public void Configure(AppSettings settings)
    {
        var values = new List<string?> { settings.Alerts.TelegramBotToken, settings.Alerts.TelegramChatId, settings.Alerts.ZaloBotToken,
            settings.Alerts.ZaloChatId, settings.Alerts.ZaloImageRelayApiKey, settings.Ai.ApiKey };
        foreach (var camera in settings.Cameras)
            if (Uri.TryCreate(camera.RtspUrl, UriKind.Absolute, out var uri))
                values.AddRange(uri.UserInfo.Split(':').Select(Uri.UnescapeDataString));
        Volatile.Write(ref _secrets, values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct().OrderByDescending(value => value.Length).ToArray());
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        var text = value.Replace("\r", " ").Replace("\n", " ");
        text = Regex.Replace(text, @"(?i)(rtsps?://)[^/\s@]+@", "$1<redacted>@");
        text = Regex.Replace(text, @"(?i)(https?://[^/\s]+/bot)[^/\s?]+", "$1<redacted>");
        text = Regex.Replace(text, @"(?i)(authorization\s*[:=]\s*)(?:Bearer\s+)?[^;]+", "$1<redacted>");
        text = Regex.Replace(text, @"(?i)(token|api[_-]?key|password|mã xác thực|verification code)\s*[:=]\s*[^; ,]+", "$1=<redacted>");
        foreach (var secret in Volatile.Read(ref _secrets)) text = text.Replace(secret, "<redacted>", StringComparison.Ordinal);
        text = Regex.Replace(text, @"(?<![\w.])(?:10\.(?:\d{1,3}\.){2}\d{1,3}|192\.168\.(?:\d{1,3}\.)?\d{1,3}|172\.(?:1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3})(?::\d+)?", "<lan-ip>");
        return text.Length > 1000 ? text[..1000] + "…" : text;
    }
}
