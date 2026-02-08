using GlucoseAPI.Models;

namespace GlucoseAPI.Domain.Services;

/// <summary>
/// Pure domain service for computing glucose statistics.
/// No I/O, no dependencies — only business logic.
/// </summary>
public static class GlucoseStatsCalculator
{
    /// <summary>
    /// Compute glucose statistics for a list of readings relative to an event timestamp.
    /// Returns glucose at event, min, max, avg, spike, and peak time.
    /// </summary>
    public static GlucoseStats ComputeEventStats(IReadOnlyList<GlucoseReading> readings, DateTime eventTimestamp)
    {
        if (readings.Count == 0)
            return GlucoseStats.Empty;

        var eventTs = DateTime.SpecifyKind(eventTimestamp, DateTimeKind.Utc);
        var closest = readings.MinBy(r => Math.Abs((r.Timestamp - eventTs).TotalMinutes))!;
        var glucoseAtEvent = closest.Value;
        var glucoseMin = readings.Min(r => r.Value);
        var glucoseMax = readings.Max(r => r.Value);
        var glucoseAvg = Math.Round(readings.Average(r => r.Value), 1);
        double? glucoseSpike = null;
        DateTime? peakTime = null;

        var afterReadings = readings.Where(r => r.Timestamp >= eventTs).ToList();
        if (afterReadings.Count > 0)
        {
            var peak = afterReadings.MaxBy(r => r.Value)!;
            peakTime = DateTime.SpecifyKind(peak.Timestamp, DateTimeKind.Utc);
            glucoseSpike = Math.Round(peak.Value - glucoseAtEvent, 1);
        }

        return new GlucoseStats(
            GlucoseAtEvent: glucoseAtEvent,
            Min: glucoseMin,
            Max: glucoseMax,
            Avg: glucoseAvg,
            Spike: glucoseSpike,
            PeakTime: peakTime,
            ReadingCount: readings.Count);
    }

    /// <summary>
    /// Compute day-level statistics: min, max, avg, stddev, and time-in-range percentages.
    /// </summary>
    public static DayGlucoseStats ComputeDayStats(IReadOnlyList<GlucoseReading> readings)
    {
        if (readings.Count == 0)
            return DayGlucoseStats.Empty;

        var min = readings.Min(r => r.Value);
        var max = readings.Max(r => r.Value);
        var avg = Math.Round(readings.Average(r => r.Value), 1);

        var mean = readings.Average(r => r.Value);
        var sumSquares = readings.Sum(r => (r.Value - mean) * (r.Value - mean));
        var stdDev = Math.Round(Math.Sqrt(sumSquares / readings.Count), 1);

        var inRange = readings.Count(r => r.Value >= 70 && r.Value <= 180);
        var above = readings.Count(r => r.Value > 180);
        var below = readings.Count(r => r.Value < 70);

        var timeInRange = Math.Round(100.0 * inRange / readings.Count, 1);
        var timeAboveRange = Math.Round(100.0 * above / readings.Count, 1);
        var timeBelowRange = Math.Round(100.0 * below / readings.Count, 1);

        return new DayGlucoseStats(
            Min: min,
            Max: max,
            Avg: avg,
            StdDev: stdDev,
            TimeInRange: timeInRange,
            TimeAboveRange: timeAboveRange,
            TimeBelowRange: timeBelowRange,
            ReadingCount: readings.Count,
            FirstReadingUtc: readings.First().Timestamp,
            LastReadingUtc: readings.Last().Timestamp);
    }

    /// <summary>
    /// Check if two nullable doubles are effectively equal (within tolerance).
    /// </summary>
    public static bool NullableDoubleEquals(double? a, double? b, double tolerance = 0.01)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return Math.Abs(a.Value - b.Value) < tolerance;
    }
}

// ── Value Objects ──────────────────────────────────────────────

/// <summary>
/// Immutable value object representing glucose statistics for an event period.
/// </summary>
public record GlucoseStats(
    double? GlucoseAtEvent,
    double? Min,
    double? Max,
    double? Avg,
    double? Spike,
    DateTime? PeakTime,
    int ReadingCount)
{
    public static readonly GlucoseStats Empty = new(null, null, null, null, null, null, 0);
}

/// <summary>
/// Immutable value object representing day-level glucose statistics.
/// </summary>
public record DayGlucoseStats(
    double? Min,
    double? Max,
    double? Avg,
    double? StdDev,
    double? TimeInRange,
    double? TimeAboveRange,
    double? TimeBelowRange,
    int ReadingCount,
    DateTime? FirstReadingUtc,
    DateTime? LastReadingUtc)
{
    public static readonly DayGlucoseStats Empty = new(null, null, null, null, null, null, null, 0, null, null);
}
