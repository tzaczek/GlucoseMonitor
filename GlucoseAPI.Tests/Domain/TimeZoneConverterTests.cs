using FluentAssertions;
using GlucoseAPI.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GlucoseAPI.Tests.Domain;

/// <summary>
/// Unit tests for <see cref="TimeZoneConverter"/>.
/// </summary>
public class TimeZoneConverterTests
{
    private readonly TimeZoneConverter _converter = new(NullLogger<TimeZoneConverter>.Instance);

    [Fact]
    public void Resolve_ValidTimezone_ReturnsCorrectTimezone()
    {
        var tz = _converter.Resolve("Europe/Warsaw");

        tz.Should().NotBeNull();
        // Timezone ID differs across OS: "Europe/Warsaw" on Linux, "Central European Standard Time" on Windows
        (tz.Id.Contains("Warsaw") || tz.Id.Contains("Central European")).Should().BeTrue();
    }

    [Fact]
    public void Resolve_NullTimezone_ReturnsUtc()
    {
        var tz = _converter.Resolve(null);

        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void Resolve_EmptyTimezone_ReturnsUtc()
    {
        var tz = _converter.Resolve("");

        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void Resolve_InvalidTimezone_ReturnsUtcFallback()
    {
        var tz = _converter.Resolve("Invalid/Timezone");

        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void ToLocal_ConvertsUtcToLocal()
    {
        var utc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");

        var local = TimeZoneConverter.ToLocal(utc, tz);

        local.Should().Be(utc); // UTC → UTC = same time
    }

    [Fact]
    public void ToUtc_ConvertsLocalToUtc()
    {
        var local = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var tz = TimeZoneInfo.Utc;

        var utc = TimeZoneConverter.ToUtc(local, tz);

        utc.Should().Be(local);
    }

    [Fact]
    public void GetDayBoundariesUtc_ReturnsCorrectBoundaries_ForUtc()
    {
        var date = new DateTime(2025, 6, 15);
        var tz = TimeZoneInfo.Utc;

        var (start, end) = TimeZoneConverter.GetDayBoundariesUtc(date, tz);

        start.Should().Be(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetDayBoundariesUtc_SpansFullDay()
    {
        var date = new DateTime(2025, 1, 1);
        var tz = TimeZoneInfo.Utc;

        var (start, end) = TimeZoneConverter.GetDayBoundariesUtc(date, tz);

        (end - start).TotalHours.Should().Be(24);
    }
}
