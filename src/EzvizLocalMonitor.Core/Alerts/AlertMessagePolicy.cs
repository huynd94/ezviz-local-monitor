using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public static class AlertMessagePolicy
{
    public static string CaptionForAlert(DetectionEvent item)
    {
        var aiCaption = string.IsNullOrWhiteSpace(item.AiSummary) ? string.Empty : $"\nAI: {item.AiSummary}";
        var label = item.IsHumanDetection ? "PHÁT HIỆN NGƯỜI" : "PHÁT HIỆN CHUYỂN ĐỘNG";
        return $"{label} | {item.CameraName} | {item.DetectedAt:yyyy-MM-dd HH:mm:ss} | Tin cậy: {item.Confidence:P0} | Nguồn: {item.DetectionSource}{aiCaption}";
    }

    public static string CaptionForPhotoAfterText(string caption, string textResult)
    {
        if (string.IsNullOrWhiteSpace(caption)) return string.Empty;
        return textResult.Contains("đã gửi", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : caption;
    }
}
