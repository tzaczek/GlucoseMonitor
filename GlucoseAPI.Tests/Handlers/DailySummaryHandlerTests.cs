using FluentAssertions;
using GlucoseAPI.Application.Features.DailySummaries;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GlucoseAPI.Tests.Handlers;

public class DailySummaryHandlerTests : IDisposable
{
    private readonly GlucoseDbContext _db;

    public DailySummaryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<GlucoseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GlucoseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── GetDailySummaries ────────────────────────────────────

    [Fact]
    public async Task GetDailySummaries_ReturnsOrderedByDate()
    {
        _db.DailySummaries.AddRange(
            TestSummary(new DateTime(2025, 1, 1)),
            TestSummary(new DateTime(2025, 1, 3)),
            TestSummary(new DateTime(2025, 1, 2)));
        await _db.SaveChangesAsync();

        var handler = new GetDailySummariesHandler(_db);
        var result = await handler.Handle(new GetDailySummariesQuery(null), CancellationToken.None);

        result.Should().HaveCount(3);
        result[0].Date.Should().Be(new DateTime(2025, 1, 3));
        result[1].Date.Should().Be(new DateTime(2025, 1, 2));
        result[2].Date.Should().Be(new DateTime(2025, 1, 1));
    }

    [Fact]
    public async Task GetDailySummaries_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
            _db.DailySummaries.Add(TestSummary(new DateTime(2025, 1, i + 1)));
        await _db.SaveChangesAsync();

        var handler = new GetDailySummariesHandler(_db);
        var result = await handler.Handle(new GetDailySummariesQuery(2), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDailySummaries_IncludesSnapshotCounts()
    {
        var summary = TestSummary(new DateTime(2025, 6, 1));
        _db.DailySummaries.Add(summary);
        await _db.SaveChangesAsync();

        _db.DailySummarySnapshots.AddRange(
            TestSnapshot(summary.Id, new DateTime(2025, 6, 1)),
            TestSnapshot(summary.Id, new DateTime(2025, 6, 1)));
        await _db.SaveChangesAsync();

        var handler = new GetDailySummariesHandler(_db);
        var result = await handler.Handle(new GetDailySummariesQuery(null), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].SnapshotCount.Should().Be(2);
    }

    // ── GetDailySummaryDetail ────────────────────────────────

    [Fact]
    public async Task GetDailySummaryDetail_NotFound_ReturnsNull()
    {
        var handler = new GetDailySummaryDetailHandler(_db);
        var result = await handler.Handle(new GetDailySummaryDetailQuery(9999), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDailySummaryDetail_IncludesEventsAndReadings()
    {
        var summary = TestSummary(new DateTime(2025, 6, 1));
        _db.DailySummaries.Add(summary);
        await _db.SaveChangesAsync();

        // Add event within period
        _db.GlucoseEvents.Add(new GlucoseEvent
        {
            NoteTitle = "Lunch",
            NoteUuid = "uuid-lunch",
            NoteContent = "Ate pasta",
            EventTimestamp = summary.PeriodStartUtc.AddHours(12),
            PeriodStart = summary.PeriodStartUtc.AddHours(10),
            PeriodEnd = summary.PeriodStartUtc.AddHours(15),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Add reading within period
        _db.GlucoseReadings.Add(new GlucoseReading
        {
            Value = 120,
            Timestamp = summary.PeriodStartUtc.AddHours(12),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var handler = new GetDailySummaryDetailHandler(_db);
        var result = await handler.Handle(new GetDailySummaryDetailQuery(summary.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Readings.Should().HaveCount(1);
    }

    // ── GetDailySummariesStatus ──────────────────────────────

    [Fact]
    public async Task GetDailySummariesStatus_ReturnsCorrectCounts()
    {
        var s1 = TestSummary(new DateTime(2025, 1, 1));
        s1.IsProcessed = true;
        var s2 = TestSummary(new DateTime(2025, 1, 2));
        s2.IsProcessed = false;
        var s3 = TestSummary(new DateTime(2025, 1, 3));
        s3.IsProcessed = false;

        _db.DailySummaries.AddRange(s1, s2, s3);
        await _db.SaveChangesAsync();

        var handler = new GetDailySummariesStatusHandler(_db);
        var result = await handler.Handle(new GetDailySummariesStatusQuery(), CancellationToken.None);

        result.TotalSummaries.Should().Be(3);
        result.ProcessedSummaries.Should().Be(1);
        result.PendingSummaries.Should().Be(2);
    }

    // ── GetSnapshotDetail ────────────────────────────────────

    [Fact]
    public async Task GetSnapshotDetail_NotFound_ReturnsNull()
    {
        var handler = new GetSnapshotDetailHandler(_db);
        var result = await handler.Handle(new GetSnapshotDetailQuery(9999), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSnapshotDetail_ReturnsAllFields()
    {
        var summary = TestSummary(new DateTime(2025, 6, 1));
        _db.DailySummaries.Add(summary);
        await _db.SaveChangesAsync();

        var snap = TestSnapshot(summary.Id, new DateTime(2025, 6, 1));
        snap.AiAnalysis = "Good day";
        snap.AiClassification = "green";
        _db.DailySummarySnapshots.Add(snap);
        await _db.SaveChangesAsync();

        var handler = new GetSnapshotDetailHandler(_db);
        var result = await handler.Handle(new GetSnapshotDetailQuery(snap.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.AiAnalysis.Should().Be("Good day");
        result.AiClassification.Should().Be("green");
        result.HasAnalysis.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────

    private static DailySummary TestSummary(DateTime date) => new()
    {
        Date = date,
        PeriodStartUtc = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        PeriodEndUtc = DateTime.SpecifyKind(date.AddDays(1), DateTimeKind.Utc),
        TimeZone = "UTC",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static DailySummarySnapshot TestSnapshot(int summaryId, DateTime date) => new()
    {
        DailySummaryId = summaryId,
        Date = date,
        GeneratedAt = DateTime.UtcNow,
        Trigger = "test",
        DataStartUtc = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        DataEndUtc = DateTime.SpecifyKind(date.AddDays(1), DateTimeKind.Utc),
        TimeZone = "UTC"
    };
}
