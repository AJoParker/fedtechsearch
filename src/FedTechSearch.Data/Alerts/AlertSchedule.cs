using FedTechSearch.Core.Model;

namespace FedTechSearch.Data.Alerts;

/// <summary>
/// Digests go out at a fixed local time: daily at 07:00, weekly on Monday at 07:00. An alert is due
/// when its last window ended before the most recent send time. A new alert starts its window at
/// confirmation, so the first digest only holds jobs found after signing up.
/// </summary>
public static class AlertSchedule
{
    public static readonly TimeSpan SendTime = TimeSpan.FromHours(7);

    public static bool IsDue(AlertFrequency frequency, DateTime? lastNotifiedUtc, DateTime nowUtc, TimeZoneInfo zone) =>
        lastNotifiedUtc is { } last && last < MostRecentSendTimeUtc(frequency, nowUtc, zone);

    /// <summary>The latest 07:00 local (on a Monday, for weekly) at or before now, in UTC.</summary>
    public static DateTime MostRecentSendTimeUtc(AlertFrequency frequency, DateTime nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var anchor = local.Date + SendTime;
        if (local < anchor) anchor = anchor.AddDays(-1);
        if (frequency == AlertFrequency.Weekly)
        {
            var sinceMonday = ((int)anchor.DayOfWeek + 6) % 7;
            anchor = anchor.AddDays(-sinceMonday);
        }
        // 07:00 is never inside a DST gap or overlap in US zones, so the conversion is unambiguous.
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(anchor, DateTimeKind.Unspecified), zone);
    }

    /// <summary>The configured zone, or Eastern; falls back to UTC-5 if the OS has no time zone data.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "America/New_York" : id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC-05", TimeSpan.FromHours(-5), "UTC-05", "UTC-05");
        }
    }
}
