using System.Net.Http;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class AlertDispatcher
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> SendAsync(AlertChannelSettings settings, DetectionEvent item, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        var aiCaption = string.IsNullOrWhiteSpace(item.AiSummary) ? string.Empty : $"\nAI: {item.AiSummary}";
        var caption = $"PHÁT HIỆN NGƯỜI | {item.CameraName} | {item.DetectedAt:yyyy-MM-dd HH:mm:ss} | Tin cậy: {item.Confidence:P0}{aiCaption}";

        if (settings.TelegramEnabled)
            results.Add(await SendTelegramPhotoAsync(settings.TelegramBotToken, settings.TelegramChatId, item.ImagePath, caption, cancellationToken));
        if (settings.ZaloEnabled)
            results.Add(await SendZaloPhotoAsync(settings.ZaloBotToken, settings.ZaloChatId, item.ImagePath, caption, cancellationToken));

        return results.Count == 0 ? "Không có kênh nào được bật" : string.Join(" | ", results);
    }

    public async Task<string> TestAsync(AlertChannelSettings settings, string channel, CancellationToken cancellationToken = default)
    {
        const string message = "EZVIZ Local Monitor: kiểm tra kênh cảnh báo thành công.";
        return channel switch
        {
            "telegram" => await SendTelegramTextAsync(settings.TelegramBotToken, settings.TelegramChatId, message, cancellationToken),
            "zalo" => await SendZaloTextAsync(settings.ZaloBotToken, settings.ZaloChatId, message, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    private static async Task<string> SendTelegramTextAsync(string token, string chatId, string text, CancellationToken ct)
    {
        Require(token, "Telegram Bot Token");
        Require(chatId, "Telegram Chat ID");
        using var payload = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId, text }), Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode && IsOk(body) ? "Telegram: đã gửi" : $"Telegram: lỗi HTTP {(int)response.StatusCode}";
    }

    private static async Task<string> SendTelegramPhotoAsync(string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        Require(token, "Telegram Bot Token");
        Require(chatId, "Telegram Chat ID");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId), "chat_id");
        form.Add(new StringContent(caption), "caption");
        form.Add(CreateImageContent(imagePath), "photo", Path.GetFileName(imagePath));
        using var response = await Client.PostAsync($"https://api.telegram.org/bot{token}/sendPhoto", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode && IsOk(body) ? "Telegram: đã gửi" : $"Telegram: lỗi HTTP {(int)response.StatusCode}";
    }

    private static async Task<string> SendZaloTextAsync(string token, string chatId, string text, CancellationToken ct)
    {
        Require(token, "Zalo Bot Token");
        Require(chatId, "Zalo Chat ID");
        using var payload = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId, text }), Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync($"https://bot-api.zaloplatforms.com/bot{token}/sendMessage", payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode && IsOk(body) ? "Zalo: đã gửi" : $"Zalo: lỗi HTTP {(int)response.StatusCode}";
    }

    private static async Task<string> SendZaloPhotoAsync(string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        Require(token, "Zalo Bot Token");
        Require(chatId, "Zalo Chat ID");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId), "chat_id");
        form.Add(new StringContent(caption), "caption");
        form.Add(CreateImageContent(imagePath), "photo", Path.GetFileName(imagePath));
        using var response = await Client.PostAsync($"https://bot-api.zaloplatforms.com/bot{token}/sendPhoto", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode && IsOk(body) ? "Zalo: đã gửi" : $"Zalo: lỗi HTTP {(int)response.StatusCode}";
    }

    private static StreamContent CreateImageContent(string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Không tìm thấy ảnh sự kiện để gửi.", imagePath);
        var stream = File.OpenRead(imagePath);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    private static bool IsOk(string body)
    {
        try { return JsonDocument.Parse(body).RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean(); }
        catch (JsonException) { return false; }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Chưa nhập {name}.");
    }
}
