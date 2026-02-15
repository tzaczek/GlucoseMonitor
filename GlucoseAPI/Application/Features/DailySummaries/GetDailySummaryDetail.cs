using GlucoseAPI.Application.Features.Events;
using GlucoseAPI.Application.Features.Glucose;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.DailySummaries;

public record GetDailySummaryDetailQuery(int Id) : IRequest<DailySummaryDetailDto?>;

public class GetDailySummaryDetailHandler
    : IRequestHandler<GetDailySummaryDetailQuery, DailySummaryDetailDto?>
{
    private readonly GlucoseDbContext _db;

    public GetDailySummaryDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<DailySummaryDetailDto?> Handle(GetDailySummaryDetailQuery request, CancellationToken ct)
    {
        var summary = await _db.DailySummaries.FindAsync(new object[] { request.Id }, ct);
        if (summary == null) return null;

        var events = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= summary.PeriodStartUtc && e.EventTimestamp < summary.PeriodEndUtc)
            .OrderByDescending(e => e.EventTimestamp)
            .ToListAsync(ct);

        var eventIds = events.Select(e => e.Id).ToList();
        var analysisCounts = await _db.EventAnalysisHistory
            .Where(h => eventIds.Contains(h.GlucoseEventId))
            .GroupBy(h => h.GlucoseEventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, ct);

        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= summary.PeriodStartUtc && r.Timestamp < summary.PeriodEndUtc)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var snapshots = await _db.DailySummarySnapshots
            .Where(snap => snap.DailySummaryId == summary.Id)
            .OrderByDescending(snap => snap.GeneratedAt)
            .ToListAsync(ct);

        return new DailySummaryDetailDto
        {
            Id = summary.Id,
            Date = summary.Date,
            PeriodStartUtc = DateTime.SpecifyKind(summary.PeriodStartUtc, DateTimeKind.Utc),
            PeriodEndUtc = DateTime.SpecifyKind(summary.PeriodEndUtc, DateTimeKind.Utc),
            TimeZone = summary.TimeZone,
            EventCount = summary.EventCount,
            EventIds = summary.EventIds,
            EventTitles = summary.EventTitles,
            ReadingCount = summary.ReadingCount,
            GlucoseMin = summary.GlucoseMin,
            GlucoseMax = summary.GlucoseMax,
            GlucoseAvg = summary.GlucoseAvg,
            GlucoseStdDev = summary.GlucoseStdDev,
            TimeInRange = summary.TimeInRange,
            TimeAboveRange = summary.TimeAboveRange,
            TimeBelowRange = summary.TimeBelowRange,
            AiAnalysis = summary.AiAnalysis,
            AiClassification = summary.AiClassification,
            AiModel = summary.AiModel,
            IsProcessed = summary.IsProcessed,
            ProcessedAt = summary.ProcessedAt.HasValue
                ? DateTime.SpecifyKind(summary.ProcessedAt.Value, DateTimeKind.Utc)
                : null,
            SnapshotCount = snapshots.Count,
            Snapshots = snapshots.Select(MapSnapshotDto).ToList(),
            Events = events.Select(e => GetEventsHandler.MapToSummaryDto(e, analysisCounts)).ToList(),
            Readings = readings.Select(GetLatestReadingHandler.MapToDto).ToList()
        };
    }

    internal static DailySummarySnapshotDto MapSnapshotDto(DailySummarySnapshot snap) => new()
    {
        Id = snap.Id,
        GeneratedAt = DateTime.SpecifyKind(snap.GeneratedAt, DateTimeKind.Utc),
        Trigger = snap.Trigger,
        ReadingCount = snap.ReadingCount,
        EventCount = snap.EventCount,
        FirstReadingUtc = snap.FirstReadingUtc.HasValue
            ? DateTime.SpecifyKind(snap.FirstReadingUtc.Value, DateTimeKind.Utc)
            : null,
        LastReadingUtc = snap.LastReadingUtc.HasValue
            ? DateTime.SpecifyKind(snap.LastReadingUtc.Value, DateTimeKind.Utc)
            : null,
        GlucoseAvg = snap.GlucoseAvg,
        GlucoseMin = snap.GlucoseMin,
        GlucoseMax = snap.GlucoseMax,
        TimeInRange = snap.TimeInRange,
        IsProcessed = snap.IsProcessed,
        HasAnalysis = !string.IsNullOrEmpty(snap.AiAnalysis),
        AiClassification = snap.AiClassification
    };
}
