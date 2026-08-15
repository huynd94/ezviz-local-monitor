namespace EzvizLocalMonitor.Services;

public static class AlertMessagePolicy
{
    public static string CaptionForPhotoAfterText(string caption, string textResult)
    {
        if (string.IsNullOrWhiteSpace(caption)) return string.Empty;
        return textResult.Contains("đã gửi", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : caption;
    }
}

