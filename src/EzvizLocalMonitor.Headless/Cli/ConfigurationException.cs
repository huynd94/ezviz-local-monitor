namespace EzvizLocalMonitor.Headless.Cli;

public sealed class ConfigurationException(string message, Exception? inner = null) : Exception(message, inner);
