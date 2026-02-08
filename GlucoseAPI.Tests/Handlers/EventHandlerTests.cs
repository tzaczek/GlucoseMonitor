using FluentAssertions;
using GlucoseAPI.Application.Features.Events;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GlucoseAPI.Tests.Handlers;

public class EventHandlerTests : IDisposable
{
    private readonly GlucoseDbContext _db;

    public EventHandlerTests()
    {
        var options = new DbContextOptionsBuilder<GlucoseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GlucoseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── GetEvents ────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_ReturnsEventsOrderedByTimestamp()
    {
        _db.GlucoseEvents.AddRange(
            TestEvent("Old Event", DateTime.UtcNow.AddDays(-2)),
            TestEvent("New Event", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var handler = new GetEventsHandler(_db);
        var result = await handler.Handle(new GetEventsQuery(null), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].NoteTitle.Should().Be("New Event");
    }

    [Fact]
    public async Task GetEvents_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
            _db.GlucoseEvents.Add(TestEvent($"Event {i}", DateTime.UtcNow.AddHours(-i)));
        await _db.SaveChangesAsync();

        var handler = new GetEventsHandler(_db);
        var result = await handler.Handle(new GetEventsQuery(2), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEvents_IncludesAnalysisCounts()
    {
        var evt = TestEvent("Lunch", DateTime.UtcNow);
        _db.GlucoseEvents.Add(evt);
        await _db.SaveChangesAsync();

        // Add 3 analysis history entries
        for (int i = 0; i < 3; i++)
        {
            _db.EventAnalysisHistory.Add(new EventAnalysisHistory
            {
                GlucoseEventId = evt.Id,
                AiAnalysis = $"Analysis {i}",
                AnalyzedAt = DateTime.UtcNow.AddMinutes(-i * 10),
                PeriodStart = evt.PeriodStart,
                PeriodEnd = evt.PeriodEnd,
                Reason = "test"
            });
        }
        await _db.SaveChangesAsync();

        var handler = new GetEventsHandler(_db);
        var result = await handler.Handle(new GetEventsQuery(null), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].AnalysisCount.Should().Be(3);
    }

    [Fact]
    public async Task GetEvents_TruncatesLongContent()
    {
        var evt = TestEvent("Long", DateTime.UtcNow);
        evt.NoteContent = new string('A', 200); // longer than 120 chars
        _db.GlucoseEvents.Add(evt);
        await _db.SaveChangesAsync();

        var handler = new GetEventsHandler(_db);
        var result = await handler.Handle(new GetEventsQuery(null), CancellationToken.None);

        result[0].NoteContentPreview.Should().HaveLength(121); // 120 + "…"
        result[0].NoteContentPreview.Should().EndWith("…");
    }

    // ── GetEventDetail ───────────────────────────────────────

    [Fact]
    public async Task GetEventDetail_NotFound_ReturnsNull()
    {
        var handler = new GetEventDetailHandler(_db);
        var result = await handler.Handle(new GetEventDetailQuery(9999), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEventDetail_RecalculatesStatsFromReadings()
    {
        var eventTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var evt = TestEvent("Lunch", eventTime);
        _db.GlucoseEvents.Add(evt);

        // Add readings in the event's period
        _db.GlucoseReadings.AddRange(
            Reading(100, eventTime.AddMinutes(-30)),
            Reading(110, eventTime),
            Reading(160, eventTime.AddMinutes(45)),
            Reading(130, eventTime.AddMinutes(90)));
        await _db.SaveChangesAsync();

        var handler = new GetEventDetailHandler(_db);
        var result = await handler.Handle(new GetEventDetailQuery(evt.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.GlucoseAtEvent.Should().Be(110); // closest to event time
        result.GlucoseMin.Should().Be(100);
        result.GlucoseMax.Should().Be(160);
        result.ReadingCount.Should().Be(4);
        result.Readings.Should().HaveCount(4);
    }

    // ── GetEventsStatus ──────────────────────────────────────

    [Fact]
    public async Task GetEventsStatus_ReturnsCorrectCounts()
    {
        var evt1 = TestEvent("Processed", DateTime.UtcNow.AddDays(-1));
        evt1.IsProcessed = true;
        var evt2 = TestEvent("Not Processed", DateTime.UtcNow);
        evt2.IsProcessed = false;

        _db.GlucoseEvents.AddRange(evt1, evt2);
        await _db.SaveChangesAsync();

        var handler = new GetEventsStatusHandler(_db);
        var result = await handler.Handle(new GetEventsStatusQuery(), CancellationToken.None);

        result.TotalEvents.Should().Be(2);
        result.ProcessedEvents.Should().Be(1);
        result.PendingEvents.Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────

    private static GlucoseEvent TestEvent(string title, DateTime ts) => new()
    {
        NoteTitle = title,
        NoteContent = "Test content",
        NoteUuid = $"uuid-{Guid.NewGuid():N}",
        EventTimestamp = DateTime.SpecifyKind(ts, DateTimeKind.Utc),
        PeriodStart = DateTime.SpecifyKind(ts.AddHours(-2), DateTimeKind.Utc),
        PeriodEnd = DateTime.SpecifyKind(ts.AddHours(3), DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static GlucoseReading Reading(double value, DateTime ts) => new()
    {
        Value = value,
        Timestamp = DateTime.SpecifyKind(ts, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow
    };
}
