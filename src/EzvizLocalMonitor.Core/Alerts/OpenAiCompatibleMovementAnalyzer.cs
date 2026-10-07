using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class OpenAiCompatibleMovementAnalyzer
{
    private static readonly HttpClient Client = new();

    public async Task<AiMovementAnalysis> AnalyzeAsync(AiSettings settings, string eventImagePath, string? previousImagePath,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled) return new AiMovementAnalysis(false, false, 0, string.Empty, "AI tắt");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)) throw new InvalidOperationException("Chưa nhập AI Base URL.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Chưa nhập AI Model.");
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("Chưa nhập AI API key.");
        if (!File.Exists(eventImagePath)) throw new FileNotFoundException("Không tìm thấy ảnh sự kiện để gửi AI.", eventImagePath);

        var content = new List<object>
        {
            new { type = "text", text = BuildPrompt(previousImagePath is not null && File.Exists(previousImagePath)) }
        };
        if (previousImagePath is not null && File.Exists(previousImagePath))
            content.Add(new { type = "image_url", image_url = new { url = ToDataUrl(previousImagePath), detail = "low" } });
        content.Add(new { type = "image_url", image_url = new { url = ToDataUrl(eventImagePath), detail = "low" } });

        var requestBody = new
        {
            model = settings.Model,
            temperature = 0,
            max_tokens = 180,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "You are a security camera event classifier. Do not identify people, faces, license plates, or demographics. Return only valid JSON." },
                new { role = "user", content }
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 90)));
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(settings.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI endpoint trả HTTP {(int)response.StatusCode}.");

        return ParseResponse(responseText);
    }

    private static string BuildPrompt(bool hasPreviousImage) => hasPreviousImage
        ? "Compare the first image (before) and the second image (event). Return JSON only with: motion_detected (boolean), person_present (boolean), confidence (number 0-1), summary (Vietnamese short description <= 180 characters). Motion means a meaningful scene/object/person change. Do not identify anyone."
        : "Inspect this security camera event image. Return JSON only with: motion_detected (boolean), person_present (boolean), confidence (number 0-1), summary (Vietnamese short description <= 180 characters). A previous frame is unavailable, so be conservative about motion. Do not identify anyone.";

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        return normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + "/chat/completions";
    }

    private static string ToDataUrl(string imagePath)
    {
        var bytes = File.ReadAllBytes(imagePath);
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }

    private static AiMovementAnalysis ParseResponse(string responseText)
    {
        using var outer = JsonDocument.Parse(responseText);
        var content = outer.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("AI endpoint không trả nội dung phân tích.");

        content = content.Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = content.IndexOf('\n');
            content = firstLineEnd >= 0 ? content[(firstLineEnd + 1)..] : content;
            content = content.Trim().TrimEnd('`').Trim();
        }

        using var analysis = JsonDocument.Parse(content);
        var root = analysis.RootElement;
        var motion = GetBoolean(root, "motion_detected");
        var person = GetBoolean(root, "person_present");
        var confidence = GetNumber(root, "confidence");
        if (confidence > 1) confidence /= 100;
        confidence = Math.Clamp(confidence, 0, 1);
        var summary = GetString(root, "summary");
        if (summary.Length > 180) summary = summary[..180];
        return new AiMovementAnalysis(motion, person, confidence, summary, "AI hoàn tất");
    }

    private static bool GetBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var result) && result,
            _ => false
        };
    }

    private static double GetNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed) ? parsed : 0;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
