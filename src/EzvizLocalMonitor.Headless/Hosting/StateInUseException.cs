namespace EzvizLocalMonitor.Headless.Hosting;

public sealed class StateInUseException(string message, Exception? inner = null) : IOException(message, inner);
