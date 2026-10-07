namespace EzvizLocalMonitor.Services;

public enum LogChannel { App, Camera, Alerts, Ai }
public sealed record LogEntry(DateTimeOffset Timestamp, LogChannel Channel, string Level, string Message);

public interface IAppLogger
{
    void Info(LogChannel channel, string message);
    void Error(LogChannel channel, string message, Exception? exception = null);
    string Redact(string? value) => new LogRedactor().Redact(value);
}
