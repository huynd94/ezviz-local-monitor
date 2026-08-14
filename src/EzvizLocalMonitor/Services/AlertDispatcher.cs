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
        if (settings.ZaloEnabled)
            ZaloDiagnostics.Info($"alert dispatch; tokenPresent={!string.IsNullOrWhiteSpace(settings.ZaloBotToken)}; chatId={ZaloDiagnostics.Mask(settings.ZaloChatId)}; chatIdLength={settings.ZaloChatId?.Trim().Length ?? 0}; imagePath={Path.GetFileName(item.ImagePath)}; imageExists={File.Exists(item.ImagePath)}; imageBytes={(File.Exists(item.ImagePath) ? new FileInfo(item.ImagePath).Length : 0)}");
        var tasks = new List<Task<string>>();
        if (settings.TelegramEnabled)
            tasks.Add(SafeSendAsync("Telegram", () => SendTelegramFastAlertAsync(settings.TelegramBotToken, settings.TelegramChatId, item.ImagePath, caption, cancellationToken)));
        if (settings.ZaloEnabled)
            tasks.Add(SafeSendAsync("Zalo", () => SendZaloFastAlertAsync(settings.ZaloBotToken ?? string.Empty, settings.ZaloChatId ?? string.Empty, item.ImagePath, caption, cancellationToken)));
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
        return FormatApiResult("Telegram", response, body);
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
        return FormatApiResult("Telegram", response, body);
    }

    private static async Task<string> SendZaloFastAlertAsync(string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        string textResult;
        try { textResult = await SendZaloTextAsync(token, chatId, caption, ct); }
        catch (Exception ex)
        {
            ZaloDiagnostics.Error($"sendMessage exception; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}", ex);
            textResult = "Zalo text: lỗi " + SafeException(ex);
        }

        string photoResult;
        try { photoResult = await SendZaloPhotoAsync(token, chatId, imagePath, caption, ct); }
        catch (Exception ex)
        {
            ZaloDiagnostics.Error($"sendPhoto exception; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}; imagePath={Path.GetFileName(imagePath)}", ex);
            photoResult = "Zalo ảnh: lỗi " + SafeException(ex);
        }
        return $"{textResult} + {photoResult}";
    }

    private static async Task<string> SendZaloTextAsync(string token, string chatId, string text, CancellationToken ct)
    {
        Require(token, "Zalo Bot Token");
        Require(chatId, "Zalo Chat ID");
        ZaloDiagnostics.Info($"sendMessage start; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}; chatIdLength={chatId.Trim().Length}; textLength={text.Length}");
        using var payload = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId.Trim(), text }), Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync($"https://bot-api.zaloplatforms.com/bot{token}/sendMessage", payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var result = FormatApiResult("Zalo", response, body);
        LogApiResult("sendMessage", chatId, null, response, body, result);
        return result;
    }

    private static async Task<string> SendZaloPhotoAsync(string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        Require(token, "Zalo Bot Token");
        Require(chatId, "Zalo Chat ID");
        var fileExists = File.Exists(imagePath);
        var imageBytes = fileExists ? new FileInfo(imagePath).Length : 0;
        var isHttpsUrl = Uri.TryCreate(imagePath, UriKind.Absolute, out var photoUri) && photoUri.Scheme == Uri.UriSchemeHttps;
        ZaloDiagnostics.Info($"sendPhoto start; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}; chatIdLength={chatId.Trim().Length}; imageFile={Path.GetFileName(imagePath)}; imageExists={fileExists}; imageBytes={imageBytes}; photoIsHttpsUrl={isHttpsUrl}");

        if (!isHttpsUrl)
        {
            const string reason = "Zalo Bot sendPhoto yêu cầu photo là URL HTTPS công khai; ảnh sự kiện hiện chỉ có đường dẫn local trong máy.";
            ZaloDiagnostics.Error($"sendPhoto skipped; reason={reason}; chatId={ZaloDiagnostics.Mask(chatId)}; imageExists={fileExists}; imageBytes={imageBytes}");
            return "Zalo ảnh: chưa gửi; API yêu cầu URL HTTPS công khai cho photo";
        }

        // Zalo Bot API documents `photo` as a String URL. Use form-urlencoded so
        // chat_id/photo/caption are parsed as the documented string fields rather
        // than as a file upload that the endpoint may ignore.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId.Trim(),
            ["photo"] = photoUri!.ToString(),
            ["caption"] = caption
        });
        using var response = await Client.PostAsync($"https://bot-api.zaloplatforms.com/bot{token}/sendPhoto", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var result = FormatApiResult("Zalo", response, body);
        LogApiResult("sendPhoto", chatId, imagePath, response, body, result);
        return result;
    }

    private static void LogApiResult(string operation, string chatId, string? imagePath, HttpResponseMessage response, string body, string result)
    {
        var fileExists = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);
        var imageBytes = fileExists ? new FileInfo(imagePath!).Length : 0;
        ZaloDiagnostics.Info($"{operation} result; status={(int)response.StatusCode}; chatId={ZaloDiagnostics.Mask(chatId)}; chatIdLength={chatId.Trim().Length}; imageExists={fileExists}; imageBytes={imageBytes}; contentType={response.Content.Headers.ContentType?.MediaType ?? "<none>"}; result={result}; response={ZaloDiagnostics.SanitizeResponse(body)}");
    }

    private static ByteArrayContent CreateImageContent(string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Không tìm thấy ảnh sự kiện để gửi.", imagePath);
        var bytes = File.ReadAllBytes(imagePath);
        if (bytes.Length == 0) throw new InvalidDataException("Ảnh sự kiện rỗng, không thể gửi Zalo.");

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentLength = bytes.Length;
        return content;
    }

    private static string FormatApiResult(string channel, HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode && IsOk(body)) return $"{channel}: đã gửi";

        var detail = ReadApiError(body);
        var status = $"HTTP {(int)response.StatusCode}";
        return string.IsNullOrWhiteSpace(detail)
            ? $"{channel}: lỗi {status}; response không xác nhận thành công"
            : $"{channel}: lỗi {status}; {detail}";
    }

    private static bool IsOk(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static string ReadApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "response rỗng";
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();
            foreach (var name in new[] { "error_code", "code" })
            {
                if (root.TryGetProperty(name, out var code) && code.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                    parts.Add($"{name}={code}");
            }
            foreach (var name in new[] { "description", "message", "error" })
            {
                if (root.TryGetProperty(name, out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    parts.Add(message.GetString()!);
                    break;
                }
            }
            return parts.Count == 0 ? "response JSON không có thông tin lỗi" : string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return "response không phải JSON hợp lệ";
        }
    }

    private static string SafeException(Exception ex) => ex.Message.Length > 100 ? ex.Message[..100] : ex.Message;

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Chưa nhập {name}.");
    }
}
