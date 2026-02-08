using FluentAssertions;
using GlucoseAPI.Application.Features.Glucose;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GlucoseAPI.Tests.Handlers;

public class GlucoseHandlerTests : IDisposable
{
    private readonly GlucoseDbContext _db;

    public GlucoseHandlerTests()
    {
        var options = new DbContextOptionsBuilder<GlucoseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GlucoseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── GetLatestReading ─────────────────────────────────────

    [Fact]
    public async Task GetLatestReading_EmptyDb_ReturnsNull()
    {
        var handler = new GetLatestReadingHandler(_db);
        var result = await handler.Handle(new GetLatestReadingQuery(), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestReading_ReturnsNewestReading()
    {
        _db.GlucoseReadings.AddRange(
            Reading(100, DateTime.UtcNow.AddMinutes(-30)),
            Reading(120, DateTime.UtcNow.AddMinutes(-15)),
            Reading(140, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var handler = new GetLatestReadingHandler(_db);
        var result = await handler.Handle(new GetLatestReadingQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Value.Should().Be(140);
    }

    // ── GetGlucoseHistory ────────────────────────────────────

    [Fact]
    public async Task GetGlucoseHistory_AppliesLimit()
    {
        // Add 5 readings within the last hour
        for (int i = 0; i < 5; i++)
            _db.GlucoseReadings.Add(Reading(100 + i * 10, DateTime.UtcNow.AddMinutes(-i * 5)));
        await _db.SaveChangesAsync();

        var handler = new GetGlucoseHistoryHandler(_db);
        var result = await handler.Handle(new GetGlucoseHistoryQuery(2, 3), CancellationToken.None);

        result.Should().HaveCount(3);
        // Results should be descending by timestamp (most recent first)
        result[0].Timestamp.Should().BeOnOrAfter(result[1].Timestamp);
    }

    // ── GetGlucoseStats ──────────────────────────────────────

    [Fact]
    public async Task GetGlucoseStats_EmptyDb_ReturnsNull()
    {
        var handler = new GetGlucoseStatsHandler(_db);
        var result = await handler.Handle(new GetGlucoseStatsQuery(24), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGlucoseStats_CalculatesCorrectly()
    {
        // 3 in range (70-180), 1 above, 1 below
        _db.GlucoseReadings.AddRange(
            Reading(60, DateTime.UtcNow.AddMinutes(-60)),  // below
            Reading(100, DateTime.UtcNow.AddMinutes(-45)), // in range
            Reading(150, DateTime.UtcNow.AddMinutes(-30)), // in range
            Reading(170, DateTime.UtcNow.AddMinutes(-15)), // in range
            Reading(200, DateTime.UtcNow));                 // above
        await _db.SaveChangesAsync();

        var handler = new GetGlucoseStatsHandler(_db);
        var result = await handler.Handle(new GetGlucoseStatsQuery(2), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Min.Should().Be(60);
        result.Max.Should().Be(200);
        result.Average.Should().Be(136); // (60+100+150+170+200)/5 = 136
        result.TotalReadings.Should().Be(5);
        result.TimeInRange.Should().Be(60); // 3 out of 5 = 60%
        result.LatestReading.Should().NotBeNull();
        result.LatestReading!.Value.Should().Be(200);
    }

    // ── GetDatesWithReadings ─────────────────────────────────

    [Fact]
    public async Task GetDatesWithReadings_ReturnsDistinctDates()
    {
        _db.GlucoseReadings.AddRange(
            Reading(100, new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)),
            Reading(110, new DateTime(2025, 1, 1, 14, 0, 0, DateTimeKind.Utc)),
            Reading(120, new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var handler = new GetDatesWithReadingsHandler(_db);
        var result = await handler.Handle(new GetDatesWithReadingsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain("2025-01-02");
        result.Should().Contain("2025-01-01");
    }

    // ── Helpers ──────────────────────────────────────────────

    private static GlucoseReading Reading(double value, DateTime ts) => new()
    {
        Value = value,
        Timestamp = DateTime.SpecifyKind(ts, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow
    };
}
