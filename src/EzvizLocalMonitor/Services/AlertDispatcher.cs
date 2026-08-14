using System.Net.Http;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class AlertDispatcher
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<string> SendAsync(AlertChannelSettings settings, DetectionEvent item, CancellationToken cancellationToken = default)
    {
        var aiCaption = string.IsNullOrWhiteSpace(item.AiSummary) ? string.Empty : $"\nAI: {item.AiSummary}";
        var label = item.IsHumanDetection ? "PHÁT HIỆN NGƯỜI" : "PHÁT HIỆN CHUYỂN ĐỘNG";
        var caption = $"{label} | {item.CameraName} | {item.DetectedAt:yyyy-MM-dd HH:mm:ss} | Tin cậy: {item.Confidence:P0} | Nguồn: {item.DetectionSource}{aiCaption}";
        var tasks = new List<Task<string>>();
        if (settings.TelegramEnabled)
            tasks.Add(SafeSendAsync("Telegram", () => SendTelegramFastAlertAsync(settings.TelegramBotToken, settings.TelegramChatId, item.ImagePath, caption, cancellationToken)));
        if (settings.ZaloEnabled)
            tasks.Add(SafeSendAsync("Zalo", () => SendZaloPhotoAsync(settings.ZaloBotToken, settings.ZaloChatId, item.ImagePath, caption, cancellationToken)));
        if (tasks.Count == 0) return "Không có kênh nào được bật";
        var results = await Task.WhenAll(tasks);
        return string.Join(" | ", results);
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

    private static async Task<string> SendTelegramFastAlertAsync(string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        // Tin chữ nhỏ đi trước để Telegram hiển thị cảnh báo gần như ngay lập tức; ảnh được upload ngay sau đó.
        string textResult;
        try { textResult = await SendTelegramTextAsync(token, chatId, caption, ct); }
        catch (Exception ex) { textResult = "Telegram text: lỗi " + SafeException(ex); }
        string photoResult;
        try { photoResult = await SendTelegramPhotoAsync(token, chatId, imagePath, caption, ct); }
        catch (Exception ex) { photoResult = "Telegram ảnh: lỗi " + SafeException(ex); }
        return $"{textResult} + {photoResult}";
    }

    private static async Task<string> SafeSendAsync(string channel, Func<Task<string>> operation)
    {
        try { return await operation(); }
        catch (Exception ex) { return $"{channel}: lỗi {SafeException(ex)}"; }
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

    private static string SafeException(Exception ex) => ex.Message.Length > 100 ? ex.Message[..100] : ex.Message;

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Chưa nhập {name}.");
    }
}
