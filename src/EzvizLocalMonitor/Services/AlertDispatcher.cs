using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class AlertDispatcher
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    // Upload ảnh multipart có thể chậm hơn sendMessage, đặc biệt khi mạng LAN/WAN
    // đang bận. Tách client để không làm tăng timeout của các tin nhắn văn bản.
    private static readonly HttpClient TelegramPhotoClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly TimeSpan ZaloOperationTimeout = TimeSpan.FromSeconds(12);
    private readonly ImageRelayService _imageRelay = new();
    private readonly ConcurrentDictionary<string, byte> _successfulOperations = new();

    public async Task<string> SendAsync(AlertChannelSettings settings, DetectionEvent item, CancellationToken cancellationToken = default)
    {
        var aiCaption = string.IsNullOrWhiteSpace(item.AiSummary) ? string.Empty : $"\nAI: {item.AiSummary}";
        var label = item.IsHumanDetection ? "PHÁT HIỆN NGƯỜI" : "PHÁT HIỆN CHUYỂN ĐỘNG";
        var caption = $"{label} | {item.CameraName} | {item.DetectedAt:yyyy-MM-dd HH:mm:ss} | Tin cậy: {item.Confidence:P0} | Nguồn: {item.DetectionSource}{aiCaption}";
        if (settings.ZaloEnabled)
        {
            var imageMetadata = ReadImageMetadata(item.ImagePath);
            ZaloDiagnostics.Info($"alert dispatch; tokenPresent={!string.IsNullOrWhiteSpace(settings.ZaloBotToken)}; chatId={ZaloDiagnostics.Mask(settings.ZaloChatId)}; chatIdLength={settings.ZaloChatId?.Trim().Length ?? 0}; imagePath={SafeFileName(item.ImagePath)}; imageExists={imageMetadata.Exists}; imageBytes={imageMetadata.Bytes}");
        }
        var tasks = new List<Task<string>>();
        if (settings.TelegramEnabled)
            tasks.Add(SafeSendAsync("Telegram", () => SendTelegramFastAlertAsync(item.Id, settings.TelegramBotToken, settings.TelegramChatId, item.ImagePath, caption, cancellationToken)));
        if (settings.ZaloEnabled)
            tasks.Add(SafeSendAsync("Zalo", () => SendZaloFastAlertAsync(item.Id, settings, settings.ZaloBotToken ?? string.Empty, settings.ZaloChatId ?? string.Empty, item.ImagePath, caption, cancellationToken)));
        if (tasks.Count == 0) return "Không có kênh nào được bật";
        try
        {
            var results = await Task.WhenAll(tasks);
            return string.Join(" | ", results);
        }
        catch (OperationCanceledException)
        {
            if (settings.ZaloEnabled) ZaloDiagnostics.Error("alert dispatch cancelled safely");
            return "Cảnh báo: thao tác đã hủy hoặc quá thời gian";
        }
        catch (Exception ex)
        {
            if (settings.ZaloEnabled) ZaloDiagnostics.Error("alert dispatch aggregate exception isolated", ex);
            return "Cảnh báo: lỗi đã được cô lập; xem log Zalo nếu kênh Zalo đang bật";
        }
    }

    public async Task<string> TestAsync(AlertChannelSettings settings, string channel, CancellationToken cancellationToken = default)
    {
        const string message = "EZVIZ Local Monitor: kiểm tra kênh cảnh báo thành công.";
        return channel switch
        {
            "telegram" => await SendTelegramTextAsync(settings.TelegramBotToken, settings.TelegramChatId, message, cancellationToken),
            "zalo" => await SafeTestZaloAsync(settings.ZaloBotToken, settings.ZaloChatId, message, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    private async Task<string> SendTelegramFastAlertAsync(long eventId, string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        // Tin chữ nhỏ đi trước để Telegram hiển thị cảnh báo gần như ngay lập tức; ảnh được upload ngay sau đó.
        string textResult;
        try { textResult = await SendIdempotentAsync(eventId, "Telegram text", () => SendTelegramTextAsync(token, chatId, caption, ct)); }
        catch (Exception ex) { textResult = "Telegram text: lỗi " + SafeException(ex); }
        string photoResult;
        try
        {
            var photoCaption = AlertMessagePolicy.CaptionForPhotoAfterText(caption, textResult);
            photoResult = await SendIdempotentAsync(eventId, "Telegram photo", () => SendTelegramPhotoAsync(token, chatId, imagePath, photoCaption, ct));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            var metadata = ReadImageMetadata(imagePath);
            AppLogger.Error(LogChannel.Alerts, $"telegram photo timeout; image={SafeFileName(imagePath)}; bytes={metadata.Bytes}; timeoutSeconds=45", ex);
            photoResult = "Telegram ảnh: lỗi timeout upload (45 giây)";
        }
        catch (OperationCanceledException)
        {
            photoResult = "Telegram ảnh: đã hủy";
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.Alerts, $"telegram photo exception; image={SafeFileName(imagePath)}", ex);
            photoResult = "Telegram ảnh: lỗi " + SafeException(ex);
        }
        return $"{textResult} + {photoResult}";
    }

    private static async Task<string> SafeSendAsync(string channel, Func<Task<string>> operation)
    {
        try { return await operation(); }
        catch (OperationCanceledException)
        {
            if (channel == "Zalo") ZaloDiagnostics.Error("alert operation cancelled safely");
            return $"{channel}: thao tác đã hủy hoặc quá thời gian";
        }
        catch (Exception ex)
        {
            if (channel == "Zalo") ZaloDiagnostics.Error("alert operation isolated safely", ex);
            return $"{channel}: lỗi {SafeException(ex)}";
        }
    }

    private static async Task<string> SafeTestZaloAsync(string token, string chatId, string message, CancellationToken ct)
    {
        try
        {
            using var timeout = CreateZaloTimeout(ct);
            return await SendZaloTextAsync(token ?? string.Empty, chatId ?? string.Empty, message, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            ZaloDiagnostics.Error("test sendMessage cancelled safely");
            return "Zalo: thao tác kiểm tra đã hủy hoặc quá thời gian";
        }
        catch (Exception ex)
        {
            ZaloDiagnostics.Error($"test sendMessage exception; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}", ex);
            return "Zalo: lỗi " + SafeException(ex);
        }
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
        var metadata = ReadImageMetadata(imagePath);
        AppLogger.Info(LogChannel.Alerts, $"telegram photo upload start; image={SafeFileName(imagePath)}; bytes={metadata.Bytes}; timeoutSeconds=45");
        using var response = await TelegramPhotoClient.PostAsync($"https://api.telegram.org/bot{token}/sendPhoto", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return FormatApiResult("Telegram", response, body);
    }

    private async Task<string> SendZaloFastAlertAsync(long eventId, AlertChannelSettings settings, string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        token ??= string.Empty;
        chatId ??= string.Empty;
        string textResult;
        try
        {
            using var textTimeout = CreateZaloTimeout(ct);
            textResult = await SendIdempotentAsync(eventId, "Zalo text", () => SendZaloTextAsync(token, chatId, caption, textTimeout.Token));
        }
        catch (OperationCanceledException)
        {
            ZaloDiagnostics.Error($"sendMessage cancelled/timeout; chatId={ZaloDiagnostics.Mask(chatId)}");
            textResult = "Zalo text: đã hủy hoặc quá thời gian";
        }
        catch (Exception ex)
        {
            ZaloDiagnostics.Error($"sendMessage exception isolated; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}", ex);
            textResult = "Zalo text: lỗi " + SafeException(ex);
        }

        string photoResult;
        try
        {
            using var photoTimeout = CreateZaloTimeout(ct);
            var photoCaption = AlertMessagePolicy.CaptionForPhotoAfterText(caption, textResult);
            photoResult = await SendIdempotentAsync(eventId, "Zalo photo", () => SendZaloPhotoAsync(settings, token, chatId, imagePath, photoCaption, photoTimeout.Token));
        }
        catch (OperationCanceledException)
        {
            ZaloDiagnostics.Error($"sendPhoto cancelled/timeout; chatId={ZaloDiagnostics.Mask(chatId)}; imageFile={SafeFileName(imagePath)}");
            photoResult = "Zalo ảnh: đã hủy hoặc quá thời gian";
        }
        catch (Exception ex)
        {
            ZaloDiagnostics.Error($"sendPhoto exception isolated; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}; imagePath={SafeFileName(imagePath)}", ex);
            photoResult = "Zalo ảnh: lỗi " + SafeException(ex);
        }
        return $"{textResult} + {photoResult}";
    }

    private async Task<string> SendIdempotentAsync(long eventId, string operation, Func<Task<string>> sender)
    {
        var key = $"{eventId}:{operation}";
        if (_successfulOperations.ContainsKey(key)) return $"{operation}: đã gửi (idempotent)";

        var result = await sender();
        if (result.Contains("đã gửi", StringComparison.OrdinalIgnoreCase))
            _successfulOperations.TryAdd(key, 0);
        return result;
    }

    private static CancellationTokenSource CreateZaloTimeout(CancellationToken parent)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        source.CancelAfter(ZaloOperationTimeout);
        return source;
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

    private async Task<string> SendZaloPhotoAsync(AlertChannelSettings settings, string token, string chatId, string imagePath, string caption, CancellationToken ct)
    {
        Require(token, "Zalo Bot Token");
        Require(chatId, "Zalo Chat ID");
        var imageMetadata = ReadImageMetadata(imagePath);
        var isHttpsUrl = Uri.TryCreate(imagePath, UriKind.Absolute, out var photoUri) && photoUri.Scheme == Uri.UriSchemeHttps;
        if (!isHttpsUrl && settings.ZaloImageRelayEnabled && settings.AllowImageRelayOutsideLan)
        {
            try
            {
                imagePath = await _imageRelay.UploadAsync(imagePath, settings.ZaloImageRelayUrl, settings.ZaloImageRelayApiKey, ct);
                isHttpsUrl = Uri.TryCreate(imagePath, UriKind.Absolute, out photoUri) && photoUri.Scheme == Uri.UriSchemeHttps;
                imageMetadata = (false, 0);
                ZaloDiagnostics.Info($"sendPhoto relay completed; chatId={ZaloDiagnostics.Mask(chatId)}; relayUrlHost={photoUri?.Host ?? "<invalid>"}");
            }
            catch (Exception ex)
            {
                ZaloDiagnostics.Error($"sendPhoto relay failed; chatId={ZaloDiagnostics.Mask(chatId)}; imageFile={SafeFileName(imagePath)}", ex);
                return "Zalo ảnh: relay HTTPS thất bại";
            }
        }
        ZaloDiagnostics.Info($"sendPhoto start; tokenPresent={!string.IsNullOrWhiteSpace(token)}; chatId={ZaloDiagnostics.Mask(chatId)}; chatIdLength={chatId.Trim().Length}; imageFile={SafeFileName(imagePath)}; imageExists={imageMetadata.Exists}; imageBytes={imageMetadata.Bytes}; photoIsHttpsUrl={isHttpsUrl}");

        if (!isHttpsUrl)
        {
            const string reason = "Zalo Bot sendPhoto yêu cầu photo là URL HTTPS công khai; relay chưa bật hoặc chưa có đồng thuận.";
            ZaloDiagnostics.Error($"sendPhoto skipped; reason={reason}; chatId={ZaloDiagnostics.Mask(chatId)}; imageExists={imageMetadata.Exists}; imageBytes={imageMetadata.Bytes}");
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
        var imageMetadata = ReadImageMetadata(imagePath);
        ZaloDiagnostics.Info($"{operation} result; status={(int)response.StatusCode}; chatId={ZaloDiagnostics.Mask(chatId)}; chatIdLength={chatId?.Trim().Length ?? 0}; imageExists={imageMetadata.Exists}; imageBytes={imageMetadata.Bytes}; contentType={response.Content.Headers.ContentType?.MediaType ?? "<none>"}; result={ZaloDiagnostics.SanitizeResponse(result)}; response={ZaloDiagnostics.SanitizeResponse(body)}");
    }

    private static (bool Exists, long Bytes) ReadImageMetadata(string? imagePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return (false, 0);
            return (true, new FileInfo(imagePath).Length);
        }
        catch
        {
            return (false, 0);
        }
    }

    private static string SafeFileName(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? "<empty>" : Path.GetFileName(path); }
        catch { return "<invalid>"; }
    }

    private static HttpContent CreateImageContent(string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Không tìm thấy ảnh sự kiện để gửi.", imagePath);
        var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            stream.Dispose();
            throw new InvalidDataException("Ảnh sự kiện rỗng, không thể gửi Telegram.");
        }

        var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentLength = stream.Length;
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

    private static string SafeException(Exception ex)
    {
        var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        return message.Length > 100 ? message[..100] : message;
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Chưa nhập {name}.");
    }
}
