namespace GlucoseAPI.Domain.Services;

/// <summary>
/// Domain service for timezone conversions.
/// Encapsulates timezone resolution and UTC ↔ local time conversion.
/// </summary>
public class TimeZoneConverter
{
    private readonly ILogger<TimeZoneConverter> _logger;

    public TimeZoneConverter(ILogger<TimeZoneConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolve a timezone ID string to a TimeZoneInfo object.
    /// Falls back to UTC if the timezone cannot be found.
    /// </summary>
    public TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            _logger.LogWarning("Could not resolve timezone '{TzId}'. Falling back to UTC.", timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Convert a UTC DateTime to local time in the given timezone.
    /// </summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz)
    {
        var utcDt = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDt, tz);
    }

    /// <summary>
    /// Convert a local DateTime to UTC in the given timezone.
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
    {
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>
    /// Get the local midnight boundaries for a given date in UTC.
    /// </summary>
    public static (DateTime startUtc, DateTime endUtc) GetDayBoundariesUtc(DateTime localDate, TimeZoneInfo tz)
    {
        var localStart = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, tz), TimeZoneInfo.ConvertTimeToUtc(localEnd, tz));
    }
}
