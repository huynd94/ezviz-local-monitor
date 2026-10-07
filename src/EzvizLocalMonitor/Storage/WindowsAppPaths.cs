namespace EzvizLocalMonitor.Services;

public static class WindowsAppPaths
{
    public static AppPaths Create(string executableRoot) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZVIZ Local Monitor"),
        Path.Combine(executableRoot, "Models", "yolov8n.onnx"));
}
