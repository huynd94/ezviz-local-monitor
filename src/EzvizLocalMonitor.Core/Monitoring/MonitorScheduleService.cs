using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public static class MonitorScheduleService
{
    public static bool IsMonitoringAllowed(AppSettings settings, DateTimeOffset? now = null, TimeZoneInfo? zone = null)
    {
        if (settings.MonitorSchedules.Count == 0) return true;
        var local = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.Now, zone ?? TimeZoneInfo.Local).DateTime;
        return settings.MonitorSchedules.Any(schedule => schedule.IsEnabled && Matches(schedule, local));
    }

    public static int? ActivePerformanceProfile(AppSettings settings, DateTimeOffset? now = null, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.Now, zone ?? TimeZoneInfo.Local).DateTime;
        return settings.MonitorSchedules
            .Where(schedule => schedule.IsEnabled && Matches(schedule, local))
            .Select(schedule => (int?)Math.Clamp(schedule.PerformanceProfile, 0, 3))
            .FirstOrDefault();
    }

    private static bool Matches(MonitorSchedule schedule, DateTime local)
    {
        if (!TryParse(schedule.StartTime, out var start) || !TryParse(schedule.EndTime, out var end)) return false;
        var current = local.TimeOfDay;
        if (start <= end) return MatchesDay(schedule.Days, local.DayOfWeek) && current >= start && current <= end;
        return current >= start ? MatchesDay(schedule.Days, local.DayOfWeek)
            : current <= end && MatchesDay(schedule.Days, local.AddDays(-1).DayOfWeek);
    }

    private static bool MatchesDay(string days, DayOfWeek day)
    {
        var normalized = days.Replace(" ", string.Empty).ToLowerInvariant();
        if (normalized is "all" or "mon-sun" or "everyday") return true;
        if (normalized.Contains("weekday") && day is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) return true;
        if (normalized.Contains("weekend") && day is DayOfWeek.Saturday or DayOfWeek.Sunday) return true;
        var token = day switch
        {
            DayOfWeek.Monday => "mon",
            DayOfWeek.Tuesday => "tue",
            DayOfWeek.Wednesday => "wed",
            DayOfWeek.Thursday => "thu",
            DayOfWeek.Friday => "fri",
            DayOfWeek.Saturday => "sat",
            _ => "sun"
        };
        return normalized.Contains(token, StringComparison.Ordinal);
    }

    private static bool TryParse(string text, out TimeSpan time)
        => TimeSpan.TryParseExact(text, new[] { @"hh\:mm", @"h\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out time);
}
