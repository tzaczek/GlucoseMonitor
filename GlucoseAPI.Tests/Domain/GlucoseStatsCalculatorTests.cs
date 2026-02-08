using FluentAssertions;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Xunit;

namespace GlucoseAPI.Tests.Domain;

/// <summary>
/// Unit tests for <see cref="GlucoseStatsCalculator"/>.
/// These are pure domain logic tests with no I/O or mocks.
/// </summary>
public class GlucoseStatsCalculatorTests
{
    // ────────────────────────────────────────────────────────────
    // ComputeEventStats
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeEventStats_EmptyReadings_ReturnsEmptyStats()
    {
        var result = GlucoseStatsCalculator.ComputeEventStats(
            Array.Empty<GlucoseReading>(),
            DateTime.UtcNow);

        result.Should().Be(GlucoseStats.Empty);
        result.ReadingCount.Should().Be(0);
        result.GlucoseAtEvent.Should().BeNull();
    }

    [Fact]
    public void ComputeEventStats_SingleReading_ReturnsCorrectStats()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(120, eventTime)
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        result.GlucoseAtEvent.Should().Be(120);
        result.Min.Should().Be(120);
        result.Max.Should().Be(120);
        result.Avg.Should().Be(120);
        result.ReadingCount.Should().Be(1);
    }

    [Fact]
    public void ComputeEventStats_MultipleReadings_FindsClosestToEventTime()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, eventTime.AddMinutes(-30)),
            CreateReading(110, eventTime.AddMinutes(-2)),  // closest before
            CreateReading(115, eventTime.AddMinutes(3)),   // closest after
            CreateReading(140, eventTime.AddMinutes(30)),
            CreateReading(130, eventTime.AddMinutes(60)),
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        result.GlucoseAtEvent.Should().Be(110); // 2 min before is closer than 3 min after
        result.Min.Should().Be(100);
        result.Max.Should().Be(140);
        result.ReadingCount.Should().Be(5);
    }

    [Fact]
    public void ComputeEventStats_CalculatesSpike_AsPeakMinusBaseline()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, eventTime.AddMinutes(-15)),
            CreateReading(105, eventTime),                 // closest to event
            CreateReading(130, eventTime.AddMinutes(30)),  // peak after event
            CreateReading(120, eventTime.AddMinutes(60)),
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        result.GlucoseAtEvent.Should().Be(105);
        result.Spike.Should().Be(25); // 130 (peak after) - 105 (at event)
        result.PeakTime.Should().Be(eventTime.AddMinutes(30));
    }

    [Fact]
    public void ComputeEventStats_NoReadingsAfterEvent_NoSpike()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, eventTime.AddMinutes(-30)),
            CreateReading(110, eventTime.AddMinutes(-5)),
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        result.Spike.Should().BeNull();
        result.PeakTime.Should().BeNull();
    }

    [Fact]
    public void ComputeEventStats_NegativeSpike_WhenGlucoseDropsAfterEvent()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(150, eventTime.AddMinutes(-15)),
            CreateReading(140, eventTime),      // at event (closest)
            CreateReading(120, eventTime.AddMinutes(30)), // after event: max is still 140 (at event itself)
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        // "After event" includes the reading AT event time (>= eventTs)
        // Max of [140, 120] = 140, so spike = 140 - 140 = 0
        result.Spike.Should().Be(0);
    }

    [Fact]
    public void ComputeEventStats_CalculatesAverage_Correctly()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, eventTime.AddMinutes(-10)),
            CreateReading(120, eventTime),
            CreateReading(140, eventTime.AddMinutes(10)),
        };

        var result = GlucoseStatsCalculator.ComputeEventStats(readings, eventTime);

        result.Avg.Should().Be(120); // (100 + 120 + 140) / 3
    }

    // ────────────────────────────────────────────────────────────
    // ComputeDayStats
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeDayStats_EmptyReadings_ReturnsEmptyStats()
    {
        var result = GlucoseStatsCalculator.ComputeDayStats(Array.Empty<GlucoseReading>());

        result.Should().Be(DayGlucoseStats.Empty);
        result.ReadingCount.Should().Be(0);
    }

    [Fact]
    public void ComputeDayStats_AllInRange_Returns100PercentTIR()
    {
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var readings = Enumerable.Range(0, 48).Select(i =>
            CreateReading(100 + (i % 20), baseTime.AddMinutes(i * 30))).ToList();

        var result = GlucoseStatsCalculator.ComputeDayStats(readings);

        result.TimeInRange.Should().Be(100);
        result.TimeAboveRange.Should().Be(0);
        result.TimeBelowRange.Should().Be(0);
        result.ReadingCount.Should().Be(48);
    }

    [Fact]
    public void ComputeDayStats_MixedRange_CalculatesPercentagesCorrectly()
    {
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            // 2 below range
            CreateReading(60, baseTime),
            CreateReading(65, baseTime.AddMinutes(5)),
            // 4 in range
            CreateReading(100, baseTime.AddMinutes(10)),
            CreateReading(120, baseTime.AddMinutes(15)),
            CreateReading(140, baseTime.AddMinutes(20)),
            CreateReading(170, baseTime.AddMinutes(25)),
            // 4 above range
            CreateReading(190, baseTime.AddMinutes(30)),
            CreateReading(200, baseTime.AddMinutes(35)),
            CreateReading(210, baseTime.AddMinutes(40)),
            CreateReading(250, baseTime.AddMinutes(45)),
        };

        var result = GlucoseStatsCalculator.ComputeDayStats(readings);

        result.TimeBelowRange.Should().Be(20); // 2/10 = 20%
        result.TimeInRange.Should().Be(40);    // 4/10 = 40%
        result.TimeAboveRange.Should().Be(40); // 4/10 = 40%
        result.ReadingCount.Should().Be(10);
    }

    [Fact]
    public void ComputeDayStats_CalculatesStdDev_Correctly()
    {
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, baseTime),
            CreateReading(100, baseTime.AddMinutes(5)),
            CreateReading(100, baseTime.AddMinutes(10)),
        };

        var result = GlucoseStatsCalculator.ComputeDayStats(readings);

        result.StdDev.Should().Be(0); // All same value → std dev = 0
        result.Avg.Should().Be(100);
    }

    [Fact]
    public void ComputeDayStats_RecordsFirstAndLastReadingTimestamps()
    {
        var first = new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var last = new DateTime(2025, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        var readings = new List<GlucoseReading>
        {
            CreateReading(100, first),
            CreateReading(120, first.AddHours(8)),
            CreateReading(110, last),
        };

        var result = GlucoseStatsCalculator.ComputeDayStats(readings);

        result.FirstReadingUtc.Should().Be(first);
        result.LastReadingUtc.Should().Be(last);
    }

    // ────────────────────────────────────────────────────────────
    // NullableDoubleEquals
    // ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, 1.0, false)]
    [InlineData(1.0, null, false)]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.0, 1.005, true)]   // within tolerance
    [InlineData(1.0, 1.02, false)]   // outside tolerance
    public void NullableDoubleEquals_VariousCases(double? a, double? b, bool expected)
    {
        GlucoseStatsCalculator.NullableDoubleEquals(a, b).Should().Be(expected);
    }

    // ── Test Helpers ──────────────────────────────────────────

    private static GlucoseReading CreateReading(double value, DateTime timestamp) => new()
    {
        Value = value,
        Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow
    };
}
