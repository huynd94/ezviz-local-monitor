using System.Net.Http.Headers;
using System.Text.Json;

namespace EzvizLocalMonitor.Services;

public sealed class ImageRelayService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> UploadAsync(string imagePath, string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Relay URL phải là HTTPS hợp lệ.");
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Không tìm thấy ảnh sự kiện để relay.", imagePath);

        await using var stream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent();
        using var image = new StreamContent(stream);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "file", Path.GetFileName(imagePath));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Relay HTTP {(int)response.StatusCode}.");

        var url = ParseUrl(body);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var photoUri) || photoUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Relay không trả về URL HTTPS trong trường url/photo_url.");
        AppLogger.Info(LogChannel.Alerts, $"image relay success; endpointHost={endpointUri.Host}; responseUrlHost={photoUri.Host}");
        return photoUri.ToString();
    }

    private static string? ParseUrl(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            foreach (var name in new[] { "url", "photo_url", "image_url" })
                if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                    return property.GetString();
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "url", "photo_url", "image_url" })
                    if (result.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                        return property.GetString();
        }
        catch (JsonException) { }
        return body.Trim().Trim('"');
    }
}
